using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// "ПВО · БУНКЕР" - повна реалізація ТЗ: телеграф, радіо, радар, телефон, кнопка ОГОНЬ, вікно з результатом.
/// Весь інтерфейс, графіка-заглушки та звуки генеруються кодом (без зовнішніх ассетів).
/// Повісь цей скрипт на порожній GameObject у сцені й натисни Play.
/// </summary>
public class BunkerGame : MonoBehaviour
{
    // ============================ ДАНІ ============================
    static readonly string[] Sectors = { "A1", "A2", "A3", "Б1", "Б2", "Б3", "В1", "В2", "В3" };
    static readonly int[] Heights = { 1000, 2000, 3000, 4000, 5000 };
    static readonly float[] TracerX = { -320f, -110f, 120f, 330f };
    const float TAU = Mathf.PI * 2f;

    enum Kind { Regular, HeightError, Allies, Disinfo, SectorError }
    enum State { Menu, Playing, Resolving, GameOver }

    class Scenario
    {
        public Kind kind;
        public int tSector, tHeight, tCount;   // що каже телеграф
        public int rSector, rHeight, rCount;   // реальність (що покаже радар)
        public bool shouldFire;
        public string phone;
        public Vector2[] dots;
    }

    class Particle
    {
        public Image img; public RectTransform rt; public Vector2 vel;
        public float life, max, s0, s1, gy; public Color c0, c1;
    }

    class Flyer { public RectTransform rt; public Vector2 vel; }

    // ============================ СТАН ============================
    State state = State.Menu;
    Scenario sc;
    int round, score, best, token, lastKind = -1, lastShownSecond;
    float timeLeft;
    int sectorIdx, heightIdx;
    bool coverOpen, phoneBusy;

    // ============================ UI ============================
    Font font;
    Sprite circle, glow, skyNight, skyDay;
    RectTransform content;
    Text hudRound, hudBest, hudTimer;
    RectTransform paperRt; Text paperText;
    Text sectorValue, heightValue;
    RectTransform sectorKnob, heightKnob;
    RectTransform radarSweep, radarDotsRoot, radarCursor;
    readonly List<Image> dots = new List<Image>();
    RectTransform receiverRt; Vector2 receiverHome;
    GameObject popup; Text popupText;
    GameObject cover;
    RectTransform skyFx, cracksRoot, starsRoot;
    Image skyImg, flashImg, lampGlow;
    Image[] lights;
    Text resultTitle, resultSub;
    GameObject overlay; Text overlayTitle, overlayBody, overlayBtnText;

    // ============================ ЕФЕКТИ ============================
    readonly List<Particle> parts = new List<Particle>();
    readonly List<Flyer> flyers = new List<Flyer>();
    float flashA, flashRate; Color flashColor = Color.white;
    float shakeT, shakeDur = 1f, shakeMag;
    float lightsAlpha, lightsTarget;

    // ============================ АУДІО ============================
    AudioSource sfx, ambientSrc, staticSrc, toneSrc;
    AudioClip clipAmbient, clipPrint, clipClick, clipStatic, clipTuned, clipDial, clipPvo,
              clipExpFar, clipExpHeavy, clipBomber, clipSiren, clipSquelch;

    // =================================================================
    //  ІНІЦІАЛІЗАЦІЯ
    // =================================================================
    void Awake()
    {
        best = PlayerPrefs.GetInt("bunker_best", 0);
        LoadFont();
        BuildSprites();
        BuildAudio();
        EnsureEventSystem();
        BuildUI();
    }

    void Start() { ShowMenu(); }

    void LoadFont()
    {
#if UNITY_2022_2_OR_NEWER
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 32);
    }

    void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        var es = FindFirstObjectByType<EventSystem>();
#else
        var es = FindObjectOfType<EventSystem>();
#endif
        if (es == null) es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        if (es.GetComponent<BaseInputModule>() == null)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var m = es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            m.AssignDefaultActions();
