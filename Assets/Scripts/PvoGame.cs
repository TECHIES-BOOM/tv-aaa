using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Bunker anti-aircraft game: rounds, scenarios, resolution cinematics, HUD.</summary>
public class PvoGame : MonoBehaviour
{
    enum State { Menu, Play, Busy, Over }
    enum Kind { Enemy, HeightErr, Allies, Fake, SectorErr }
    static readonly string[] Sec = { "A1", "A2", "A3", "Б1", "Б2", "Б3", "В1", "В2", "В3" };

    [Header("Scene refs (PVO > Build Scene fills these)")]
    public Camera cam;
    public Transform deskView, windowView, outside;
    public Transform knobSector, knobHeight, receiver, fireCover, fireButton, paper, radarSweep;
    public TextMesh telegraphText, radioText;
    public LineRenderer radioWave;
    public Transform[] radarDots;
    public Renderer radarScreen, glass;
    public Light lamp, moon, flash;
    public Volume volume;
    public float radarSize = .46f;

    [Header("Your own sounds (optional - empty = synthesised placeholders)")]
    public AudioClip snd_telegraph_print, snd_knob_click, snd_radio_static, snd_radio_tuned, snd_phone_dial,
        snd_pvo_fire, snd_explosion_far, snd_explosion_heavy, snd_bomber_plane, snd_ambient_bunker, snd_siren;

    State state = State.Menu; Kind kind, lastKind = (Kind)(-1);
    bool enemy, armed, phoneBusy, windowLook, jam, deadLine;
    int tS, tH, tN, rS, rH, rN, tuneS, tuneH, round, score, streak, lives = 3, calls, best, radioMode = -1;
    float moonBase = .35f, timeLeft, timeMax = 40, shake, flashK, phoneTill, pressT, overAt, phantomT;
    string msg = "", phoneShown = "", overText = "";
    readonly Vector2[] dp = new Vector2[6];
    AudioSource sfx, radio, amb;
    Bloom bloom; Vignette vig; ChromaticAberration chroma; ColorAdjustments adj;
    Vector3 paperRest, receiverRest, btnRest, camPos; Quaternion coverClosed, camRot;
    PvoClickable hover; LightFlicker lampFlicker; Material glassMat;
    readonly HashSet<Transform> dead = new HashSet<Transform>();
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    GUIStyle st;

    bool Tuned { get { return tuneS == rS && tuneH == rH; } }

    // ---------------------------------------------------------------- setup
    void Start()
    {
        if (!cam) cam = Camera.main;
        sfx = gameObject.AddComponent<AudioSource>(); sfx.spatialBlend = 0;
        radio = gameObject.AddComponent<AudioSource>(); radio.loop = true; radio.volume = 0; radio.spatialBlend = 0;
        amb = gameObject.AddComponent<AudioSource>(); amb.loop = true; amb.volume = .5f; amb.spatialBlend = 0;
        amb.clip = Clip("amb", snd_ambient_bunker, PvoSynth.Hum); amb.Play();
        if (volume != null)
        {
            var p = volume.profile; p.TryGet(out bloom); p.TryGet(out vig); p.TryGet(out chroma); p.TryGet(out adj);
        }
        paperRest = paper.localPosition; receiverRest = receiver.localPosition; btnRest = fireButton.localPosition; coverClosed = fireCover.localRotation;
        if (lamp) lampFlicker = lamp.GetComponent<LightFlicker>();
        if (moon) moonBase = moon.intensity;
        radarScreen.material.SetTexture("_BaseMap", PvoFx.RadarGrid());
        radarSweep.GetComponent<Renderer>().material.SetTexture("_BaseMap", PvoFx.Sweep());
        if (glass) { glassMat = glass.material; glassMat.SetTexture("_BaseMap", PvoFx.Cracks()); SetCrack(0); }
        PvoFx.Stars(); PvoFx.Dust(transform);
        paper.localPosition = paperRest + Vector3.down * .25f;
        camPos = deskView.position; camRot = deskView.rotation; cam.transform.SetPositionAndRotation(camPos, camRot);
        foreach (var d in radarDots) d.gameObject.SetActive(false);
        tuneS = 0; tuneH = 0; rS = rH = -1;
    }

