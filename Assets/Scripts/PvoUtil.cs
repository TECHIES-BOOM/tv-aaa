using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Input wrapper that works with the new Input System and the legacy Input Manager.</summary>
public static class PvoIn
{
    public enum K { Q, E, R, Space, Tab }

    public static Vector2 Pointer()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }
    public static bool Pressed(int b)
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current; if (m == null) return false;
        return b == 0 ? m.leftButton.wasPressedThisFrame : m.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(b);
#endif
    }
    public static float Scroll()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }
    public static bool Shift()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }
    public static bool Down(K k)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current; if (kb == null) return false;
        switch (k)
        {
            case K.Q: return kb.qKey.wasPressedThisFrame;
            case K.E: return kb.eKey.wasPressedThisFrame;
            case K.R: return kb.rKey.wasPressedThisFrame;
            case K.Space: return kb.spaceKey.wasPressedThisFrame;
            default: return kb.tabKey.wasPressedThisFrame;
        }
#else
        switch (k)
        {
            case K.Q: return Input.GetKeyDown(KeyCode.Q);
            case K.E: return Input.GetKeyDown(KeyCode.E);
            case K.R: return Input.GetKeyDown(KeyCode.R);
            case K.Space: return Input.GetKeyDown(KeyCode.Space);
            default: return Input.GetKeyDown(KeyCode.Tab);
        }
#endif
    }
}

/// <summary>Procedural placeholder sounds. Replace by assigning your own snd_* clips on PvoGame.</summary>
public static class PvoSynth
{
    const int SR = 44100;
    static readonly System.Random rng = new System.Random(11);
    static float R() { return (float)(rng.NextDouble() * 2 - 1); }
    const float TAU = 6.2831853f;

    static AudioClip Make(string n, float dur, System.Func<float, float, float> f)
    {
        int c = Mathf.CeilToInt(dur * SR); var d = new float[c];
        for (int i = 0; i < c; i++) d[i] = Mathf.Clamp(f(i / (float)SR, dur), -1f, 1f);
        var clip = AudioClip.Create(n, c, 1, SR, false); clip.SetData(d, 0); return clip;
    }
    public static AudioClip Click() { return Make("click", .06f, (t, d) => (Mathf.Sin(TAU * 1500 * t) * .5f + R() * .5f) * Mathf.Exp(-t * 90f) * .9f); }
    public static AudioClip Key() { return Make("key", .035f, (t, d) => R() * Mathf.Exp(-t * 150f) * .6f); }
    public static AudioClip Print() { return Make("print", .6f, (t, d) => R() * Mathf.Exp(-t * 6f) * .25f * (Mathf.Sin(t * 260f) > 0 ? 1f : .3f)); }
    public static AudioClip Static() { float s = 0; return Make("static", 2f, (t, d) => { s += (R() - s) * .55f; return s * .6f; }); }
    public static AudioClip Tuned() { return Make("tuned", 1f, (t, d) => .3f * (Mathf.Sin(TAU * 440 * t) + .5f * Mathf.Sin(TAU * 880 * t))); }
    public static AudioClip Hum() { float s = 0; return Make("amb", 4f, (t, d) => { s += (R() - s) * .015f; return .22f * Mathf.Sin(TAU * 50 * t) + .1f * Mathf.Sin(TAU * 100 * t) + s * .9f; }); }
    public static AudioClip Dial() { return Make("dial", 1.5f, (t, d) => { float p = Mathf.Repeat(t, .17f); return (Mathf.Sin(TAU * 1100 * p) * .4f + R() * .4f) * Mathf.Exp(-p * 120f); }); }
    public static AudioClip Beep() { return Make("beep", .25f, (t, d) => Mathf.Sin(TAU * 620 * t) * Mathf.Exp(-t * 9f) * .45f); }
    public static AudioClip Shot() { float s = 0; return Make("shot", .35f, (t, d) => { s += (R() - s) * .25f; return s * 1.6f * Mathf.Exp(-t * 14f); }); }
    public static AudioClip Boom(float dur, float lp, float vol)
    {
        float s = 0;
        return Make("boom", dur, (t, d) => { s += (R() - s) * lp; return (s * 2f + Mathf.Sin(TAU * (45f - 15f * t / d) * t) * .7f) * Mathf.Exp(-6.4f * t / d) * vol; });
    }
    public static AudioClip Engine()
    {
        float ph = 0;
        return Make("engine", 3.5f, (t, d) => { float f = 52f + 5f * Mathf.Sin(TAU * t / d); ph += TAU * f / SR; float env = Mathf.Sin(Mathf.PI * t / d); return (Mathf.Sin(ph) + .6f * Mathf.Sin(ph * 2f) + .3f * Mathf.Sin(ph * 3.01f)) * .3f * env; });
    }
    public static AudioClip Siren()
    {
        float ph = 0;
        return Make("siren", 3f, (t, d) => { float f = 560f + 260f * Mathf.Sin(TAU * t * .6f); ph += TAU * f / SR; return Mathf.Sin(ph) * .3f; });
    }
}