#else
            es.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }

    // ----------------------------- спрайти -----------------------------
    static Sprite MakeSprite(Texture2D t)
    {
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeGradient(Color top, Color bottom)
    {
        var t = new Texture2D(2, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
        {
            var c = Color.Lerp(bottom, top, y / 63f);
            t.SetPixel(0, y, c); t.SetPixel(1, y, c);
        }
        return MakeSprite(t);
    }

    void BuildSprites()
    {
        int n = 128; float r = n / 2f;
        var tc = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var tg = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var pc = new Color[n * n]; var pg = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                pc[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(r - d));
                float g = Mathf.Clamp01(1f - d / r);
                pg[y * n + x] = new Color(1, 1, 1, g * g);
            }
        tc.SetPixels(pc); tg.SetPixels(pg);
        circle = MakeSprite(tc); glow = MakeSprite(tg);
        skyNight = MakeGradient(new Color(0.01f, 0.02f, 0.06f), new Color(0.12f, 0.14f, 0.22f));
        skyDay = MakeGradient(new Color(0.42f, 0.62f, 0.9f), new Color(0.82f, 0.88f, 0.95f));
    }

    // =================================================================
    //  UI-ХЕЛПЕРИ
    // =================================================================
    RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Image MakeBox(string name, Transform parent, Vector2 pos, Vector2 size, Color c, Sprite sp = null)
    {
        var rt = NewRect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sp; img.color = c; img.raycastTarget = false;
        return img;
    }

    Text MakeText(Transform parent, string s, int size, Color c, Vector2 pos, Vector2 box,
                  TextAnchor a = TextAnchor.MiddleCenter, bool outline = false)
    {
        var rt = NewRect("Text", parent, pos, box);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.text = s; t.fontSize = size; t.color = c; t.alignment = a;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (outline)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.85f); o.effectDistance = new Vector2(2, -2);
        }
        return t;
    }

    Button MakeButton(Transform parent, string label, int fs, Vector2 pos, Vector2 size, Color c,
                      UnityAction act, Sprite sp = null)
    {
        var img = MakeBox("Btn_" + label, parent, pos, size, c, sp);
        img.raycastTarget = true;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.normalColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        cb.selectedColor = cb.normalColor;
        cb.fadeDuration = 0.05f;
        b.colors = cb;
        b.onClick.AddListener(act);
        if (!string.IsNullOrEmpty(label)) MakeText(img.transform, label, fs, Color.white, Vector2.zero, size);
        return b;
    }

    // =================================================================
    //  ПОБУДОВА ІНТЕРФЕЙСУ
    // =================================================================
    void BuildUI()
    {
        var cgo = new GameObject("BunkerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        cgo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var cs = cgo.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        content = NewRect("Content", cgo.transform, Vector2.zero, Vector2.zero);
        content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;

        // ---- стіна ----
        MakeBox("Wall", content, Vector2.zero, new Vector2(2600, 1500), new Color(0.13f, 0.12f, 0.11f));
        MakeBox("BeamL", content, new Vector2(-900, 0), new Vector2(40, 1500), new Color(0.10f, 0.09f, 0.085f));
        MakeBox("BeamR", content, new Vector2(900, 0), new Vector2(40, 1500), new Color(0.10f, 0.09f, 0.085f));
        MakeBox("Pipe", content, new Vector2(0, 492), new Vector2(2600, 22), new Color(0.2f, 0.18f, 0.16f));
        MakeBox("LampBody", content, new Vector2(-780, 470), new Vector2(70, 14), new Color(0.07f, 0.07f, 0.07f));
        lampGlow = MakeBox("LampGlow", content, new Vector2(-780, 440), new Vector2(380, 300), new Color(1f, 0.8f, 0.5f, 0.2f), glow);

        BuildWindow();
        MakeBox("Sill", content, new Vector2(0, -72), new Vector2(1040, 24), new Color(0.22f, 0.18f, 0.14f));
        MakeBox("Table", content, new Vector2(0, -330), new Vector2(2600, 500), new Color(0.20f, 0.14f, 0.09f));
        MakeBox("TableEdge", content, new Vector2(0, -82), new Vector2(2600, 8), new Color(0.32f, 0.23f, 0.14f));

        // ---- HUD ----
        hudRound = MakeText(content, "", 32, new Color(0.8f, 0.9f, 0.7f), new Vector2(-650, 505), new Vector2(600, 50), TextAnchor.MiddleLeft);
        hudBest = MakeText(content, "", 32, new Color(0.9f, 0.8f, 0.5f), new Vector2(650, 505), new Vector2(600, 50), TextAnchor.MiddleRight);
        hudTimer = MakeText(content, "00:00", 46, new Color(0.85f, 1f, 0.8f), new Vector2(0, 505), new Vector2(400, 56));
        hudTimer.fontStyle = FontStyle.Bold;

        BuildTelegraph();
        BuildRadio();
        BuildRadar();
        BuildPhone();
        BuildFire();

        // ---- спливаюче вікно телефону ----
        var pp = MakeBox("Popup", content, new Vector2(0, -100), new Vector2(820, 88), new Color(0.02f, 0.04f, 0.02f, 0.94f));
        popup = pp.gameObject;
        popupText = MakeText(pp.transform, "", 26, new Color(0.6f, 1f, 0.7f), Vector2.zero, new Vector2(780, 80));
        popup.SetActive(false);

        // ---- екран меню / кінця гри ----
        var ov = MakeBox("Overlay", cgo.transform, Vector2.zero, new Vector2(2800, 1600), new Color(0, 0, 0, 0.9f));
        ov.raycastTarget = true;
        overlay = ov.gameObject;
        overlayTitle = MakeText(ov.transform, "", 78, Color.white, new Vector2(0, 250), new Vector2(1500, 110));
        overlayTitle.fontStyle = FontStyle.Bold;
        overlayBody = MakeText(ov.transform, "", 30, new Color(0.85f, 0.85f, 0.85f), new Vector2(0, 20), new Vector2(1300, 400));
        var ob = MakeButton(ov.transform, "ПОЧАТИ", 38, new Vector2(0, -270), new Vector2(380, 90), new Color(0.2f, 0.5f, 0.22f), OnOverlayButton);
        overlayBtnText = ob.GetComponentInChildren<Text>();
    }

    void BuildWindow()
    {
        MakeBox("WinFrame", content, new Vector2(0, 200), new Vector2(980, 520), new Color(0.30f, 0.28f, 0.25f));
        MakeBox("WinInner", content, new Vector2(0, 200), new Vector2(940, 480), Color.black);
        var sky = NewRect("Sky", content, new Vector2(0, 200), new Vector2(920, 460));
        sky.gameObject.AddComponent<RectMask2D>();
        skyImg = MakeBox("SkyBg", sky, Vector2.zero, new Vector2(920, 460), Color.white, skyNight);

        starsRoot = NewRect("Stars", sky, Vector2.zero, new Vector2(920, 460));
        for (int i = 0; i < 45; i++)
        {
            float s = Random.Range(2f, 3.5f);
            MakeBox("Star", starsRoot, new Vector2(Random.Range(-450f, 450f), Random.Range(-100f, 220f)),
                    new Vector2(s, s), new Color(1, 1, 1, Random.Range(0.3f, 0.9f)));
        }

        lights = new Image[3];
        float[] lx = { -300f, 0f, 300f };
        for (int i = 0; i < 3; i++)
        {
            var l = MakeBox("Searchlight" + i, sky, Vector2.zero, new Vector2(50, 720), new Color(1, 1, 0.85f, 0));
            l.rectTransform.pivot = new Vector2(0.5f, 0f);
            l.rectTransform.anchoredPosition = new Vector2(lx[i], -170f);
            lights[i] = l;
        }

        MakeBox("Ground", sky, new Vector2(0, -200), new Vector2(920, 60), new Color(0.03f, 0.04f, 0.03f));
        float bx = -450f;
        while (bx < 450f)
        {
            float w = Random.Range(36f, 70f), h = Random.Range(18f, 75f);
            MakeBox("Building", sky, new Vector2(bx + w / 2f, -170f + h / 2f), new Vector2(w, h), new Color(0.045f, 0.05f, 0.055f));
            bx += w + Random.Range(-6f, 10f);
        }

        skyFx = NewRect("Fx", sky, Vector2.zero, new Vector2(920, 460));
        flashImg = MakeBox("Flash", sky, Vector2.zero, new Vector2(920, 460), new Color(1, 1, 1, 0));
        cracksRoot = NewRect("Cracks", sky, Vector2.zero, new Vector2(920, 460));
        resultTitle = MakeText(sky, "", 60, Color.white, new Vector2(0, 40), new Vector2(880, 90), TextAnchor.MiddleCenter, true);
        resultTitle.fontStyle = FontStyle.Bold;
        resultSub = MakeText(sky, "", 28, Color.white, new Vector2(0, -40), new Vector2(800, 90), TextAnchor.UpperCenter, true);
        resultTitle.gameObject.SetActive(false);
        resultSub.gameObject.SetActive(false);
    }

    void BuildTelegraph()
    {
        var tg = new Vector2(-720, -300);
        MakeBox("TelegraphBody", content, tg, new Vector2(420, 270), new Color(0.24f, 0.26f, 0.24f));
        MakeBox("TelegraphTop", content, tg + new Vector2(0, 120), new Vector2(380, 30), new Color(0.16f, 0.17f, 0.16f));
        MakeBox("Slot", content, tg + new Vector2(0, 106), new Vector2(340, 8), Color.black);

        var paper = MakeBox("Paper", content, tg + new Vector2(0, 104), new Vector2(320, 0), new Color(0.93f, 0.9f, 0.78f));
        paperRt = paper.rectTransform;
        paperRt.pivot = new Vector2(0.5f, 1f);
        paperRt.anchoredPosition = tg + new Vector2(0, 104);
        paperRt.gameObject.AddComponent<RectMask2D>();

        paperText = MakeText(paperRt, "", 24, new Color(0.1f, 0.1f, 0.1f), Vector2.zero, new Vector2(290, 150), TextAnchor.UpperLeft);
        var tr = paperText.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0, -12);
        tr.sizeDelta = new Vector2(290, 150);

        MakeText(content, "ТЕЛЕГРАФ", 22, new Color(0.65f, 0.65f, 0.55f), tg + new Vector2(0, -155), new Vector2(300, 30));
    }

    void BuildRadio()
    {
        var rd = new Vector2(-250, -300);
        MakeBox("RadioBody", content, rd, new Vector2(480, 270), new Color(0.19f, 0.2f, 0.17f));
        MakeBox("RadioFace", content, rd, new Vector2(456, 246), new Color(0.12f, 0.12f, 0.10f));
        MakeText(content, "РАДІОПРИЙМАЧ", 22, new Color(0.75f, 0.75f, 0.6f), rd + new Vector2(0, 100), new Vector2(400, 30));
        BuildRadioRow("СЕКТОР", rd + new Vector2(0, 30), out sectorValue, out sectorKnob, () => ChangeSector(-1), () => ChangeSector(1));
        BuildRadioRow("ВИСОТА", rd + new Vector2(0, -60), out heightValue, out heightKnob, () => ChangeHeight(-1), () => ChangeHeight(1));
        MakeText(content, "РАДІО", 22, new Color(0.65f, 0.65f, 0.55f), rd + new Vector2(0, -155), new Vector2(300, 30));
    }

    void BuildRadioRow(string label, Vector2 center, out Text value, out RectTransform knob, UnityAction prev, UnityAction next)
    {
        var k = MakeBox(label + "Knob", content, center + new Vector2(-185, 0), new Vector2(58, 58), new Color(0.38f, 0.38f, 0.33f), circle);
        knob = k.rectTransform;
        MakeBox("Indicator", knob, new Vector2(0, 14), new Vector2(5, 26), new Color(0.98f, 0.85f, 0.35f));
        MakeButton(content, "<", 30, center + new Vector2(-105, 0), new Vector2(46, 46), new Color(0.3f, 0.3f, 0.27f), prev);
        MakeBox(label + "Display", content, center, new Vector2(132, 52), Color.black);
        value = MakeText(content, "", 30, new Color(0.4f, 1f, 0.5f), center, new Vector2(132, 52));
        MakeButton(content, ">", 30, center + new Vector2(105, 0), new Vector2(46, 46), new Color(0.3f, 0.3f, 0.27f), next);
        MakeText(content, label, 18, new Color(0.65f, 0.65f, 0.55f), center + new Vector2(185, 0), new Vector2(100, 30));
    }

    static Vector2 CellCenter(int idx) { return new Vector2((idx % 3 - 1) * 80f, (1 - idx / 3) * 80f); }

    void BuildRadar()
    {
        var rp = new Vector2(190, -305);
        MakeBox("RadarRim", content, rp, new Vector2(324, 324), new Color(0.16f, 0.17f, 0.15f), circle);
        var radar = MakeBox("Radar", content, rp, new Vector2(300, 300), new Color(0.02f, 0.11f, 0.04f), circle);
        radar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var rr = radar.transform;

        var lineCol = new Color(0f, 0.5f, 0.15f, 0.45f);
        float[] gl = { -120f, -40f, 40f, 120f };
        foreach (float g in gl)
        {
            MakeBox("GridV", rr, new Vector2(g, 0), new Vector2(2, 300), lineCol);
            MakeBox("GridH", rr, new Vector2(0, g), new Vector2(300, 2), lineCol);
        }
        for (int i = 0; i < Sectors.Length; i++)
            MakeText(rr, Sectors[i], 18, new Color(0f, 0.6f, 0.2f, 0.4f), CellCenter(i) + new Vector2(0, -28), new Vector2(60, 24));

        radarCursor = MakeBox("Cursor", rr, CellCenter(0), new Vector2(78, 78), new Color(0f, 1f, 0.3f, 0.12f)).rectTransform;
        radarDotsRoot = NewRect("Dots", rr, Vector2.zero, new Vector2(300, 300));

        var sweep = MakeBox("Sweep", rr, Vector2.zero, new Vector2(150, 3), new Color(0.35f, 1f, 0.45f, 0.95f));
        radarSweep = sweep.rectTransform;
        radarSweep.pivot = new Vector2(0f, 0.5f);
        radarSweep.anchoredPosition = Vector2.zero;
        for (int i = 1; i <= 7; i++)
        {
            var tail = MakeBox("Tail", radarSweep, Vector2.zero, new Vector2(150, 3), new Color(0.3f, 1f, 0.4f, 0.5f / i));
            tail.rectTransform.pivot = new Vector2(0f, 0.5f);
            tail.rectTransform.anchoredPosition = Vector2.zero;
            tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 3.5f * i);
        }

        MakeText(content, "РАДАР", 22, new Color(0.65f, 0.65f, 0.55f), rp + new Vector2(0, -185), new Vector2(300, 30));
    }

    void BuildPhone()
    {
        var ph = new Vector2(560, -345);
        MakeBox("PhoneBase", content, ph, new Vector2(270, 120), new Color(0.07f, 0.07f, 0.07f));
        MakeBox("PhoneDial", content, ph + new Vector2(-60, -4), new Vector2(92, 92), new Color(0.24f, 0.24f, 0.22f), circle);
        MakeBox("PhoneDialCenter", content, ph + new Vector2(-60, -4), new Vector2(46, 46), new Color(0.05f, 0.05f, 0.05f), circle);
        for (int i = 0; i < 9; i++)
        {
            float a = (30f + i * 28f) * Mathf.Deg2Rad;
            MakeBox("Hole", content, ph + new Vector2(-60, -4) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 33f,
                    new Vector2(11, 11), new Color(0.04f, 0.04f, 0.04f), circle);
        }
        for (int i = 0; i < 3; i++)
            MakeBox("PhoneKey", content, ph + new Vector2(40 + i * 30, 0), new Vector2(22, 22), new Color(0.3f, 0.3f, 0.26f));

        receiverHome = ph + new Vector2(0, 68);
        var rec = MakeButton(content, "ТРУБКА", 20, receiverHome, new Vector2(250, 46), new Color(0.11f, 0.11f, 0.11f), OnPhone);
        receiverRt = (RectTransform)rec.transform;
        MakeText(content, "ТЕЛЕФОН ШТАБУ", 22, new Color(0.65f, 0.65f, 0.55f), ph + new Vector2(0, -85), new Vector2(300, 30));
    }

    void BuildFire()
    {
        var fp = new Vector2(840, -330);
        MakeBox("FireBase", content, fp, new Vector2(200, 200), new Color(0.17f, 0.16f, 0.14f));
        MakeBox("FireRing", content, fp, new Vector2(172, 172), new Color(0.38f, 0.32f, 0.1f), circle);
        MakeButton(content, "ОГОНЬ", 30, fp, new Vector2(150, 150), new Color(0.82f, 0.05f, 0.05f), OnFire, circle);
        var cv = MakeButton(content, "КРИШКА", 22, fp, new Vector2(190, 190), new Color(1f, 0.9f, 0.3f, 0.6f), OnCover);
        cover = cv.gameObject;
        MakeText(content, "ПВО", 22, new Color(0.65f, 0.65f, 0.55f), fp + new Vector2(0, -120), new Vector2(200, 30));
    }

    // =================================================================
    //  АУДІО (процедурний синтез - ассети не потрібні)
    // =================================================================
    const int SR = 22050;
    static readonly System.Random Rng = new System.Random(1234);
    static float Noise() { return (float)(Rng.NextDouble() * 2.0 - 1.0); }

    static AudioClip MakeClip(string name, float seconds, System.Func<float, float> f)
    {
        int len = Mathf.Max(1, (int)(seconds * SR));
        var d = new float[len];
        for (int i = 0; i < len; i++) d[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
        var c = AudioClip.Create(name, len, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    AudioSource MakeSource(AudioClip c, bool loop, float vol)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = c; s.loop = loop; s.volume = vol; s.playOnAwake = false; s.spatialBlend = 0f;
        if (loop) s.Play();
        return s;
    }

    void BuildAudio()
    {
        float lpA = 0f, lpS = 0f, lpP = 0f, lpF = 0f, lpH = 0f, lpB = 0f, sirenPh = 0f;

        clipAmbient = MakeClip("snd_ambient_bunker", 2f, t =>
        { lpA += 0.02f * (Noise() - lpA); return Mathf.Sin(TAU * 60f * t) * 0.10f + Mathf.Sin(TAU * 120f * t) * 0.04f + lpA * 0.35f; });

        clipPrint = MakeClip("snd_telegraph_print", 2f, t =>
        {
            float ph = t % 0.075f; float env = Mathf.Exp(-ph * 220f);
            return Noise() * env * 0.8f + Mathf.Sin(TAU * 900f * ph) * env * 0.3f + Mathf.Sin(TAU * 95f * t) * 0.04f;
        });

        clipClick = MakeClip("snd_knob_click", 0.06f, t =>
        { float env = Mathf.Exp(-t * 120f); return (Noise() * 0.6f + Mathf.Sin(TAU * 1800f * t) * 0.4f) * env; });

        clipStatic = MakeClip("snd_radio_static", 1f, t =>
        { lpS += 0.6f * (Noise() - lpS); return lpS * 0.9f; });

        clipTuned = MakeClip("snd_radio_tuned", 1f, t =>
            (Mathf.Sin(TAU * 440f * t) * 0.5f + Mathf.Sin(TAU * 660f * t) * 0.25f) * (0.8f + 0.2f * Mathf.Sin(TAU * 4f * t)));

        clipDial = MakeClip("snd_phone_dial", 1.5f, t =>
        {
            float v = 0f;
            if (t < 1.0f)
            {
                float ph = t % 0.16f;
                v += Noise() * Mathf.Exp(-ph * 200f) * 0.7f + Mathf.Sin(TAU * (300f + 200f * t) * t) * 0.05f;
            }
            else
            {
                float bt = (t - 1.0f) % 0.25f;
                if (bt < 0.15f) v += Mathf.Sin(TAU * 425f * t) * 0.4f;
            }
            return v;
        });

        float[] shots = { 0f, 0.28f, 0.55f, 0.95f, 1.2f };
        clipPvo = MakeClip("snd_pvo_fire", 1.7f, t =>
        {
            lpP += 0.15f * (Noise() - lpP); float v = 0f;
            foreach (float s in shots)
            {
                float d = t - s;
                if (d >= 0f) v += lpP * Mathf.Exp(-d * 9f) * 3f + Mathf.Sin(TAU * (90f - 30f * d) * d) * Mathf.Exp(-d * 8f) * 0.6f;
            }
            return v;
        });

        clipExpFar = MakeClip("snd_explosion_far", 2.2f, t =>
        { lpF += 0.03f * (Noise() - lpF); return lpF * Mathf.Exp(-t * 2.2f) * 5f + Mathf.Sin(TAU * 45f * t) * Mathf.Exp(-t * 2f) * 0.5f; });

        clipExpHeavy = MakeClip("snd_explosion_heavy", 3f, t =>
        {
            lpH += 0.06f * (Noise() - lpH);
            return lpH * Mathf.Exp(-t * 1.4f) * 6f + Mathf.Sin(TAU * 38f * t) * Mathf.Exp(-t * 1.2f) * 0.8f + Noise() * Mathf.Exp(-t * 10f) * 0.6f;
        });

        clipBomber = MakeClip("snd_bomber_plane", 3.2f, t =>
        {
            float a = Mathf.Clamp01(t / 3f); a *= a;
            lpB += 0.05f * (Noise() - lpB);
            float saw1 = (t * 55f % 1f) * 2f - 1f, saw2 = (t * 82f % 1f) * 2f - 1f;
            return (saw1 * 0.3f + saw2 * 0.2f + lpB * 1.2f) * a * (0.85f + 0.15f * Mathf.Sin(TAU * 12f * t));
        });

        clipSiren = MakeClip("snd_siren", 2.4f, t =>
        { sirenPh += TAU * (700f + 250f * Mathf.Sin(TAU * 0.8f * t)) / SR; return Mathf.Sin(sirenPh) * 0.5f; });

        clipSquelch = MakeClip("snd_phone_voice_filter", 0.35f, t =>
            Noise() * Mathf.Exp(-t * 10f) * 0.4f + Mathf.Sin(TAU * 1200f * t) * Mathf.Exp(-t * 30f) * 0.3f);

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.spatialBlend = 0f; sfx.playOnAwake = false;
        ambientSrc = MakeSource(clipAmbient, true, 0.35f);
        staticSrc = MakeSource(clipStatic, true, 0f);
        toneSrc = MakeSource(clipTuned, true, 0f);
    }

    void Click() { sfx.PlayOneShot(clipClick, 0.8f); }

    void RadioAudio(bool on)
    {
        if (!on || sc == null) { staticSrc.volume = 0f; toneSrc.volume = 0f; return; }
        bool tuned = IsTuned();
        staticSrc.volume = tuned ? 0f : 0.10f;
        toneSrc.volume = tuned ? 0.12f : 0f;
    }

    // =================================================================
    //  ЛОГІКА ГРИ
    // =================================================================
    void ShowMenu()
    {
        state = State.Menu;
        overlay.SetActive(true);
        overlayTitle.text = "ПВО · БУНКЕР";
        overlayTitle.color = new Color(1f, 0.85f, 0.4f);
        overlayBody.text =
            "Кожна зміна - це хвиля. Таймер 30-45 секунд.\n\n" +
            "ТЕЛЕГРАФ видає дані про ціль - але вони можуть бути хибними.\n" +
            "Налаштуй РАДІО (сектор і висота): гул - ти влучив у частоту, шум - ні.\n" +
            "РАДАР покаже цілі лише при правильному налаштуванні.\n" +
            "ТЕЛЕФОН штабу (1.5 с на дзвінок) розкаже, де помилка, чи це свої, чи фальшивка.\n\n" +
            "Ціль підтверджена - відкрий КРИШКУ й тисни ОГОНЬ.\n" +
            "Свої або дезінформація - НЕ СТРІЛЯЙ, дочекайся кінця таймера.";
        overlayBtnText.text = "ПОЧАТИ ЗМІНУ";
        UpdateHud();
        RadioAudio(false);
    }

    void OnOverlayButton() { Click(); StartGame(); }

    void StartGame()
    {
        overlay.SetActive(false);
        round = 0; score = 0;
        NextRound();
    }

    Scenario MakeScenario()
    {
        var s = new Scenario();
        int k;
        if (round == 1) k = 0;
        else { do { k = Random.Range(0, 5); } while (k == lastKind); }
        lastKind = k; s.kind = (Kind)k;

        int sec = Random.Range(0, Sectors.Length), hei = Random.Range(0, Heights.Length), cnt = Random.Range(1, 5);
        s.tSector = s.rSector = sec; s.tHeight = s.rHeight = hei; s.tCount = s.rCount = cnt;
        s.shouldFire = true;

        switch (s.kind)
        {
            case Kind.Regular:
                s.phone = "Розвідка на зв'язку! Підтверджую, це ворог!";
                break;
            case Kind.HeightError:
                s.rHeight = Other(hei, Heights.Length);
                s.phone = "Розвідка на зв'язку! Навідник помилився, реальна висота " + Heights[s.rHeight] + " метрів!";
                break;
            case Kind.Allies:
                s.tCount = s.rCount = 1; s.shouldFire = false;
                s.phone = "Не стріляти! Це група «Альфа», вони повертаються на базу!";
                break;
            case Kind.Disinfo:
                s.shouldFire = false;
                s.rCount = Random.value < 0.5f ? s.tCount : 0;
                s.phone = "Увага! Ворог пустив хибні радарні завади, ігноруйте сигнал!";
                break;
            case Kind.SectorError:
                s.rSector = Other(sec, Sectors.Length);
                s.phone = "Штаб: вони у секторі " + Sectors[s.rSector] + "!";
                break;
        }

        s.dots = new Vector2[4];
        Vector2[] q = { new Vector2(-13, 13), new Vector2(13, 13), new Vector2(-13, -13), new Vector2(13, -13) };
        for (int i = 0; i < 4; i++) s.dots[i] = q[i] + new Vector2(Random.Range(-5f, 5f), Random.Range(-5f, 5f));
        return s;
    }

    static int Other(int cur, int n) { int r = Random.Range(0, n - 1); return r >= cur ? r + 1 : r; }

    void NextRound()
    {
        token++; round++;
        sc = MakeScenario();
        timeLeft = Mathf.Max(30f, 45f - (round - 1) * 1.5f);
        lastShownSecond = -1;
        coverOpen = false; cover.SetActive(true);
        phoneBusy = false; popup.SetActive(false);
        receiverRt.anchoredPosition = receiverHome;
        ClearWindow();
        ApplySky();

        int tries = 0;
        do { sectorIdx = Random.Range(0, Sectors.Length); heightIdx = Random.Range(0, Heights.Length); tries++; }
        while (tries < 50 && ((sectorIdx == sc.rSector && heightIdx == sc.rHeight) || (sectorIdx == sc.tSector && heightIdx == sc.tHeight)));

        state = State.Playing;
        UpdateRadio();
        UpdateHud();
        StartCoroutine(TelegraphRoutine(token));
    }

    void ApplySky()
    {
        bool night = (round % 3) != 0;
        skyImg.sprite = night ? skyNight : skyDay;
        starsRoot.gameObject.SetActive(night);
    }

    void ClearWindow()
    {
        for (int i = skyFx.childCount - 1; i >= 0; i--) Destroy(skyFx.GetChild(i).gameObject);
        for (int i = cracksRoot.childCount - 1; i >= 0; i--) Destroy(cracksRoot.GetChild(i).gameObject);
        parts.Clear(); flyers.Clear();
        lightsTarget = 0f; lightsAlpha = 0f; flashA = 0f;
        flashImg.color = new Color(1, 1, 1, 0);
        resultTitle.gameObject.SetActive(false);
        resultSub.gameObject.SetActive(false);
    }

    void UpdateHud()
    {
        hudRound.text = "ЗМІНА " + Mathf.Max(round, 0) + "   |   ВІДБИТО: " + score;
        hudBest.text = "РЕКОРД: " + best;
    }

    // ----------------------------- телеграф -----------------------------
    IEnumerator TelegraphRoutine(int tk)
    {
        paperText.text = "";
        paperRt.sizeDelta = new Vector2(320, 0);
        string full = "ТЕЛЕГРАМА №" + round + "\n[СЕКТОР: " + Sectors[sc.tSector] + "]\n[ВИСОТА: " + Heights[sc.tHeight] + "м]\n[ЦІЛЕЙ: " + sc.tCount + "]";
        sfx.PlayOneShot(clipPrint, 0.6f);
        float dur = 1.8f, t = 0f;
        while (t < dur)
        {
            if (tk != token) yield break;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            paperRt.sizeDelta = new Vector2(320, Mathf.Lerp(0f, 175f, Mathf.SmoothStep(0f, 1f, p)));
            paperText.text = full.Substring(0, Mathf.Min(full.Length, (int)(full.Length * p)));
            yield return null;
        }
        paperRt.sizeDelta = new Vector2(320, 175);
        paperText.text = full;
    }

    // ----------------------------- радіо / радар -----------------------------
    bool IsTuned() { return sc != null && sectorIdx == sc.rSector && heightIdx == sc.rHeight; }

    void ChangeSector(int d)
    {
        if (state != State.Playing) return;
        sectorIdx = (sectorIdx + d + Sectors.Length) % Sectors.Length;
        Click(); UpdateRadio();
    }

    void ChangeHeight(int d)
    {
        if (state != State.Playing) return;
        heightIdx = (heightIdx + d + Heights.Length) % Heights.Length;
        Click(); UpdateRadio();
    }

    void UpdateRadio()
    {
        sectorValue.text = Sectors[sectorIdx];
        heightValue.text = Heights[heightIdx] + " м";
        sectorKnob.localRotation = Quaternion.Euler(0, 0, 135f - 270f * sectorIdx / (Sectors.Length - 1));
        heightKnob.localRotation = Quaternion.Euler(0, 0, 135f - 270f * heightIdx / (Heights.Length - 1));
        radarCursor.anchoredPosition = CellCenter(sectorIdx);

        for (int i = radarDotsRoot.childCount - 1; i >= 0; i--) Destroy(radarDotsRoot.GetChild(i).gameObject);
        dots.Clear();
        if (sc != null && IsTuned() && sc.rCount > 0)
        {
            for (int i = 0; i < sc.rCount; i++)
            {
                var d = MakeBox("Dot", radarDotsRoot, CellCenter(sc.rSector) + sc.dots[i], new Vector2(16, 16), new Color(0.45f, 1f, 0.55f), circle);
                MakeBox("DotGlow", d.transform, Vector2.zero, new Vector2(38, 38), new Color(0.3f, 1f, 0.4f, 0.35f), glow);
                dots.Add(d);
            }
        }
        RadioAudio(state == State.Playing);
    }

    // ----------------------------- телефон -----------------------------
    void OnPhone()
    {
        if (state != State.Playing || phoneBusy) return;
        StartCoroutine(PhoneRoutine(token));
    }

    IEnumerator PhoneRoutine(int tk)
    {
        phoneBusy = true;
        popup.SetActive(false);
        receiverRt.anchoredPosition = receiverHome + new Vector2(0, 22);
        sfx.PlayOneShot(clipDial, 0.8f);
        float t = 0f;
        while (t < 1.5f)
        {
            if (tk != token || state != State.Playing) { phoneBusy = false; yield break; }
            t += Time.deltaTime; yield return null;
        }
        popupText.color = new Color(0.6f, 1f, 0.7f);
        popupText.text = "ШТАБ: " + sc.phone;
        popup.SetActive(true);
        sfx.PlayOneShot(clipSquelch, 0.6f);
        t = 0f;
        while (t < 5f && tk == token && state == State.Playing) { t += Time.deltaTime; yield return null; }
        if (tk == token) { popup.SetActive(false); receiverRt.anchoredPosition = receiverHome; }
        phoneBusy = false;
    }

    // ----------------------------- вогонь -----------------------------
    void OnCover()
    {
        if (state != State.Playing || coverOpen) return;
        coverOpen = true; cover.SetActive(false); Click();
    }

    void OnFire()
    {
        if (state != State.Playing || !coverOpen) return;
        state = State.Resolving;
        RadioAudio(false);
        popup.SetActive(false);
        Click();
        if (sc.kind == Kind.Allies) StartCoroutine(AlliesRoutine());
        else if (sc.kind == Kind.Disinfo)
            StartCoroutine(BomberAttack(true, "ПРОМАХ", "Це була фальшивка. Снаряди пішли у порожнечу, а справжні бомбардувальники прорвалися."));
        else if (IsTuned()) StartCoroutine(WinRoutine());
        else StartCoroutine(BomberAttack(true, "ПРОМАХ", "Невірні координати! Снаряди пролетіли повз ціль."));
    }

    void TimeUp()
    {
        if (state != State.Playing) return;
        state = State.Resolving;
        RadioAudio(false);
        popup.SetActive(false);
        if (!sc.shouldFire) StartCoroutine(SafeRoutine());
        else StartCoroutine(BomberAttack(false, "ЧАС ВИЙШОВ", "Ви не встигли відкрити вогонь - бомбардувальники скинули бомби на бункер."));
    }

    // =================================================================
    //  СЦЕНАРІЇ РЕЗУЛЬТАТУ (вікно)
    // =================================================================
    IEnumerator WinRoutine()
    {
        yield return StartCoroutine(ShootDownSequence(false));
        score++; UpdateHud();
        ShowResult("ЦІЛЬ ЗНИЩЕНО", new Color(0.5f, 1f, 0.5f), "Ворожий літак збито. Гарна робота, навіднику!");
        yield return new WaitForSeconds(2.8f);
        NextRound();
    }

    IEnumerator AlliesRoutine()
    {
        yield return StartCoroutine(ShootDownSequence(true));
        yield return new WaitForSeconds(0.8f);
        sfx.PlayOneShot(clipSiren, 0.9f);
        sfx.PlayOneShot(clipSquelch, 0.8f);
        popupText.color = new Color(1f, 0.4f, 0.35f);
        popupText.text = "ШТАБ: ВИ ЗБИЛИ НАШИХ!!!";
        popup.SetActive(true);
        Flash(new Color(1f, 0.1f, 0.05f), 0.5f, 1.2f);
        Shake(12f, 0.7f);
        ShowResult("ТРАГЕДІЯ", new Color(1f, 0.35f, 0.3f), "Це був літак групи «Альфа».");
        yield return new WaitForSeconds(3.2f);
        GameOver("ТРАГЕДІЯ", "Ви збили своїх. Штаб вас не пробачить.");
    }

    IEnumerator SafeRoutine()
    {
        if (sc.kind == Kind.Allies)
        {
            for (int i = 0; i < 3; i++)
            {
                var pl = BuildPlane(new Color(0.25f, 0.3f, 0.4f), 0.45f);
                pl.anchoredPosition = new Vector2(-560f - i * 130f, 70f + i * 38f);
                flyers.Add(new Flyer { rt = pl, vel = new Vector2(250f, 0f) });
            }
            ShowResult("СВОЇ ПРОЙШЛИ", new Color(0.6f, 0.85f, 1f), "Група «Альфа» повернулася на базу. Ви не відкрили вогонь.");
        }
        else
        {
            ShowResult("ХИБНА ТРИВОГА", new Color(0.6f, 0.85f, 1f), "Ви не піддалися на дезінформацію ворога.");
        }
        score++; UpdateHud();
        yield return new WaitForSeconds(3.2f);
        NextRound();
    }

    IEnumerator BomberAttack(bool shotsFired, string title, string sub)
    {
        if (shotsFired)
        {
            lightsTarget = 1f;
            sfx.PlayOneShot(clipPvo, 0.9f);
            float ft = 0f, tt = 0f;
            while (ft < 1.3f)
            {
                ft += Time.deltaTime; tt -= Time.deltaTime;
                if (tt <= 0f) { tt = 0.05f; FireTracer(new Vector2(Random.Range(-380f, 380f), Random.Range(80f, 230f))); }
                yield return null;
            }
        }
        else yield return new WaitForSeconds(0.4f);

        sfx.PlayOneShot(clipBomber, 1f);
        float t = 0f, flashTimer = 0f;
        while (t < 3.2f)
        {
            float dt = Time.deltaTime;
            t += dt; flashTimer -= dt;
            float p = t / 3.2f;
            if (flashTimer <= 0f)
            {
                flashTimer = Mathf.Lerp(0.55f, 0.15f, p) * Random.Range(0.7f, 1.2f);
                Spawn(glow, new Vector2(Random.Range(-420f, 420f), -150f), Vector2.zero, new Vector2(260, 200), 0.5f, 0.5f, 1.5f,
                      new Color(1f, 0.65f, 0.25f, 0.8f), new Color(1f, 0.25f, 0f, 0f));
                Flash(new Color(1f, 0.7f, 0.4f), 0.18f, 1.5f);
                Shake(Mathf.Lerp(2f, 9f, p), 0.3f);
                sfx.PlayOneShot(clipExpFar, 0.35f);
            }
            yield return null;
        }

        Explode(new Vector2(0f, -120f), 1.6f, false);
        BuildCracks();
        yield return new WaitForSeconds(0.9f);
        ShowResult(title, new Color(1f, 0.35f, 0.3f), sub);
        yield return new WaitForSeconds(2.6f);
        GameOver(title, sub);
    }

    IEnumerator ShootDownSequence(bool ally)
    {
        lightsTarget = 1f;
        var plane = BuildPlane(ally ? new Color(0.25f, 0.3f, 0.4f) : new Color(0.05f, 0.05f, 0.06f), 1f);
        Vector2 pos = new Vector2(-560f, 125f);
        Vector2 vel = new Vector2(280f, -6f);
        plane.anchoredPosition = pos;
        float t = 0f, shotTimer = 0f, rot = 0f, smokeTimer = 0f;
        bool fired = false, exploded = false;
        const float explodeAt = 1.9f;

        while (true)
        {
            float dt = Time.deltaTime; t += dt;
            if (!exploded)
            {
                pos += vel * dt;
                plane.anchoredPosition = pos;
                if (t > 0.5f && !fired) { fired = true; sfx.PlayOneShot(clipPvo, 0.9f); }
                if (t > 0.5f)
                {
                    shotTimer -= dt;
                    if (shotTimer <= 0f) { shotTimer = 0.045f; FireTracer(pos + vel * 0.4f); }
                }
                if (t >= explodeAt)
                {
                    exploded = true;
                    Explode(pos, 1f, true);
                    foreach (var im in plane.GetComponentsInChildren<Image>()) im.color = new Color(0.18f, 0.06f, 0.03f);
                    vel = new Vector2(150f, -20f);
                }
            }
            else
            {
                vel.y -= 420f * dt;
                pos += vel * dt;
                rot -= 75f * dt;
                plane.anchoredPosition = pos;
                plane.localRotation = Quaternion.Euler(0, 0, rot);
                smokeTimer -= dt;
                if (smokeTimer <= 0f)
                {
                    smokeTimer = 0.05f;
                    Spawn(glow, pos, new Vector2(-20f, 12f), new Vector2(70, 70), 1.2f, 0.6f, 2f,
                          new Color(0.12f, 0.12f, 0.12f, 0.7f), new Color(0.12f, 0.12f, 0.12f, 0f));
                    Spawn(glow, pos, Vector2.zero, new Vector2(50, 50), 0.4f, 1f, 0.3f,
                          new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.2f, 0f, 0f));
                }
                if (pos.y < -165f)
                {
                    Explode(new Vector2(pos.x, -165f), 0.6f, true);
                    Destroy(plane.gameObject);
                    break;
                }
            }
            yield return null;
        }
        yield return new WaitForSeconds(0.8f);
    }

    void GameOver(string title, string reason)
    {
        state = State.GameOver;
        if (score > best) { best = score; PlayerPrefs.SetInt("bunker_best", best); PlayerPrefs.Save(); }
        overlay.SetActive(true);
        overlayTitle.text = title;
        overlayTitle.color = new Color(1f, 0.3f, 0.25f);
        overlayBody.text = reason + "\n\nВідбито хвиль: " + score + "\nРекорд: " + best;
        overlayBtnText.text = "ЗНОВУ";
        RadioAudio(false);
        UpdateHud();
    }

    // =================================================================
    //  ЕФЕКТИ
    // =================================================================
    RectTransform BuildPlane(Color c, float scale)
    {
        var root = NewRect("Plane", skyFx, Vector2.zero, new Vector2(10, 10));
        MakeBox("Fuselage", root, Vector2.zero, new Vector2(130, 16), c);
        MakeBox("Wing", root, new Vector2(5, 0), new Vector2(28, 96), c);
        MakeBox("Tail", root, new Vector2(-58, 14), new Vector2(14, 36), c);
        MakeBox("TailPlane", root, new Vector2(-58, 0), new Vector2(14, 44), c);
        root.localScale = Vector3.one * scale;
        return root;
    }

    void Spawn(Sprite sp, Vector2 pos, Vector2 vel, Vector2 size, float life, float s0, float s1,
               Color c0, Color c1, float angle = 0f, float gravityY = 0f)
    {
        var img = MakeBox("P", skyFx, pos, size, c0, sp);
        img.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        img.rectTransform.localScale = Vector3.one * s0;
        parts.Add(new Particle { img = img, rt = img.rectTransform, vel = vel, life = life, max = life, s0 = s0, s1 = s1, c0 = c0, c1 = c1, gy = gravityY });
    }

    void FireTracer(Vector2 target)
    {
        Vector2 from = new Vector2(TracerX[Random.Range(0, TracerX.Length)], -172f);
        Vector2 dir = (target + Random.insideUnitCircle * 18f - from).normalized;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        Spawn(circle, from, dir * 1100f, new Vector2(6, 30), 0.55f, 1f, 1f,
              new Color(1f, 0.9f, 0.5f, 1f), new Color(1f, 0.45f, 0.1f, 0.2f), ang);
    }

    void Explode(Vector2 pos, float size, bool far)
    {
        Spawn(glow, pos, Vector2.zero, new Vector2(320, 320) * size, 0.55f, 0.3f, 1.4f,
              new Color(1f, 0.92f, 0.65f, 0.95f), new Color(1f, 0.5f, 0.1f, 0f));
        int n = Mathf.RoundToInt(26 * size);
        for (int i = 0; i < n; i++)
        {
            float a = Random.value * TAU, sp = Random.Range(60f, 260f) * size;
            Spawn(circle, pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, Vector2.one * Random.Range(30f, 60f) * size,
                  Random.Range(0.6f, 1.2f), 0.6f, 2.2f, new Color(1f, 0.75f, 0.25f, 0.9f), new Color(0.55f, 0.1f, 0f, 0f), 0f, -60f);
        }
        sfx.PlayOneShot(far ? clipExpFar : clipExpHeavy, far ? 0.9f : 1f);
        Flash(Color.white, far ? 0.55f : 1f, far ? 3f : 0.8f);
        Shake(far ? 5f : 40f, far ? 0.4f : 1.3f);
    }

    void BuildCracks()
    {
        Vector2 p = new Vector2(Random.Range(-150f, 150f), Random.Range(-40f, 80f));
        for (int i = 0; i < 9; i++)
        {
            float ang = i * 40f + Random.Range(-14f, 14f);
            float len = Random.Range(140f, 420f);
            var c = MakeBox("Crack", cracksRoot, p, new Vector2(3, len), new Color(1f, 1f, 1f, 0.85f));
            c.rectTransform.pivot = new Vector2(0.5f, 0f);
            c.rectTransform.anchoredPosition = p;
            c.rectTransform.localRotation = Quaternion.Euler(0, 0, ang);
            var br = MakeBox("CrackBranch", c.transform, new Vector2(0, len * Random.Range(0.35f, 0.65f)), new Vector2(2, len * 0.35f), new Color(1f, 1f, 1f, 0.7f));
            br.rectTransform.pivot = new Vector2(0.5f, 0f);
            br.rectTransform.anchoredPosition = new Vector2(0, len * Random.Range(0.35f, 0.65f));
            br.rectTransform.localRotation = Quaternion.Euler(0, 0, Random.Range(-50f, 50f));
        }
        MakeBox("GlassTint", cracksRoot, Vector2.zero, new Vector2(920, 460), new Color(1f, 1f, 1f, 0.06f));
    }

    void ShowResult(string title, Color c, string sub)
    {
        resultTitle.text = title; resultTitle.color = c;
        resultSub.text = sub; resultSub.color = Color.white;
        resultTitle.gameObject.SetActive(true);
        resultSub.gameObject.SetActive(true);
    }

    void Flash(Color c, float a, float rate) { flashColor = c; flashA = a; flashRate = rate; }
    void Shake(float mag, float dur) { shakeMag = mag; shakeDur = Mathf.Max(0.01f, dur); shakeT = dur; }

    // =================================================================
    //  UPDATE
    // =================================================================
    void Update()
    {
        float dt = Time.deltaTime;

        radarSweep.localRotation = Quaternion.Euler(0, 0, -(Time.time * 120f) % 360f);
        float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 3f));
        for (int i = 0; i < dots.Count; i++)
            if (dots[i] != null) dots[i].color = new Color(0.45f, 1f, 0.55f, blink);

        lampGlow.color = new Color(1f, 0.8f, 0.5f, 0.20f + 0.06f * Mathf.PerlinNoise(Time.time * 7f, 0.5f));

        if (state == State.Playing)
        {
            timeLeft -= dt;
            int sec = Mathf.Max(0, Mathf.CeilToInt(timeLeft));
            if (sec != lastShownSecond)
            {
                lastShownSecond = sec;
                hudTimer.text = (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
                if (sec <= 10 && sec > 0) sfx.PlayOneShot(clipClick, 0.5f);
            }
            hudTimer.color = timeLeft <= 10f
                ? (Mathf.Sin(Time.time * 8f) > 0f ? new Color(1f, 0.2f, 0.15f) : new Color(1f, 0.6f, 0.5f))
                : new Color(0.85f, 1f, 0.8f);
            if (timeLeft <= 0f) { timeLeft = 0f; TimeUp(); }
        }

        UpdateParticles(dt);

        for (int i = flyers.Count - 1; i >= 0; i--)
        {
            var f = flyers[i];
            if (f.rt == null) { flyers.RemoveAt(i); continue; }
            f.rt.anchoredPosition += f.vel * dt;
            if (f.rt.anchoredPosition.x > 620f) { Destroy(f.rt.gameObject); flyers.RemoveAt(i); }
        }

        lightsAlpha = Mathf.MoveTowards(lightsAlpha, lightsTarget, dt * 1.5f);
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i].color = new Color(1f, 1f, 0.85f, 0.17f * lightsAlpha);
            lights[i].rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 0.8f + i * 2.1f) * 22f + (i - 1) * 10f);
        }

        if (flashA > 0f)
        {
            flashA = Mathf.Max(0f, flashA - dt * flashRate);
            flashImg.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashA);
        }

        if (shakeT > 0f)
        {
            shakeT -= dt;
            float k = Mathf.Clamp01(shakeT / shakeDur);
            content.anchoredPosition = Random.insideUnitCircle * shakeMag * k;
            if (shakeT <= 0f) content.anchoredPosition = Vector2.zero;
        }
    }

    void UpdateParticles(float dt)
    {
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var p = parts[i];
            p.life -= dt;
            if (p.rt == null) { parts.RemoveAt(i); continue; }
            if (p.life <= 0f) { Destroy(p.rt.gameObject); parts.RemoveAt(i); continue; }
            float k = 1f - p.life / p.max;
            p.vel.y += p.gy * dt;
            p.rt.anchoredPosition += p.vel * dt;
            p.rt.localScale = Vector3.one * Mathf.Lerp(p.s0, p.s1, k);
            p.img.color = Color.Lerp(p.c0, p.c1, k);
        }
    }
}