    AudioClip Clip(string key, AudioClip own, System.Func<AudioClip> make)
    {
        if (own) return own;
        AudioClip c; if (!cache.TryGetValue(key, out c)) cache[key] = c = make(); return c;
    }
    void Play(AudioClip c, float v = 1f) { sfx.PlayOneShot(c, v); }
    void Click() { Play(Clip("click", snd_knob_click, PvoSynth.Click), .8f); }
    void Boom(bool heavy)
    {
        if (heavy) Play(Clip("boomH", snd_explosion_heavy, () => PvoSynth.Boom(2.8f, .3f, 1f)), 1f);
        else Play(Clip("boomF", snd_explosion_far, () => PvoSynth.Boom(2.2f, .12f, .9f)), .85f);
    }
    void SetCrack(float a) { if (glassMat != null) glassMat.SetColor("_BaseColor", new Color(1, 1, 1, a)); }
    Vector3 W(float x, float y, float z) { return outside.TransformPoint(new Vector3(x, y, z)); }

    // ---------------------------------------------------------------- loop
    void Update()
    {
        float dt = Time.deltaTime;
        HandleInput();
        if (state == State.Play)
        {
            timeLeft -= dt;
            if (timeLeft <= 0f) { timeLeft = 0f; state = State.Busy; StartCoroutine(Timeout()); }
        }
        Animate(dt); UpdateRadio(); UpdateRadar(dt); UpdateFx(dt);
    }

    void HandleInput()
    {
        var ray = cam.ScreenPointToRay(PvoIn.Pointer());
        RaycastHit hit;
        hover = Physics.Raycast(ray, out hit, 8f) ? hit.collider.GetComponentInParent<PvoClickable>() : null;
        if (PvoIn.Down(PvoIn.K.Q)) windowLook = true;
        if (PvoIn.Down(PvoIn.K.E)) windowLook = false;
        if (PvoIn.Down(PvoIn.K.Tab)) windowLook = !windowLook;
        if (state == State.Menu || (state == State.Over && Time.time > overAt + 1f))
        {
            if (PvoIn.Down(PvoIn.K.Space) || PvoIn.Down(PvoIn.K.R) || PvoIn.Pressed(0)) NewGame();
            return;
        }
        if (state != State.Play || windowLook || hover == null) return;
        int d = PvoIn.Pressed(1) ? -1 : (PvoIn.Pressed(0) ? (PvoIn.Shift() ? -1 : 1) : 0);
        float w = PvoIn.Scroll();
        bool knob = hover.type == PvoClickable.Type.SectorKnob || hover.type == PvoClickable.Type.HeightKnob;
        if (d == 0 && knob && Mathf.Abs(w) > .01f) d = w > 0 ? 1 : -1;
        if (d != 0) Use(hover, d);
    }

    void Use(PvoClickable c, int d)
    {
        switch (c.type)
        {
            case PvoClickable.Type.SectorKnob: tuneS = (tuneS + d + 9) % 9; Click(); break;
            case PvoClickable.Type.HeightKnob: tuneH = (tuneH + d + 5) % 5; Click(); break;
            case PvoClickable.Type.Phone: if (!phoneBusy) StartCoroutine(Call()); break;
            case PvoClickable.Type.FireCover: armed = !armed; Click(); break;
            case PvoClickable.Type.FireButton:
                if (armed) StartCoroutine(Fire()); else { msg = "Спершу підніми захисну кришку."; }
                break;
        }
    }

    // ---------------------------------------------------------------- game flow
    void NewGame()
    {
        foreach (var g in spawned) if (g) Destroy(g);
        spawned.Clear(); dead.Clear();
        round = 0; score = 0; streak = 0; lives = 3; SetCrack(0); NewRound();
    }