/// <summary>Materials, procedural textures and particle effects used by the game.</summary>
public static class PvoFx
{
    public static Material Lit(Color c, float smooth = .3f, float metal = 0f, Color? emis = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        if (emis.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emis.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        return m;
    }
    public static Material Unlit(Color c, Texture tex = null, bool additive = false, bool transparent = false, bool twoSided = false)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetColor("_BaseColor", c); if (tex != null) m.SetTexture("_BaseMap", tex);
        if (additive || transparent)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
        }
        if (twoSided) m.SetFloat("_Cull", 0);
        return m;
    }

    static Texture2D soft;
    public static Texture2D Soft()
    {
        if (soft != null) return soft;
        int n = 64; soft = new Texture2D(n, n, TextureFormat.RGBA32, false); soft.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            { float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f); float a = Mathf.Clamp01(1 - d); soft.SetPixel(x, y, new Color(1, 1, 1, a * a)); }
        soft.Apply(); return soft;
    }
    public static Texture2D RadarGrid()
    {
        int n = 256; var t = new Texture2D(n, n, TextureFormat.RGBA32, false); t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
            if (r > 1f) { t.SetPixel(x, y, Color.clear); continue; }
            float g = .06f + .05f * (1 - r);
            float line = 0;
            foreach (float k in new[] { -1f / 3f, 1f / 3f }) { line = Mathf.Max(line, 1 - Mathf.Clamp01(Mathf.Abs(u - k) / .012f)); line = Mathf.Max(line, 1 - Mathf.Clamp01(Mathf.Abs(v - k) / .012f)); }
            foreach (float k in new[] { .33f, .66f }) line = Mathf.Max(line, (1 - Mathf.Clamp01(Mathf.Abs(r - k) / .01f)) * .5f);
            float rim = 1 - Mathf.Clamp01(Mathf.Abs(r - .985f) / .012f);
            t.SetPixel(x, y, new Color(.05f + .1f * rim, g + line * .6f + rim * .5f, .04f + line * .2f, 1));
        }
        t.Apply(); return t;
    }
    public static Texture2D Sweep()
    {
        int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, false); t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
            if (u * u + v * v > 1f) { t.SetPixel(x, y, Color.clear); continue; }
            float a = Mathf.Repeat(Mathf.Atan2(v, u), Mathf.PI * 2) / (Mathf.PI * 2);
            t.SetPixel(x, y, new Color(.3f, 1f, .5f, Mathf.Pow(a, 7f) * .9f));
        }
        t.Apply(); return t;
    }
    public static Texture2D Cracks()
    {
        int n = 512; var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color[n * n];
        var rnd = new System.Random(3); System.Action<float, float, float, int> walk = null;
        walk = (x, y, a, len) =>
        {
            for (int i = 0; i < len; i++)
            {
                a += (float)(rnd.NextDouble() - .5) * .35f; x += Mathf.Cos(a) * 2f; y += Mathf.Sin(a) * 2f;
                int xi = (int)x, yi = (int)y; if (xi < 1 || yi < 1 || xi >= n - 2 || yi >= n - 2) return;
                float al = 1f - i / (float)len * .5f;
                px[yi * n + xi] = new Color(1, 1, 1, al); px[yi * n + xi + 1] = new Color(1, 1, 1, al * .5f);
                if (rnd.NextDouble() < .02) walk(x, y, a + (float)(rnd.NextDouble() - .5) * 1.6f, len - i - 10);
            }
        };
        for (int k = 0; k < 9; k++) walk(n * .45f, n * .55f, k * 6.2831853f / 9f, 260);
        t.SetPixels(px); t.Apply(); return t;
    }
    public static Texture2D Glow()
    {
        int h = 64; var t = new Texture2D(2, h, TextureFormat.RGBA32, false); t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++) { float a = Mathf.Pow(1f - y / (h - 1f), 2.2f); for (int x = 0; x < 2; x++) t.SetPixel(x, y, new Color(1, 1, 1, a)); }
        t.Apply(); return t;
    }

    static ParticleSystem NewPS(Transform parent, string n)
    {
        var go = new GameObject(n); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); return ps;
    }
    static void Burst(Transform parent, string n, Material mat, float life, float speed, float size, int count, Color c0, Color c1, float grav)
    {
        var ps = NewPS(parent, n); var main = ps.main;
        main.loop = false; main.duration = 1f; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .3f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
        main.gravityModifier = grav; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = Mathf.Max(.05f, size * .25f);
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, .4f, 1, 1.4f));
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
    }
    public static void Explosion(Vector3 pos, float size)
    {
        var go = new GameObject("Boom"); go.transform.position = pos; var tr = go.transform;
        Burst(tr, "Fire", Unlit(new Color(3f, 1.6f, .5f, 1), Soft(), true), 1.1f, size * .8f, size * .9f, 26, new Color(1, .7f, .3f, 1), new Color(1, .3f, .05f, 1), -.1f);
        Burst(tr, "Smoke", Unlit(new Color(.07f, .07f, .08f, .9f), Soft(), false, true), 3.5f, size * .25f, size * 1.2f, 14, new Color(.3f, .3f, .3f, 1), new Color(.1f, .1f, .1f, 1), -.05f);
        Burst(tr, "Sparks", Unlit(new Color(4f, 2.5f, 1f, 1), Soft(), true), 1.5f, size * 1.6f, size * .06f, 40, Color.white, new Color(1, .6f, .2f, 1), .6f);
        Object.Destroy(go, 6f);
    }
    public static void DustBurst(Vector3 pos)
    {
        var go = new GameObject("DustBurst"); go.transform.position = pos;
        Burst(go.transform, "D", Unlit(new Color(.7f, .7f, .7f, .5f), Soft(), false, true), 4f, .4f, .06f, 120, new Color(.6f, .6f, .6f, .6f), new Color(.4f, .4f, .4f, .4f), .05f);
        Object.Destroy(go, 6f);
    }
    public static void Burning(Transform t)
    {
        var ps = NewPS(t, "Burn"); var main = ps.main;
        main.loop = true; main.startLifetime = 1.3f; main.startSpeed = 2f; main.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .6f, .2f, 1), new Color(1, .25f, .05f, 1)); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 45f;
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        col.color = g;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Unlit(new Color(2.5f, 1.2f, .4f, 1), Soft(), true);
        ps.Play();
    }
    public static void Stars()
    {
        var ps = NewPS(null, "Stars"); var main = ps.main;
        main.loop = false; main.duration = 1f; main.startLifetime = 100000f; main.startSpeed = 0f; main.maxParticles = 800;
        main.startSize = new ParticleSystem.MinMaxCurve(.8f, 2.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.7f, .8f, 1, .8f), Color.white); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)600) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 600f; sh.radiusThickness = 0f;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Unlit(new Color(2f, 2f, 2.4f, 1), Soft(), true);
        ps.Play();
    }
    public static void Dust(Transform parent)
    {
        var ps = NewPS(parent, "Dust"); ps.transform.position = new Vector3(0, 1.4f, -.1f); var main = ps.main;
        main.loop = true; main.prewarm = true; main.startLifetime = 22f; main.startSpeed = .02f; main.maxParticles = 250;
        main.startSize = new ParticleSystem.MinMaxCurve(.005f, .011f); main.startColor = new Color(.8f, .85f, 1f, .5f); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 12f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3.4f, 2.4f, 2.6f);
        var nz = ps.noise; nz.enabled = true; nz.strength = .08f; nz.frequency = .25f;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Unlit(new Color(1, 1, 1, .5f), Soft(), true);
        ps.Play();
    }
}
