#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>PVO > Build Scene: builds the whole bunker inside Project-X-map using placeholder cubes.
/// Replace any placeholder under PVO_Stage with your own model (keep the PvoClickable + collider objects).</summary>
public static class PvoSceneBuilder
{
    static Material Save(Material m, string n)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/Pvo")) AssetDatabase.CreateFolder("Assets/Materials", "Pvo");
        string p = "Assets/Materials/Pvo/" + n + ".mat";
        AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(m, p); return m;
    }
    static Material Lit(string n, Color c, float s = .3f, float mt = 0f, Color? e = null) { return Save(PvoFx.Lit(c, s, mt, e), n); }
    static GameObject Prim(PrimitiveType t, string n, Transform p, Vector3 pos, Vector3 sc, Material m, bool col = false, Vector3? eul = null)
    {
        var g = GameObject.CreatePrimitive(t); g.name = n; g.transform.SetParent(p, false);
        g.transform.localPosition = pos; g.transform.localScale = sc; if (eul.HasValue) g.transform.localEulerAngles = eul.Value;
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!col) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }
    static GameObject Empty(string n, Transform p, Vector3 pos, Vector3? eul = null)
    {
        var g = new GameObject(n); g.transform.SetParent(p, false); g.transform.localPosition = pos;
        if (eul.HasValue) g.transform.localEulerAngles = eul.Value; return g;
    }
    static TextMesh Txt(string n, Transform p, Vector3 pos, string s, float size, Color c, TextAnchor a = TextAnchor.MiddleCenter, Vector3? eul = null)
    {
        var g = Empty(n, p, pos, eul ?? new Vector3(0, 180, 0)); var tm = g.AddComponent<TextMesh>();
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = f; tm.text = s; tm.characterSize = size; tm.fontSize = 64; tm.anchor = a; tm.alignment = TextAlignment.Center; tm.color = c;
        g.GetComponent<MeshRenderer>().sharedMaterial = f.material; return tm;
    }
    static void Click(GameObject g, PvoClickable.Type t, string tip) { var c = g.AddComponent<PvoClickable>(); c.type = t; c.tip = tip; }
    static Quaternion Look(Vector3 from, Vector3 to) { return Quaternion.LookRotation(to - from); }

    [MenuItem("PVO/Build Scene")]
    public static void Build()
    {
        var old = GameObject.Find("PVO_Stage"); if (old) Object.DestroyImmediate(old);
        var rootGo = new GameObject("PVO_Stage"); var st = rootGo.transform;
        var game = rootGo.AddComponent<PvoGame>();

        // --- room (FBX)
        var room = GameObject.Find("Project-X-map");
        if (!room) room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Project-X-map.fbx"));
        var concrete = Lit("Concrete", new Color(.30f, .32f, .30f), .12f);
        foreach (var fc in room.GetComponentsInChildren<Camera>(true)) fc.enabled = false;
        foreach (var r in room.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = concrete; r.shadowCastingMode = ShadowCastingMode.TwoSided; }
        Prim(PrimitiveType.Cube, "Ceiling", st, new Vector3(0, 2.97f, 0), new Vector3(4.1f, .12f, 3.3f), concrete);
        Prim(PrimitiveType.Cube, "FrontWall", st, new Vector3(0, 1.46f, 1.6f), new Vector3(4.3f, 2.91f, .12f), concrete);

        // --- desk
        var wood = Lit("Wood", new Color(.16f, .10f, .06f), .35f);
        Prim(PrimitiveType.Cube, "DeskTop", st, new Vector3(0, .86f, -1.15f), new Vector3(2.7f, .08f, .85f), wood);
        foreach (var x in new[] { -1.28f, 1.28f }) foreach (var z in new[] { -1.52f, -.78f })
            Prim(PrimitiveType.Cube, "DeskLeg", st, new Vector3(x, .43f, z), new Vector3(.06f, .86f, .06f), wood);

        var metal = Lit("MetalDark", new Color(.06f, .065f, .06f), .5f, .6f);
        var olive = Lit("Olive", new Color(.12f, .16f, .1f), .35f, .3f);
        var knobM = Lit("Knob", new Color(.05f, .05f, .045f), .55f, .7f);
        var brass = Lit("Brass", new Color(.5f, .38f, .12f), .6f, .8f);
        var green = new Color(.45f, 1f, .6f);

        // --- telegraph
        var tele = Empty("Telegraph", st, new Vector3(-.9f, .9f, -1.25f));
        Prim(PrimitiveType.Cube, "Model_Body", tele.transform, new Vector3(0, .09f, 0), new Vector3(.5f, .18f, .38f), olive);
        Prim(PrimitiveType.Cylinder, "Model_Roller", tele.transform, new Vector3(0, .2f, .06f), new Vector3(.04f, .22f, .04f), metal, false, new Vector3(0, 0, 90));
        var paper = Prim(PrimitiveType.Quad, "Paper", tele.transform, new Vector3(0, .38f, .12f), new Vector3(.26f, .34f, 1), Lit("Paper", new Color(.85f, .8f, .65f), .1f), false, new Vector3(0, 180, 0));
        var telegraphText = Txt("Text", paper.transform, new Vector3(-.4f, 0, -.002f), "", .0115f, new Color(.1f, .08f, .05f), TextAnchor.MiddleLeft, Vector3.zero);
        telegraphText.alignment = TextAlignment.Left;

        // --- radio
        var radio = Empty("Radio", st, new Vector3(-.45f, .9f, -.93f));
        Prim(PrimitiveType.Cube, "Model_Body", radio.transform, new Vector3(0, .07f, 0), new Vector3(.78f, .14f, .34f), olive);
        Prim(PrimitiveType.Cube, "Model_Display", radio.transform, new Vector3(0, .24f, -.12f), new Vector3(.5f, .16f, .02f), Lit("Glass", new Color(.02f, .03f, .02f), .9f));
        var radioText = Txt("Text", radio.transform, new Vector3(0, .26f, -.105f), "", .0042f, new Color(1f, .75f, .3f));
        var waveGo = Empty("Wave", radio.transform, new Vector3(0, .19f, -.105f));
        var wave = waveGo.AddComponent<LineRenderer>(); wave.useWorldSpace = false; wave.positionCount = 48; wave.widthMultiplier = .003f;
        wave.sharedMaterial = Save(PvoFx.Unlit(new Color(.4f, 3f, .8f, 1)), "Wave"); wave.shadowCastingMode = ShadowCastingMode.Off;
        var kS = Prim(PrimitiveType.Cylinder, "KnobSector", radio.transform, new Vector3(-.2f, .16f, .06f), new Vector3(.1f, .03f, .1f), knobM, true);
        var kH = Prim(PrimitiveType.Cylinder, "KnobHeight", radio.transform, new Vector3(.2f, .16f, .06f), new Vector3(.1f, .03f, .1f), knobM, true);
        var ptrM = Lit("Pointer", Color.white, .2f, 0, new Color(1, 1, 1)); foreach (var k in new[] { kS, kH }) Prim(PrimitiveType.Cube, "Pointer", k.transform, new Vector3(0, 1.05f, .3f), new Vector3(.12f, .12f, .45f), ptrM);
        Click(kS, PvoClickable.Type.SectorKnob, "Сектор: ЛКМ / коліщатко — далі, ПКМ — назад");
        Click(kH, PvoClickable.Type.HeightKnob, "Висота: ЛКМ / коліщатко — далі, ПКМ — назад");
        Txt("LabelS", radio.transform, new Vector3(-.2f, .085f, .172f), "СЕКТОР", .0028f, new Color(.8f, .8f, .7f));
        Txt("LabelH", radio.transform, new Vector3(.2f, .085f, .172f), "ВИСОТА", .0028f, new Color(.8f, .8f, .7f));

        // --- radar
        var radar = Empty("Radar", st, new Vector3(0, .9f, -1.32f));
        Prim(PrimitiveType.Cube, "Model_Base", radar.transform, new Vector3(0, .04f, 0), new Vector3(.36f, .08f, .24f), metal);
        Prim(PrimitiveType.Cube, "Model_Neck", radar.transform, new Vector3(0, .2f, 0), new Vector3(.1f, .24f, .08f), metal);
        var face = Empty("RadarFace", radar.transform, new Vector3(0, .38f, 0), new Vector3(0, 180, 0));
        Prim(PrimitiveType.Cylinder, "Model_Bezel", face.transform, new Vector3(0, 0, .03f), new Vector3(.6f, .03f, .6f), metal, false, new Vector3(90, 0, 0));
        var screenM = Save(PvoFx.Unlit(new Color(1.5f, 1.5f, 1.5f, 1), PvoFx.RadarGrid(), false, true), "RadarScreen");
        var screen = Prim(PrimitiveType.Quad, "Screen", face.transform, new Vector3(0, 0, -.006f), new Vector3(.46f, .46f, 1), screenM);
        var sweepM = Save(PvoFx.Unlit(new Color(.9f, 1.2f, .9f, 1), PvoFx.Sweep(), true), "RadarSweep");
        var sweep = Prim(PrimitiveType.Quad, "Sweep", face.transform, new Vector3(0, 0, -.009f), new Vector3(.46f, .46f, 1), sweepM);
        var dotM = Lit("RadarDot", Color.black, 0, 0, new Color(.6f, 4f, 1.2f));
        var dots = new Transform[6];
        for (int i = 0; i < 6; i++) { var d = Prim(PrimitiveType.Sphere, "Dot" + i, face.transform, new Vector3(0, 0, -.016f), Vector3.one * .022f, dotM); d.SetActive(false); dots[i] = d.transform; }
        string[] rows = { "A", "Б", "В" };
        for (int r = 0; r < 3; r++) Txt("Row" + rows[r], face.transform, new Vector3(-.205f, (.5f - (r + .5f) / 3f) * .46f * .82f, -.012f), rows[r], .0032f, new Color(.4f, .9f, .5f), TextAnchor.MiddleCenter, Vector3.zero);
        for (int c = 0; c < 3; c++) Txt("Col" + (c + 1), face.transform, new Vector3((-.5f + (c + .5f) / 3f) * .46f * .82f, .2f, -.012f), (c + 1).ToString(), .0032f, new Color(.4f, .9f, .5f), TextAnchor.MiddleCenter, Vector3.zero);
        var radarGlow = Empty("RadarGlow", radar.transform, new Vector3(0, .38f, -.25f)).AddComponent<Light>();
        radarGlow.type = LightType.Point; radarGlow.color = new Color(.3f, 1f, .5f); radarGlow.intensity = .25f; radarGlow.range = 1.4f;

        // --- phone
        var phone = Empty("Phone", st, new Vector3(.95f, .9f, -1.28f));
        Prim(PrimitiveType.Cube, "Model_Base", phone.transform, new Vector3(0, .05f, 0), new Vector3(.3f, .1f, .24f), metal);
        Prim(PrimitiveType.Cylinder, "Model_Dial", phone.transform, new Vector3(0, .105f, .03f), new Vector3(.14f, .01f, .14f), brass);
        var recv = Empty("Receiver", phone.transform, new Vector3(0, .15f, -.02f));
        Prim(PrimitiveType.Cube, "Model_Handset", recv.transform, Vector3.zero, new Vector3(.28f, .04f, .07f), metal);
        var rc = recv.AddComponent<BoxCollider>(); rc.size = new Vector3(.3f, .08f, .1f);
        Click(recv, PvoClickable.Type.Phone, "Телефон: зателефонувати в штаб (1.5 с)");

        // --- fire box
        var fire = Empty("FireBox", st, new Vector3(.5f, .9f, -.9f));
        Prim(PrimitiveType.Cube, "Model_Base", fire.transform, new Vector3(0, .05f, 0), new Vector3(.36f, .1f, .3f), olive);
        var btn = Prim(PrimitiveType.Cylinder, "Button", fire.transform, new Vector3(0, .12f, 0), new Vector3(.12f, .03f, .12f), Lit("RedButton", new Color(.6f, .02f, .02f), .4f, 0, new Color(1.6f, .05f, .05f)), true);
        Click(btn, PvoClickable.Type.FireButton, "ОГОНЬ ПВО (потрібно підняти кришку)");
        var pivot = Empty("CoverPivot", fire.transform, new Vector3(0, .1f, -.14f));
        var cover = Prim(PrimitiveType.Cube, "Cover", pivot.transform, new Vector3(0, .04f, .13f), new Vector3(.28f, .08f, .26f), Save(PvoFx.Unlit(new Color(1f, .2f, .2f, .35f), null, false, true), "CoverGlass"), true);
        Click(cover, PvoClickable.Type.FireCover, "Захисна кришка: клік — підняти / опустити");

        // --- camera views
        var deskPos = new Vector3(0, 1.5f, -.1f); var winPos = new Vector3(.55f, 1.6f, .05f);
        var deskView = Empty("DeskView", st, deskPos); deskView.transform.rotation = Look(deskPos, new Vector3(.05f, 1f, -1.15f));
        var winView = Empty("WindowView", st, winPos); winView.transform.rotation = Look(winPos, new Vector3(1.7f, 1.75f, -.38f));

        // --- window glass (cracks)
        Vector3 outDir = new Vector3(.991f, 0, -.135f);
        var glass = Prim(PrimitiveType.Quad, "WindowGlass", st, new Vector3(1.69f, 1.885f, -.38f), new Vector3(1.49f, 1.39f, 1), Save(PvoFx.Unlit(new Color(1, 1, 1, 0), PvoFx.Cracks(), false, true, true), "WindowCracks"));
        glass.transform.rotation = Quaternion.LookRotation(outDir);

        // --- lights
        var lampGo = Empty("MainLamp", st, new Vector3(0, 2.55f, -.6f)); var lamp = lampGo.AddComponent<Light>();
        lamp.type = LightType.Point; lamp.color = new Color(1f, .72f, .4f); lamp.range = 6f; lamp.shadows = LightShadows.Soft;
        var fl = lampGo.AddComponent<LightFlicker>(); fl.baseIntensity = 2.6f; lamp.intensity = 2.6f;
        Prim(PrimitiveType.Sphere, "Model_Bulb", lampGo.transform, Vector3.zero, Vector3.one * .1f, Lit("Bulb", Color.black, 0, 0, new Color(4f, 2.6f, 1.2f)));
        Prim(PrimitiveType.Cylinder, "Model_Cord", lampGo.transform, new Vector3(0, .18f, 0), new Vector3(.01f, .18f, .01f), metal);
        var deskLampGo = Empty("DeskLamp", st, new Vector3(-.55f, 1.55f, -.95f)); deskLampGo.transform.rotation = Look(deskLampGo.transform.position, new Vector3(-.2f, .95f, -1.2f));
        var dl = deskLampGo.AddComponent<Light>(); dl.type = LightType.Spot; dl.spotAngle = 80; dl.range = 3f; dl.intensity = 2.4f; dl.color = new Color(1f, .8f, .5f); dl.shadows = LightShadows.Soft;
        var flashGo = Empty("OutsideFlash", st, new Vector3(1.3f, 2f, -.4f)); var flash = flashGo.AddComponent<Light>();
        flash.type = LightType.Point; flash.color = new Color(1f, .55f, .25f); flash.range = 14f; flash.intensity = 0f;

        var moon = GameObject.Find("Directional Light").GetComponent<Light>(); moon.transform.rotation = Quaternion.LookRotation(new Vector3(-.75f, -.45f, .18f));
        moon.color = new Color(.55f, .68f, 1f); moon.intensity = .35f; moon.shadows = LightShadows.Soft; moon.shadowStrength = .95f;

        // --- outside world
        var outsideGo = Empty("Outside", st, new Vector3(1.9f, 1.9f, -.4f)); outsideGo.transform.rotation = Quaternion.LookRotation(outDir); var o = outsideGo.transform;
        Prim(PrimitiveType.Cube, "Ground", o, new Vector3(0, -8.5f, 250), new Vector3(1200, 1, 1200), Lit("Ground", new Color(.025f, .03f, .035f), .1f));
        var bld = Lit("Building", new Color(.05f, .055f, .07f), .2f); var win = Lit("BuildingWindow", Color.black, 0, 0, new Color(2.2f, 1.4f, .5f));
        Random.InitState(5);
        for (int i = 0; i < 46; i++)
        {
            float w = Random.Range(8f, 18f), h = Random.Range(8f, 40f), x = Random.Range(-170f, 170f), z = Random.Range(110f, 260f);
            Prim(PrimitiveType.Cube, "Building" + i, o, new Vector3(x, -8f + h / 2f, z), new Vector3(w, h, w), bld);
            if (i % 3 == 0) Prim(PrimitiveType.Cube, "Lit" + i, o, new Vector3(x + Random.Range(-w / 3f, w / 3f), -8f + h * Random.Range(.3f, .9f), z - w / 2f - .1f), new Vector3(1.4f, 1.4f, .1f), win);
        }
        var beamM = Save(PvoFx.Unlit(new Color(.5f, .65f, 1f, .12f), null, true, false, true), "SearchBeam");
        float[] bx = { -60, 40, 90 };
        for (int i = 0; i < 3; i++)
        {
            var sl = Empty("Searchlight" + i, o, new Vector3(bx[i], -8f, 110 + i * 25));
            Prim(PrimitiveType.Cube, "Model_Base", sl.transform, new Vector3(0, 1f, 0), new Vector3(3, 2, 3), metal);
            var beam = Empty("Beam", sl.transform, new Vector3(0, 2f, 0)); beam.AddComponent<MeshRenderer>().sharedMaterial = beamM; beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var sw = beam.AddComponent<SearchLightSweep>(); sw.phase = i * 2.1f; sw.speed = .6f + i * .15f;
        }
        Prim(PrimitiveType.Sphere, "MoonDisc", o, new Vector3(-120, 150, 700), Vector3.one * 55f, Lit("Moon", Color.black, 0, 0, new Color(2.2f, 2.3f, 2.6f)));
        var glowQ = Prim(PrimitiveType.Quad, "HorizonGlow", o, new Vector3(0, 20f, 900), new Vector3(1600, 200, 1), Save(PvoFx.Unlit(new Color(1.2f, .45f, .15f, .35f), PvoFx.Glow(), true), "HorizonGlow"));
        glowQ.transform.localRotation = Quaternion.Euler(0, 180, 0);

        // --- environment, camera, post
        RenderSettings.skybox = null; RenderSettings.sun = moon;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.06f, .065f, .09f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = .0014f; RenderSettings.fogColor = new Color(.035f, .045f, .07f);
        var cam = Camera.main; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.012f, .016f, .035f);
        cam.fieldOfView = 60; cam.nearClipPlane = .05f; cam.farClipPlane = 1500;
        cam.transform.SetPositionAndRotation(deskView.transform.position, deskView.transform.rotation);
        var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        var vol = GameObject.Find("Global Volume").GetComponent<Volume>();
        if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
        AssetDatabase.DeleteAsset("Assets/Settings/PvoProfile.asset");
        var prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, "Assets/Settings/PvoProfile.asset");
        var bl = prof.Add<Bloom>(true); bl.intensity.Override(1.1f); bl.threshold.Override(.9f); bl.scatter.Override(.7f);
        var vg = prof.Add<Vignette>(true); vg.intensity.Override(.32f); vg.smoothness.Override(.5f);
        var ca = prof.Add<ChromaticAberration>(true); ca.intensity.Override(.06f);
        var fg = prof.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(.35f);
        var co = prof.Add<ColorAdjustments>(true); co.postExposure.Override(0f); co.contrast.Override(12f); co.saturation.Override(-15f);
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        foreach (var comp in prof.components) AssetDatabase.AddObjectToAsset(comp, prof); EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets(); vol.sharedProfile = prof; vol.isGlobal = true;

        // --- wire the game
        game.cam = cam; game.deskView = deskView.transform; game.windowView = winView.transform; game.outside = o;
        game.knobSector = kS.transform; game.knobHeight = kH.transform; game.receiver = recv.transform; game.fireCover = pivot.transform; game.fireButton = btn.transform;
        game.paper = paper.transform; game.radarSweep = sweep.transform; game.telegraphText = telegraphText; game.radioText = radioText; game.radioWave = wave;
        game.radarDots = dots; game.radarScreen = screen.GetComponent<Renderer>(); game.glass = glass.GetComponent<Renderer>();
        game.lamp = lamp; game.moon = moon; game.flash = flash; game.volume = vol;
        EditorUtility.SetDirty(game);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("PVO scene built.");
    }
}
#endif