    void NewRound()
    {
        round++; state = State.Play; armed = false; phoneBusy = false; calls = 0; msg = ""; phoneShown = ""; windowLook = false;
        Kind k; int guard = 0;
        do { k = (Kind)Random.Range(0, round <= 2 ? 3 : 5); } while (k == lastKind && guard++ < 20);
        lastKind = kind = k;
        tS = Random.Range(0, 9); tH = Random.Range(0, 5); tN = Random.Range(1, 5); rS = tS; rH = tH; rN = tN;
        enemy = k != Kind.Allies && k != Kind.Fake;
        string line = "Підтверджую, це ворог!";
        switch (k)
        {
            case Kind.HeightErr: do rH = Random.Range(0, 5); while (rH == tH); line = "Помилка! Висота " + (rH + 1) * 1000 + " метрів!"; break;
            case Kind.SectorErr: do rS = Random.Range(0, 9); while (rS == tS); line = "Штаб: вони у секторі " + Sec[rS] + "!"; break;
            case Kind.Allies: tN = rN = 1; line = "Це наші! Не стріляти!"; break;
            case Kind.Fake: tN = 4; rN = Random.value < .5f ? 0 : 4; line = "Це фальшивка ворога!"; break;
        }
        phoneLine = line;
        jam = round >= 4 && Random.value < .35f;
        deadLine = round >= 6 && Random.value < .3f;
        timeMax = Mathf.Max(24f, 42f - round * 1.5f); timeLeft = timeMax;
        do { tuneS = Random.Range(0, 9); tuneH = Random.Range(0, 5); } while (Tuned);
        for (int i = 0; i < dp.Length; i++) dp[i] = new Vector2(Random.Range(.25f, .75f), Random.Range(.25f, .75f));
        StartCoroutine(Print());
    }
    string phoneLine = "";

    IEnumerator Print()
    {
        telegraphText.text = ""; paper.localPosition = paperRest + Vector3.down * .25f;
        string full = "ТЕЛЕГРАМА №" + round + "\n[СЕКТОР: " + Sec[tS] + "]\n[ВИСОТА: " + (tH + 1) * 1000 + "м]\n[ЦІЛЕЙ: " + tN + "]";
        Play(Clip("print", snd_telegraph_print, PvoSynth.Print), .7f);
        for (int i = 0; i <= full.Length; i++)
        {
            telegraphText.text = full.Substring(0, i);
            if (i % 2 == 0) Play(Clip("key", null, PvoSynth.Key), .35f);
            yield return new WaitForSeconds(.03f);
        }
    }

    IEnumerator Call()
    {
        phoneBusy = true; calls++; phoneShown = "";
        Play(Clip("dial", snd_phone_dial, PvoSynth.Dial), .8f);
        yield return new WaitForSeconds(1.5f);
        phoneBusy = false;
        if (state != State.Play) yield break;
        string line = deadLine ? "...зв’язок обірвано. Повторіть виклик." : phoneLine; deadLine = false;
        Play(Clip("beep", null, PvoSynth.Beep), .6f);
        phoneShown = "ШТАБ: " + line; phoneTill = Time.time + 6f;
    }

    void Award(string text, bool hold)
    {
        streak++; int mult = Mathf.Min(1 + streak / 3, 4);
        int pts = hold ? Mathf.Max(50, 120 - calls * 30) : Mathf.Max(50, 100 + Mathf.RoundToInt(timeLeft * 6f) - calls * 30);
        pts *= mult; score += pts; msg = text + "  +" + pts + (mult > 1 ? "  (x" + mult + ")" : "");
    }

    IEnumerator Timeout()
    {
        if (enemy) yield return StartCoroutine(Bombard("Час вийшов — бункер під бомбами."));
        else
        {
            Award("Правильно: не стріляти.", true);
            yield return new WaitForSeconds(2.8f); NewRound();
        }
    }

    IEnumerator Fire()
    {
        state = State.Busy; windowLook = true; pressT = .4f; Click();
        yield return new WaitForSeconds(1f);
        bool allies = kind == Kind.Allies, hit = enemy && Tuned;
        int count = allies ? 1 : kind == Kind.Fake ? 0 : Mathf.Max(1, rN);
        var planes = new List<Transform>();
        for (int i = 0; i < count; i++)
        {
            var p = MakePlane(new Vector3(-100 - i * 36, 32 + (i % 2) * 9, 150 + i * 12), allies); planes.Add(p); StartCoroutine(Fly(p, 34f));
        }
        yield return StartCoroutine(Volley(planes, hit || allies, 2.2f));
        if (hit || allies)
        {
            foreach (var p in planes) StartCoroutine(Kill(p));
            yield return new WaitForSeconds(2.6f);
            if (allies)
            {
                Play(Clip("siren", snd_siren, PvoSynth.Siren), .8f); phoneShown = "ШТАБ: Ви збили наших!"; phoneTill = Time.time + 5f;
                yield return new WaitForSeconds(2.4f); GameOver("Ви збили своїх. Це була група «Альфа»."); yield break;
            }
            Award("Ціль знищено!", false);
            yield return new WaitForSeconds(2.8f); NewRound();
        }
        else yield return StartCoroutine(Bombard(enemy ? "Промах — залп пішов у порожнє небо." : "Залп на фальшивку — справжні бомбардувальники прорвалися."));
    }

    IEnumerator Bombard(string reason)
    {
        state = State.Busy; windowLook = true; armed = false;
        yield return new WaitForSeconds(.6f);
        Play(Clip("bomber", snd_bomber_plane, PvoSynth.Engine), .9f);
        for (int i = 0; i < 3; i++) { var p = MakePlane(new Vector3(-120 - i * 30, 44, 90 + i * 12), false); StartCoroutine(Fly(p, 44f)); }
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForSeconds(.5f);
            Vector3 impact = W(Mathf.Lerp(-40, 10, i / 5f) + Random.Range(-8f, 8f), -7.5f, Mathf.Lerp(200, 36, i / 5f));
            StartCoroutine(Bomb(impact, i == 5));
        }
        yield return new WaitForSeconds(1.8f);
        lives--; streak = 0; SetCrack((3 - lives) / 3f);
        if (lives <= 0) { GameOver(reason + " Бункер зруйновано."); yield break; }
        msg = reason + "  Цілість бункера: " + lives + "/3.";
        yield return new WaitForSeconds(3.4f); NewRound();
    }

    IEnumerator Bomb(Vector3 impact, bool last)
    {
        yield return new WaitForSeconds(Random.Range(.3f, .6f));
        PvoFx.Explosion(impact, last ? 40f : 20f); Boom(last); flashK = last ? 1.5f : .7f; shake = Mathf.Max(shake, last ? 1.1f : .25f);
        if (last) { if (lampFlicker) lampFlicker.Blackout(1f); PvoFx.DustBurst(new Vector3(0, 2.8f, -.4f)); }
    }

    void GameOver(string reason)
    {
        state = State.Over; overText = reason; best = Mathf.Max(best, score); overAt = Time.time; windowLook = false;
    }

    // ---------------------------------------------------------------- cinematics
    Transform MakePlane(Vector3 lp, bool navLights)
    {
        var g = new GameObject("Plane"); spawned.Add(g);
        g.transform.position = W(lp.x, lp.y, lp.z); g.transform.rotation = outside.rotation * Quaternion.Euler(0, 90, 0);
        var m = PvoFx.Lit(new Color(.045f, .05f, .055f), .2f);
        Part(g.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(3, 2.6f, 17), m);
        Part(g.transform, PrimitiveType.Cube, new Vector3(0, 0, 1), new Vector3(30, .5f, 4.5f), m);
        Part(g.transform, PrimitiveType.Cube, new Vector3(0, 3, -7), new Vector3(.7f, 5f, 3.5f), m);
        Part(g.transform, PrimitiveType.Cube, new Vector3(0, .2f, -7), new Vector3(10, .4f, 2.5f), m);
        if (navLights)
        {
            Part(g.transform, PrimitiveType.Sphere, new Vector3(-15, 0, 1), Vector3.one * 1.8f, PvoFx.Lit(Color.black, 0, 0, new Color(5, 0, 0)));
            Part(g.transform, PrimitiveType.Sphere, new Vector3(15, 0, 1), Vector3.one * 1.8f, PvoFx.Lit(Color.black, 0, 0, new Color(0, 5, 0)));
        }
        return g.transform;
    }
    static void Part(Transform p, PrimitiveType t, Vector3 pos, Vector3 sc, Material m)
    {
        var o = GameObject.CreatePrimitive(t); Destroy(o.GetComponent<Collider>());
        o.transform.SetParent(p, false); o.transform.localPosition = pos; o.transform.localScale = sc; o.GetComponent<Renderer>().sharedMaterial = m;
    }
    IEnumerator Fly(Transform p, float speed)
    {
        while (p && !dead.Contains(p))
        {
            p.position += outside.right * speed * Time.deltaTime;
            if (outside.InverseTransformPoint(p.position).x > 140f) { Destroy(p.gameObject); yield break; }
            yield return null;
        }
    }
    IEnumerator Volley(List<Transform> t, bool aimed, float dur)
    {
        float e = 0; int idx = 0;
        while (e < dur)
        {
            Play(Clip("shot", snd_pvo_fire, PvoSynth.Shot), .45f);
            for (int b = 0; b < 2; b++)
            {
                Vector3 from = W(Random.Range(-40, 40), -8, Random.Range(70, 110)), to;
                if (aimed && t.Count > 0 && t[idx % t.Count]) { var p = t[idx++ % t.Count]; to = p.position + outside.right * 22f + Random.insideUnitSphere * 3f; }
                else to = W(Random.Range(-90, 90), Random.Range(25, 60), Random.Range(130, 170));
                StartCoroutine(Tracer(from, to));
            }
            yield return new WaitForSeconds(.14f); e += .14f;
        }
    }
    IEnumerator Tracer(Vector3 a, Vector3 b)
    {
        var g = new GameObject("Tracer"); g.transform.position = a;
        var tr = g.AddComponent<TrailRenderer>();
        tr.sharedMaterial = PvoFx.Unlit(new Color(3f, 2f, .6f, 1), null, true);
        tr.time = .22f; tr.widthMultiplier = .7f; tr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0); tr.shadowCastingMode = ShadowCastingMode.Off;
        float d = Vector3.Distance(a, b), t = 0, dur = d / 260f;
        while (t < dur) { t += Time.deltaTime; g.transform.position = Vector3.Lerp(a, b, t / dur); yield return null; }
        Destroy(g, .3f);
    }
    IEnumerator Kill(Transform p)
    {
        if (!p) yield break;
        dead.Add(p); Boom(false); PvoFx.Explosion(p.position, 14f); flashK = 1f; shake = Mathf.Max(shake, .08f); PvoFx.Burning(p);
        Vector3 v = outside.right * 14f; Vector3 spin = Random.insideUnitSphere * 60f; float groundY = W(0, -7f, 0).y;
        while (p && p.position.y > groundY) { v += Vector3.down * 20f * Time.deltaTime; p.position += v * Time.deltaTime; p.Rotate(spin * Time.deltaTime); yield return null; }
        if (p) { PvoFx.Explosion(p.position, 22f); flashK = 1f; Boom(false); Destroy(p.gameObject); }
    }

    // ---------------------------------------------------------------- per-frame visuals
    void Animate(float dt)
    {
        float k = 1 - Mathf.Exp(-8 * dt);
        cam.fieldOfView = Mathf.Min(88f, 2f * Mathf.Atan(Mathf.Tan(45f * Mathf.Deg2Rad) / cam.aspect) * Mathf.Rad2Deg);
        knobSector.localRotation = Quaternion.Slerp(knobSector.localRotation, Quaternion.Euler(0, -tuneS * 40f, 0), k);
        knobHeight.localRotation = Quaternion.Slerp(knobHeight.localRotation, Quaternion.Euler(0, -tuneH * 60f, 0), k);
        paper.localPosition = Vector3.Lerp(paper.localPosition, paperRest, 1 - Mathf.Exp(-3 * dt));
        receiver.localPosition = Vector3.Lerp(receiver.localPosition, receiverRest + (phoneBusy ? new Vector3(0, .12f, 0) : Vector3.zero), k);
        fireCover.localRotation = Quaternion.Slerp(fireCover.localRotation, coverClosed * Quaternion.Euler(armed ? -105f : 0f, 0, 0), k);
        pressT -= dt; fireButton.localPosition = Vector3.Lerp(fireButton.localPosition, btnRest + (pressT > 0 ? Vector3.down * .015f : Vector3.zero), k);

        var target = windowLook ? windowView : deskView;
        float ck = 1 - Mathf.Exp(-5 * dt);
        camPos = Vector3.Lerp(camPos, target.position, ck); camRot = Quaternion.Slerp(camRot, target.rotation, ck);
        shake = Mathf.MoveTowards(shake, 0, dt * .9f); float s = shake * shake;
        float nx = Mathf.PerlinNoise(Time.time * 25f, 0f) - .5f, ny = Mathf.PerlinNoise(0f, Time.time * 25f) - .5f;
        float sx = Mathf.PerlinNoise(Time.time * .4f, 9f) - .5f, sy = Mathf.PerlinNoise(7f, Time.time * .4f) - .5f;
        Vector2 pv = new Vector2(PvoIn.Pointer().x / Screen.width, PvoIn.Pointer().y / Screen.height) - new Vector2(.5f, .5f);
        pv = new Vector2(Mathf.Clamp(pv.x, -.5f, .5f), Mathf.Clamp(pv.y, -.5f, .5f));
        cam.transform.position = camPos + new Vector3(nx, ny, 0) * s * .12f;
        cam.transform.rotation = camRot * Quaternion.Euler(-pv.y * 2.5f + sy * 1f + ny * s * 6f, pv.x * 3.5f + sx * 1f + nx * s * 6f, nx * s * 4f);
    }

    void SetText(TextMesh t, string s) { if (t.text != s) t.text = s; }

    void UpdateRadio()
    {
        int mode = state != State.Play ? 0 : (Tuned ? 2 : 1);
        if (mode != radioMode)
        {
            radioMode = mode;
            if (mode == 0) radio.Stop();
            else
            {
                radio.clip = mode == 2 ? Clip("tuned", snd_radio_tuned, PvoSynth.Tuned) : Clip("static", snd_radio_static, PvoSynth.Static); radio.Play();
                if (mode == 2) Play(Clip("beep", null, PvoSynth.Beep), .5f);
            }
        }
        radio.volume = mode == 0 ? 0f : (mode == 2 ? .22f : .16f);
        SetText(radioText, "СЕКТОР " + Sec[tuneS] + "\nВИСОТА " + (tuneH + 1) * 1000 + "м");
        radioText.color = mode == 2 ? new Color(.5f, 1f, .55f) : new Color(1f, .75f, .3f);
        int n = radioWave.positionCount;
        for (int i = 0; i < n; i++)
        {
            float x = Mathf.Lerp(-.22f, .22f, i / (n - 1f));
            float y = mode == 2 ? Mathf.Sin(i * .55f + Time.time * 14f) * .012f : (Random.value - .5f) * .03f;
            radioWave.SetPosition(i, new Vector3(x, y, 0));
        }
    }

    Vector3 Cell(int s, Vector2 uv)
    {
        int c = s % 3, r = s / 3;
        return new Vector3((-.5f + (c + uv.x) / 3f) * radarSize * .82f, (.5f - (r + uv.y) / 3f) * radarSize * .82f, -.016f);
    }

    void UpdateRadar(float dt)
    {
        radarSweep.Rotate(0, 0, -140f * dt);
        bool show = state == State.Play && Tuned;
        phantomT -= dt; bool move = phantomT <= 0f; if (move) phantomT = .25f;
        for (int i = 0; i < radarDots.Length; i++)
        {
            var d = radarDots[i]; bool on;
            if (i < 4) { on = show && i < rN; if (on) d.localPosition = Cell(rS, dp[i]); }
            else
            {
                on = jam && state == State.Play && Random.value < .7f;
                if (on && move) d.localPosition = Cell(Random.Range(0, 9), new Vector2(Random.value, Random.value));
            }
            d.gameObject.SetActive(on);
            if (on) d.localScale = Vector3.one * .022f * (1f + .25f * Mathf.Sin(Time.time * 8 + i));
        }
    }

    void UpdateFx(float dt)
    {
        flashK = Mathf.MoveTowards(flashK, 0, dt * 2.5f);
        if (flash) flash.intensity = flashK * 25f;
        float press = state == State.Play ? Mathf.InverseLerp(14f, 0f, timeLeft) : 0f;
        if (vig != null) vig.intensity.value = .32f + press * .25f + (state == State.Over ? .15f : 0f);
        if (chroma != null) chroma.intensity.value = .06f + shake * .5f + flashK * .2f + press * .1f;
        if (adj != null) adj.postExposure.value = flashK * 1.5f;
        if (moon) moon.intensity = moonBase + flashK * .6f;
    }

    // ---------------------------------------------------------------- HUD
    void Lbl(Rect r, string s, int size, Color c, TextAnchor a = TextAnchor.MiddleCenter, bool bold = false)
    {
        if (st == null) st = new GUIStyle(GUI.skin.label) { wordWrap = true };
        st.fontSize = size; st.alignment = a; st.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        st.normal.textColor = new Color(0, 0, 0, .8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        st.normal.textColor = c; GUI.Label(r, s, st);
    }
    void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }

    void OnGUI()
    {
        float W_ = Screen.width, H = Screen.height; int fs = Mathf.Max(12, (int)(H * .024f));
        var amber = new Color(1f, .8f, .4f);
        if (state == State.Play || state == State.Busy)
        {
            Lbl(new Rect(16, 10, W_ * .4f, fs * 1.6f), "НІЧ " + round + "    РАХУНОК " + score + (streak >= 3 ? "    СЕРІЯ " + streak : ""), fs, amber, TextAnchor.MiddleLeft);
            Lbl(new Rect(16, 10 + fs * 1.5f, W_ * .4f, fs * 1.6f), "БУНКЕР " + new string('■', Mathf.Max(0, lives)) + new string('□', Mathf.Max(0, 3 - lives)), fs, lives == 1 ? Color.red : amber, TextAnchor.MiddleLeft);
            float bw = W_ * .3f, bx = W_ * .5f - bw / 2f, f = Mathf.Clamp01(timeLeft / timeMax);
            Box(new Rect(bx - 2, 12, bw + 4, 16), new Color(0, 0, 0, .6f)); Box(new Rect(bx, 14, bw * f, 12), Color.Lerp(Color.red, amber, f * 1.6f));
            Lbl(new Rect(bx, 28, bw, fs * 1.4f), Mathf.CeilToInt(timeLeft) + " с", fs, Color.white);
            Lbl(new Rect(16, H - fs * 2.4f, W_ * .8f, fs * 2), "ЛКМ / коліщатко — вперед · ПКМ — назад · Q — вікно · E — стіл", (int)(fs * .75f), new Color(1, 1, 1, .55f), TextAnchor.LowerLeft);
        }
        if (hover != null && state == State.Play && !windowLook && !string.IsNullOrEmpty(hover.tip))
        {
            var mp = Event.current.mousePosition; var r = new Rect(mp.x + 18, mp.y + 12, fs * 18f, fs * 2.2f);
            Box(r, new Color(0, 0, 0, .65f)); Lbl(new Rect(r.x + 6, r.y, r.width - 12, r.height), hover.tip, (int)(fs * .8f), Color.white, TextAnchor.MiddleLeft);
        }
        if (Time.time < phoneTill && state != State.Menu)
        {
            var r = new Rect(W_ * .5f - W_ * .3f, H - fs * 5.2f, W_ * .6f, fs * 2.4f);
            Box(r, new Color(0, 0, 0, .7f)); Lbl(r, phoneShown, (int)(fs * 1.1f), new Color(.6f, 1f, .7f));
        }
        if (!string.IsNullOrEmpty(msg) && (state == State.Play || state == State.Busy))
            Lbl(new Rect(W_ * .2f, H * .12f, W_ * .6f, fs * 3f), msg, (int)(fs * 1.5f), Color.white, TextAnchor.MiddleCenter, true);
        if (state == State.Menu)
        {
            Box(new Rect(0, 0, W_, H), new Color(0, 0, 0, .72f));
            Lbl(new Rect(0, H * .22f, W_, fs * 4f), "БУНКЕР ПВО", (int)(fs * 3.4f), amber, TextAnchor.MiddleCenter, true);
            Lbl(new Rect(W_ * .25f, H * .38f, W_ * .5f, H * .3f),
                "Телеграма дає дані — вони можуть бути хибні.\nНалаштуй радіо (сектор і висота), звір точки на радарі, за потреби подзвони в штаб.\nПіднімай кришку й тисни кнопку тільки тоді, коли впевнений.\nСоюзників і фальшивки не чіпай — дочекайся кінця таймера.",
                (int)(fs * 1.05f), Color.white);
            Lbl(new Rect(0, H * .74f, W_, fs * 2f), "ПРОБІЛ або клік — почати зміну", (int)(fs * 1.2f), amber);
        }
        if (state == State.Over)
        {
            Box(new Rect(0, 0, W_, H), new Color(0, 0, 0, .6f));
            Lbl(new Rect(0, H * .25f, W_, fs * 4f), "ВІДБІЙ", (int)(fs * 3.2f), Color.red, TextAnchor.MiddleCenter, true);
            Lbl(new Rect(W_ * .2f, H * .42f, W_ * .6f, fs * 3f), overText, (int)(fs * 1.2f), Color.white);
            Lbl(new Rect(0, H * .55f, W_, fs * 2f), "Ночей: " + (round - 1) + "    Рахунок: " + score + "    Рекорд: " + best, (int)(fs * 1.2f), amber);
            if (Time.time > overAt + 1f) Lbl(new Rect(0, H * .7f, W_, fs * 2f), "R або клік — нова зміна", (int)(fs * 1.1f), amber);
        }
    }
}
