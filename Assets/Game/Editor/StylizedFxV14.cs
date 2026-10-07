using System.IO;
using System.Linq;
using StoneSignal.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PSS = UnityEngine.ParticleSystem;

/// v14 art: PF_VFX_CoinDrop (CoinDropFx), PF_VFX_EnemyGround (EnemyGroundFx, added as child "GroundFx" to PF_Enemy_*), previews.
public static class StylizedFxV14
{
    const string Pf = StylizedArtIntegration.PrefabDir;
    const string MatDir = "Assets/Game/Materials/Stylized/";
    const string Gen = StylizedArtIntegration.ArtDir + "Generated/";
    const string PreviewDir = "Verification/StylizedArt1/";
    static Color H(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    static Material M(string name, bool add, int shape, float soft = .5f)
    {
        string p = MatDir + name + ".mat"; var sh = Shader.Find(add ? "StoneSignal/FXAdditive" : "StoneSignal/FXAlpha");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.SetFloat("_Shape", shape); m.SetFloat("_Softness", soft); m.SetFloat("_RingWidth", .15f);
        m.SetColor("_TintColor", Color.white); m.SetFloat("_Intensity", add ? 1.6f : 1); m.renderQueue = add ? 3100 : 3000; m.enableInstancing = true;
        EditorUtility.SetDirty(m); return m;
    }
    static ParticleSystem EmitPS(Transform parent, string name, Material mat, float life, float size, Color c, int max)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = false; m.duration = 1; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = life; m.startSize = size; m.startColor = c; m.startSpeed = 0; m.maxParticles = max; m.stopAction = ParticleSystemStopAction.None;
        var e = ps.emission; e.enabled = false; var s = ps.shape; s.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }
    static void FadeOut(ParticleSystem ps, float a0 = 1)
    {
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(a0, 0), new GradientAlphaKey(a0, .4f), new GradientAlphaKey(0, 1) });
        col.color = g;
    }

    public static void BuildAll()
    {
        if (!AssetDatabase.IsValidFolder(Gen.TrimEnd('/'))) AssetDatabase.CreateFolder(StylizedArtIntegration.ArtDir.TrimEnd('/'), "Generated");
        BuildCoinDrop(); BuildEnemyGround(); BuildAtlas(); BuildRunes(); AssetDatabase.SaveAssets();
        Previews();
    }

    static Material IconMat(string name, Texture2D tex)
    {
        string p = MatDir + name + ".mat"; var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3050; m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
    }
    static void BuildCoinDrop()
    {
        const string rd = StylizedArtIntegration.ArtDir + "Rewards";
        if (!AssetDatabase.IsValidFolder(rd)) AssetDatabase.CreateFolder(StylizedArtIntegration.ArtDir.TrimEnd('/'), "Rewards");
        var defs = new[] {
            ("gold",  "ui_coin_gold",    "FFE07A", "FFE27A", "FFB020", .46f, "PF_VFX_RewardFly_Gold"),
            ("shard", "ui_reward_shard", "E0B0FF", "E6C0FF", "9A50FF", .44f, "PF_VFX_RewardFly_Shard"),
            ("gem",   "ui_reward_gem",   "A0F0FF", "C8F6FF", "40A8FF", .44f, "PF_VFX_RewardFly_Gem") };
        foreach (var (id, icon, spk, th, tt, size, pf) in defs)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(StylizedArtIntegration.ArtDir + "UI/" + icon + ".png");
            string rp = rd + "/RT_" + char.ToUpper(id[0]) + id.Substring(1) + ".asset";
            var rt = AssetDatabase.LoadAssetAtPath<RewardType>(rp); if (!rt) { rt = ScriptableObject.CreateInstance<RewardType>(); AssetDatabase.CreateAsset(rt, rp); }
            rt.id = id; rt.icon = tex; rt.material = IconMat("M_FX_RewardIcon_" + id, tex); rt.size = size; rt.sparkle = H(spk); rt.trailHead = H(th); var tc = H(tt); tc.a = 0; rt.trailTail = tc;
            EditorUtility.SetDirty(rt);
            for (int v = 0; v < (id == "gold" ? 2 : 1); v++)
            {
                string name = v == 1 ? "PF_VFX_CoinDrop" : pf;
                var root = new GameObject(name);
                RewardFlyFx fx = id == "gold" && v == 1 ? root.AddComponent<CoinDropFx>() : root.AddComponent<RewardFlyFx>();
                fx.type = rt;
                var icons = EmitPS(root.transform, "Icons", rt.material, 10, size, Color.white, 48);
                var im = icons.main; im.startSize3D = true;
                var r = icons.GetComponent<ParticleSystemRenderer>(); r.sortingFudge = -20;
                var tr = icons.trails; tr.enabled = true; tr.mode = ParticleSystemTrailMode.PerParticle; tr.lifetime = .2f; tr.minVertexDistance = .06f;
                tr.widthOverTrail = new PSS.MinMaxCurve(.7f, AnimationCurve.Linear(0, 1, 1, 0)); tr.dieWithParticles = true; tr.inheritParticleColor = false; tr.sizeAffectsWidth = true;
                var tg = new Gradient(); tg.SetKeys(new[] { new GradientColorKey(rt.trailHead, 0), new GradientColorKey((Color)rt.trailTail, 1) }, new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(0, 1) });
                tr.colorOverTrail = tg; r.trailMaterial = M("M_FX_Add_Trail", true, 7);
                var sp = EmitPS(root.transform, "Sparkle", M("M_FX_Add_Dot", true, 0, .7f), .45f, .14f, rt.sparkle, 24);
                var spm = sp.main; spm.startSpeed = new PSS.MinMaxCurve(1f, 2.4f); spm.startSize = new PSS.MinMaxCurve(.1f, .18f); spm.gravityModifier = .35f;
                var sh = sp.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .15f; FadeOut(sp);
                var fl = EmitPS(root.transform, "AbsorbFlash", M("M_FX_Add_Ring", true, 1), .28f, .9f, rt.sparkle, 6);
                var so = fl.sizeOverLifetime; so.enabled = true; so.size = new PSS.MinMaxCurve(1, AnimationCurve.EaseInOut(0, .3f, 1, 1.8f)); FadeOut(fl);
                fx.icons = icons; fx.sparkle = sp; fx.flash = fl;
                PrefabUtility.SaveAsPrefabAsset(root, Pf + name + ".prefab"); Object.DestroyImmediate(root);
            }
        }
    }

    static Mesh BlobMesh()
    {
        string p = Gen + "MSH_BlobShadow.asset"; var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (!m) { m = new Mesh { name = "MSH_BlobShadow" }; AssetDatabase.CreateAsset(m, p); }
        m.Clear(); m.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.colors = Enumerable.Repeat(new Color(.07f, .04f, .16f, .35f), 4).ToArray();
        m.triangles = new[] { 0, 1, 2, 2, 1, 3 };   // v15: faces -Z -> after Euler(90,0,0) faces +Y (v14 winding faced down and was culled)
        m.RecalculateBounds(); EditorUtility.SetDirty(m); return m;
    }

    /// v17.4 footprint tint: #24160C (dark warm brown, not black), opacity 0.62 (= the suggested #24160C a=0.85 on top of the old
    /// 0.73-ish particle alpha, folded into one number). Effective luminance step: ~-30 on dark bridge planks (v17.3: -22), ~-85 on
    /// light island sand (dev #1E120A x 0.7: -99, the "black stamp" look).
    public static readonly Color FootprintColor = new Color(0x24 / 255f, 0x16 / 255f, 0x0C / 255f, .62f);

    static void BuildEnemyGround()
    {
        // global dust system (one PS, Emit only, cap 64)
        var sysGo = new GameObject("PF_VFX_EnemyGroundSystem"); var sys = sysGo.AddComponent<EnemyGroundFxSystem>();
        // v17.2 footprints: painted paw print (T_FX_Footprint), heading-aligned, 1.5 s (hold 55% then fade). v17.3: #3A2414, alpha 0.7, larger
        // v17.4: THIS builder is the single source of truth for the print colour/opacity (BatchImport rewrites M_VFX_Footprint):
        //   FootprintColor below = #24160C, opacity 0.62 in _BaseColor.a; particle start colour is plain white (alpha 1), so the
        //   material shows exactly what the game draws. Tuned on footprint_tuned/ (dev's #1E120A x 0.7 read as near-black stamps on
        //   the light island sand; #3A2414 x 0.7 was too weak on the dark +x bridge planks). Edit here, not in the .mat.
        const string fpTex = StylizedArtIntegration.ArtDir + "FX/Ground/T_FX_Footprint.png";
        var fti = (TextureImporter)AssetImporter.GetAtPath(fpTex);
        if (fti) { fti.sRGBTexture = true; fti.alphaIsTransparency = true; fti.wrapMode = TextureWrapMode.Clamp; fti.mipmapEnabled = true; fti.maxTextureSize = 128; fti.SaveAndReimport(); }
        // v17.3: SS_GroundPrint (queue 2995 after opaque, ZTest LEqual, depth offset). v17.4: colour AND opacity from the material only
        var psh = Shader.Find("StoneSignal/SS_GroundPrint"); if (!psh) throw new System.Exception("SS_GroundPrint shader missing");
        var fpm = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_VFX_Footprint.mat"); if (!fpm) { fpm = new Material(psh); AssetDatabase.CreateAsset(fpm, MatDir + "M_VFX_Footprint.mat"); }
        fpm.shader = psh; fpm.shaderKeywords = new string[0];
        fpm.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(fpTex)); fpm.SetColor("_BaseColor", FootprintColor);
        fpm.renderQueue = 2995; fpm.enableInstancing = false; EditorUtility.SetDirty(fpm);
        var dust = EmitPS(sysGo.transform, "Dust", fpm, 1.5f, .40f, Color.white, EnemyGroundFxSystem.MaxFootprints);   // v17.4: opacity lives in FootprintColor.a
        dust.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        var so = dust.sizeOverLifetime; so.enabled = false;
        { var col = dust.colorOverLifetime; col.enabled = true; var g2 = new Gradient();
          g2.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .55f), new GradientAlphaKey(0, 1) }); col.color = g2; }
        sys.dust = dust;
        PrefabUtility.SaveAsPrefabAsset(sysGo, Pf + "PF_VFX_EnemyGroundSystem.prefab"); Object.DestroyImmediate(sysGo);

        // per-enemy blob (no particle system)
        var root = new GameObject("PF_VFX_EnemyGround"); var g = root.AddComponent<EnemyGroundFx>();
        var blob = new GameObject("Blob"); blob.transform.SetParent(root.transform, false); blob.transform.localPosition = new Vector3(0, .07f, 0); blob.transform.localRotation = Quaternion.Euler(90, 0, 0);
        blob.AddComponent<MeshFilter>().sharedMesh = BlobMesh();
        var bm = M("M_FX_Alpha_BlobShadow", false, 0, 1f); bm.enableInstancing = true; bm.renderQueue = 2990; EditorUtility.SetDirty(bm);
        var br = blob.AddComponent<MeshRenderer>(); br.sharedMaterial = bm; br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = false;
        g.blob = blob.transform;
        var pfab = PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_VFX_EnemyGround.prefab"); Object.DestroyImmediate(root);
        foreach (var path in Directory.GetFiles(Pf, "PF_Enemy_*.prefab"))
        {
            var c = PrefabUtility.LoadPrefabContents(path);
            var old = c.transform.Find("GroundFx"); if (old) Object.DestroyImmediate(old.gameObject);
            var b = new Bounds(c.transform.position, Vector3.zero); foreach (var rr in c.GetComponentsInChildren<Renderer>()) b.Encapsulate(rr.bounds);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pfab, c.transform); inst.name = "GroundFx"; inst.transform.localPosition = Vector3.zero;
            var fx = inst.GetComponent<EnemyGroundFx>(); fx.radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * .85f, .2f, 1.2f); fx.flying = path.Contains("Flyer");
            inst.transform.Find("Blob").localScale = Vector3.one * fx.radius * 2 * (fx.flying ? .7f : 1);
            PrefabUtility.SaveAsPrefabAsset(c, path); PrefabUtility.UnloadPrefabContents(c);
        }
    }

    const string RuneAtlasPath = StylizedArtIntegration.ArtDir + "Runes/T_RuneGlyphAtlas.png";
    static void BuildRunes()
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(RuneAtlasPath);
        if (ti) { ti.textureType = TextureImporterType.Default; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.sRGBTexture = false; ti.SaveAndReimport(); }
        var atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RuneAtlasPath);
        // default rune atlas on the shared wall/block materials (MPB sets index/colour only)
        foreach (var mp in Directory.GetFiles(MatDir, "*.mat")) { var m = AssetDatabase.LoadAssetAtPath<Material>(mp); if (m && m.HasProperty("_RuneAtlas")) { m.SetTexture("_RuneAtlas", atlasTex); m.SetFloat("_RuneIdx", -1); EditorUtility.SetDirty(m); } }
        // PF_UI_TowerBuffIcons: 4 billboard sprites (HUD atlas) + 4 link lines
        var root = new GameObject("PF_UI_TowerBuffIcons"); var tb = root.AddComponent<TowerBuffIcons>();
        string[] names = { "blade", "swift", "sight", "frost", "bounty", "resonance" };
        for (int i = 0; i < 6; i++) tb.runeIcons[i] = AssetDatabase.LoadAssetAtPath<Sprite>(StylizedArtIntegration.ArtDir + "UI/ui_rune_" + names[i] + ".png");
        var linkMat = M("M_FX_Add_Trail", true, 7);
        for (int i = 0; i < 4; i++)
        {
            var g = new GameObject("Buff" + i); g.transform.SetParent(root.transform, false); var sr = g.AddComponent<SpriteRenderer>(); sr.sprite = tb.runeIcons[i]; sr.sortingOrder = 50; tb.slots[i] = sr; g.SetActive(false);
            var l = new GameObject("Link" + i); l.transform.SetParent(root.transform, false); var lr = l.AddComponent<LineRenderer>(); lr.sharedMaterial = linkMat; lr.positionCount = 2; lr.widthMultiplier = .08f; lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; tb.links[i] = lr; l.SetActive(false);
        }
        PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_UI_TowerBuffIcons.prefab"); Object.DestroyImmediate(root);
        // PF_UI_ResonanceAura: subtle dashed ring, radius 8 cells (quad 17 m incl. cell centre)
        var ringMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/UI/M_UI_RangeRing.mat");
        var rm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/UI/M_UI_ResonanceRing.mat");
        if (!rm) { rm = new Material(ringMat); AssetDatabase.CreateAsset(rm, "Assets/Game/Materials/Stylized/UI/M_UI_ResonanceRing.mat"); }
        rm.CopyPropertiesFromMaterial(ringMat); rm.SetColor("_Color", new Color(.95f, .93f, .9f, .35f)); rm.SetFloat("_Speed", .05f); rm.SetFloat("_Dashes", 96); rm.SetFloat("_Width", .015f); EditorUtility.SetDirty(rm);
        var au = GameObject.CreatePrimitive(PrimitiveType.Quad); au.name = "PF_UI_ResonanceAura"; Object.DestroyImmediate(au.GetComponent<Collider>());
        au.transform.rotation = Quaternion.Euler(90, 0, 0); au.transform.localScale = Vector3.one * (2 * RuneArt.ResonanceRadiusCells + 1);
        var ar = au.GetComponent<Renderer>(); ar.sharedMaterial = rm; ar.shadowCastingMode = ShadowCastingMode.Off; ar.receiveShadows = false;
        var auRoot = new GameObject("PF_UI_ResonanceAura"); au.name = "Ring"; au.transform.SetParent(auRoot.transform, false); au.transform.localPosition = Vector3.up * .06f;
        PrefabUtility.SaveAsPrefabAsset(auRoot, Pf + "PF_UI_ResonanceAura.prefab"); Object.DestroyImmediate(auRoot);
    }

    // ---------------- HUD sprite atlas
    static void BuildAtlas()
    {
        const string ui = StylizedArtIntegration.ArtDir + "UI";
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ui }))
        {
            var tp = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
            var border = ti.spriteBorder;   // keep borders from our .meta
            var nm = System.IO.Path.GetFileNameWithoutExtension(tp);   // v16 draw pile: pin borders (no hand-written metas)
            if (nm == "ui9_draw_bubble") border = new Vector4(30, 28, 30, 28); else if (nm.StartsWith("ui9_reward_frame_")) border = new Vector4(36, 36, 36, 36); else if (nm.StartsWith("ui9_reward_band_")) border = new Vector4(12, 12, 12, 12); else if (nm == "ui9_reward_tier_pill") border = new Vector4(29, 29, 29, 29); else if (nm.StartsWith("ui_reward_") || nm.StartsWith("ui_dir_") || nm == "ui_fx_skull") border = Vector4.zero; else if (nm.StartsWith("ui_draw_") || nm == "ui_icon_free") border = Vector4.zero;
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.alphaIsTransparency = true;
            ti.spriteBorder = border; ti.spritePixelsPerUnit = 100; ti.npotScale = TextureImporterNPOTScale.None; ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();   // rewrites minimal hand-written metas into full Tuanjie metas
        }
        var frame = (TextureImporter)AssetImporter.GetAtPath(ui + "/ui_card_tower_frame.png");
        Debug.Log($"HUD FRAME CHECK: ui_card_tower_frame type={frame.textureType} mode={frame.spriteImportMode} border={frame.spriteBorder}");
        string ap = ui + "/HUD.spriteatlas";
        var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(ap);
        if (!atlas) { atlas = new UnityEngine.U2D.SpriteAtlas(); AssetDatabase.CreateAsset(atlas, ap); }
        var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ui);
        if (!UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(atlas).Contains(folder)) UnityEditor.U2D.SpriteAtlasExtensions.Add(atlas, new Object[] { folder });
        UnityEditor.U2D.SpriteAtlasExtensions.SetPackingSettings(atlas, new UnityEditor.U2D.SpriteAtlasPackingSettings { enableRotation = false, enableTightPacking = false, padding = 4 });
        UnityEditor.U2D.SpriteAtlasExtensions.SetTextureSettings(atlas, new UnityEditor.U2D.SpriteAtlasTextureSettings { generateMipMaps = false, filterMode = FilterMode.Bilinear, sRGB = true });
        EditorUtility.SetDirty(atlas); AssetDatabase.SaveAssets();
        UnityEditor.U2D.SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
        int n = AssetDatabase.FindAssets("t:Sprite", new[] { ui }).Length;
        Debug.Log($"HUD ATLAS: {ap} packs folder {ui} ({n} sprites)");
    }

    // ---------------- previews
    static Camera Scene(Vector3 pos, Vector3 look, float ortho)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var l = new GameObject("Sun").AddComponent<Light>(); l.type = LightType.Directional; l.color = H("FFD3A0"); l.intensity = 1.45f; l.shadows = LightShadows.Soft; l.transform.rotation = Quaternion.Euler(48, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = H("86A6E8"); RenderSettings.ambientEquatorColor = H("A88C9C"); RenderSettings.ambientGroundColor = H("4A3A55");
        var cam = new GameObject("Cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = H("0A4C9E");
        cam.orthographic = true; cam.orthographicSize = ortho; cam.transform.position = pos; cam.transform.LookAt(look); cam.nearClipPlane = .1f; cam.farClipPlane = 200;
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        return cam;
    }
    static GameObject Put(string pf, Vector3 p, float yaw = 0) { var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Pf + pf + ".prefab")); go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0)); return go; }
    static float Top(GameObject g) { var b = g.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }); return b.max.y; }
    static void Previews()
    {
        // towers without base, on wall blocks
        var cam = Scene(new Vector3(-14, 14, -14), new Vector3(0, .6f, 0), 3.4f);
        string[] towers = { "PF_Tower_Gatling_1x1", "PF_Tower_Tesla_1x1", "PF_Tower_Frost_1x1", "PF_Tower_Cannon_1x1", "PF_Tower_Flamer_1x2", "PF_Tower_Mortar_2x2" };
        Vector3[] pos = { new Vector3(-3, 0, 1.5f), new Vector3(-1.5f, 0, 1.5f), new Vector3(0, 0, 1.5f), new Vector3(1.5f, 0, 1.5f), new Vector3(-1.5f, 0, -1.5f), new Vector3(1.5f, 0, -1.5f) };
        for (int i = 0; i < towers.Length; i++)
        {
            int w = towers[i].Contains("2x2") ? 2 : 1, d = towers[i].Contains("1x2") || towers[i].Contains("2x2") ? 2 : 1; float top = 0;
            for (int a = 0; a < w; a++) for (int b = 0; b < d; b++) { var blk = Put("PF_Env_Rock_1x1", pos[i] + new Vector3(a - (w - 1) * .5f, 0, b - (d - 1) * .5f)); top = Top(blk); }
            Put(towers[i], pos[i] + Vector3.up * top, 180);
        }
        StylizedArtIntegration.Capture(cam, PreviewDir + "towers_base_v15.png");

        // coin drop: real simulation stepped at 30 fps, particles baked to meshes per frame (pop / hold / fly+trail / pickup flash)
        float[] ts = { .15f, .4f, .75f, .98f, 1.12f, 1.26f };
        string[] lab = { "pop", "bounce", "hold", "fly + trail", "fly", "pickup flash" };
        cam = Scene(new Vector3(0, 11, -11), Vector3.zero, 3.6f);
        for (int k = 0; k < ts.Length; k++)
        {
            var c0 = new Vector3(-8.75f + k * 3.5f, 0, 0);
            var tile = Put("PF_Env_Rock_1x1", c0); tile.transform.localScale = new Vector3(2.4f, 1, 2.4f); tile.transform.position = c0 + Vector3.down * Top(tile);
            var target = c0 + new Vector3(-1.0f, 3.0f, 1.0f);
            var pill = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(pill.GetComponent<Collider>()); pill.transform.position = target + new Vector3(.45f, .1f, 0);
            pill.transform.localScale = new Vector3(1.1f, .4f, .1f) * (k == 5 ? 1.18f : 1); pill.transform.rotation = cam.transform.rotation;
            var pm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); pm.color = H("FFD678"); pill.GetComponent<Renderer>().sharedMaterial = pm;
            var fxGo = Put("PF_VFX_CoinDrop", Vector3.zero); var fx = fxGo.GetComponent<CoinDropFx>(); fx.cam = null; fx.target = null;
            foreach (var ps in fxGo.GetComponentsInChildren<ParticleSystem>()) { ps.useAutoRandomSeed = false; ps.randomSeed = 3; }
            UnityEngine.Random.InitState(5);
            fx.Play(c0, 20, target, null, false);
            const float dt = 1 / 30f;
            for (float t = 0; t < ts[k]; t += dt) { fx.Tick(dt); foreach (var ps in fxGo.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(dt, false, false, false); }
            Bake(fxGo, cam);
        }
        StylizedArtIntegration.Capture(cam, PreviewDir + "coin_drop_v15.png");

        // enemy ground: several enemies mid-walk, global dust system with real Emit along their paths, blobs lifted
        cam = Scene(new Vector3(-9, 11, -9), new Vector3(0, .8f, 0), 3.4f);
        for (int x = -5; x <= 5; x++) for (int z = -4; z <= 4; z++)
        {
            var t = Put("PF_Env_Rock_1x1", new Vector3(x, 0, z)); float h = Top(t);
            t.transform.position = new Vector3(x, .8f - h + ((x * 7 + z * 13) % 3) * .03f, z);   // tile tops 0.80 + jitter up to 0.06
        }
        var sysGo = Put("PF_VFX_EnemyGroundSystem", Vector3.zero); var sys = sysGo.GetComponent<EnemyGroundFxSystem>();
        var dps = sys.dust; dps.useAutoRandomSeed = false; dps.randomSeed = 9;
        string[] en = { "PF_Enemy_Drifter", "PF_Enemy_Bulwark", "PF_Enemy_Skimmer", "PF_Enemy_Splitter", "PF_Enemy_Flyer" };
        var dir = new Vector3(1, 0, 1).normalized; var walkers = new System.Collections.Generic.List<(Vector3 end, EnemyGroundFx g)>();
        for (int i = 0; i < en.Length; i++)
        {
            var end = new Vector3(-2.6f + i * 1.5f, .8f, -1.2f + (i % 2) * 1.4f);
            var e = Put(en[i], end, 45);
            var gf = e.GetComponentInChildren<EnemyGroundFx>(); if (!gf) continue;
            gf.blob.position = end + Vector3.up * (gf.groundY + gf.lift);
            if (!gf.flying) walkers.Add((end, gf));
        }
        for (int k = 6; k >= 1; k--)    // footprints left behind, oldest first, 0.3 s apart
        {
            foreach (var (end, gf) in walkers)
            {
                var p = end - dir * (k * gf.stepDistance) + Vector3.up * (gf.lift - .02f) + Vector3.Cross(dir, Vector3.up) * ((k % 2) * .16f - .08f);
                dps.Emit(new PSS.EmitParams { position = p, startSize = .26f, rotation = k * 50 }, 1);
            }
            dps.Simulate(.3f, false, false, false);
        }
        Bake(sysGo, cam);
        StylizedArtIntegration.Capture(cam, PreviewDir + "enemy_trail_v15.png");

        // placement: ghost tower + footprint frame (per-cell valid/invalid, rotated) + real wall blocks highlighted (MPB) + rune glyph + buff icons + resonance aura
        cam = Scene(new Vector3(-9, 12, -9), new Vector3(0, .8f, 0), 4.2f);
        RuneInlay.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(RuneAtlasPath);
        var walls = new System.Collections.Generic.Dictionary<Vector2Int, Renderer>(); float wtop = 0;
        for (int x = -4; x <= 4; x++) for (int z = -3; z <= 3; z++)
        {
            var t = Put("PF_Env_Rock_1x1", new Vector3(x, 0, z)); float h = Top(t); t.transform.position = new Vector3(x, .8f + .45f - h, z); wtop = .8f + .45f;
            walls[new Vector2Int(x, z)] = t.GetComponentInChildren<Renderer>();
        }
        var ghostPf = "PF_UI_PlaceGhost_Tower";
        (string tower, Vector3 at, Vector2Int size, int rot, bool[] ok)[] cases = {
            ("PF_Tower_Gatling_1x1", new Vector3(-3, 0, 1), new Vector2Int(1, 1), 0, new[] { true }),
            ("PF_Tower_Flamer_1x2",  new Vector3(-.5f, 0, 1), new Vector2Int(1, 2), 1, new[] { true, false }),
            ("PF_Tower_Mortar_2x2",  new Vector3(2.5f, 0, .5f), new Vector2Int(2, 2), 0, new[] { true, true, true, false }) };
        foreach (var cs in cases)
        {
            var gpos = cs.at + Vector3.up * (wtop + .0f);
            var gg = Put(ghostPf, gpos, cs.rot * 90); var pg = gg.GetComponent<PlacementGhost>();
            gg.transform.rotation = Quaternion.identity; pg.SetFootprint(cs.size, cs.rot);
            for (int k = 0; k < cs.ok.Length; k++) pg.SetCellValid(k, cs.ok[k]);
            var tw = Put(cs.tower, gpos, cs.rot * 90); bool allOk = cs.ok.All(o => o);
            var gmat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/UI/" + (allOk ? "M_UI_Ghost_Valid.mat" : "M_UI_Ghost_Invalid.mat"));
            foreach (var r in tw.GetComponentsInChildren<Renderer>()) { var arr = r.sharedMaterials; for (int i = 0; i < arr.Length; i++) arr[i] = gmat; r.sharedMaterials = arr; }
            // wall highlight under footprint (per cell colour)
            var rotq = Quaternion.Euler(0, cs.rot * 90, 0); var cells = new System.Collections.Generic.List<Vector2Int>();
            for (int y = 0; y < cs.size.y; y++) for (int x = 0; x < cs.size.x; x++)
            {
                var lp = rotq * new Vector3(x - (cs.size.x - 1) * .5f, 0, y - (cs.size.y - 1) * .5f); var c = new Vector2Int(Mathf.RoundToInt(cs.at.x + lp.x), Mathf.RoundToInt(cs.at.z + lp.z));
                cells.Add(c); if (walls.TryGetValue(c, out var wr)) WallHighlight.Register(c, wr);
            }
            WallHighlight.SetCells(cells.ToArray(), cs.ok, true);
        }
        // runes on wall tops + a placed tower with buff icons and resonance aura
        RuneInlay.Set(walls[new Vector2Int(-3, -2)], RuneId.Blade); RuneInlay.Set(walls[new Vector2Int(-2, -2)], RuneId.Swift); RuneInlay.Set(walls[new Vector2Int(-1, -2)], RuneId.Sight);
        RuneInlay.Set(walls[new Vector2Int(0, -2)], RuneId.Frost); RuneInlay.Set(walls[new Vector2Int(1, -2)], RuneId.Bounty); RuneInlay.Set(walls[new Vector2Int(3, -2)], RuneId.Resonance);
        var placed = Put("PF_Tower_Tesla_1x1", new Vector3(-2, wtop, -2), 180);
        var bi = Put("PF_UI_TowerBuffIcons", placed.transform.position).GetComponent<TowerBuffIcons>();
        bi.Set(new[] { RuneId.Swift }, new[] { new Vector3(-2, wtop, -2) });
        foreach (var s0 in bi.slots) if (s0 && s0.gameObject.activeSelf) s0.transform.rotation = cam.transform.rotation;
        Put("PF_UI_ResonanceAura", new Vector3(3, wtop, -2));
        StylizedArtIntegration.Capture(cam, PreviewDir + "ghost_footprint_v15.png");
        WallHighlight.ClearRegistry();
    }

    /// Batch captures don't draw edit-mode particle systems reliably: bake each PS (+trails) into a static mesh with the same material.
    static void Bake(GameObject root, Camera cam)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
        {
            var r = ps.GetComponent<ParticleSystemRenderer>(); if (ps.particleCount == 0) continue;
            var m = new Mesh(); r.BakeMesh(m, cam, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
            var g = new GameObject(ps.name + "_baked"); g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = r.sharedMaterial;
            if (ps.trails.enabled && r.trailMaterial)
            {
                var tm = new Mesh(); r.BakeTrailsMesh(tm, cam, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                var tg = new GameObject(ps.name + "_trail"); tg.AddComponent<MeshFilter>().sharedMesh = tm; tg.AddComponent<MeshRenderer>().sharedMaterial = r.trailMaterial;
            }
            r.enabled = false;
        }
    }

    public static void Checks()
    {
        bool ok = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_CoinDrop.prefab") && AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_EnemyGround.prefab")
                  && AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_EnemyGroundSystem.prefab")
                  && !AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_EnemyGround.prefab").GetComponentInChildren<ParticleSystem>()
                  && AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(StylizedArtIntegration.ArtDir + "UI/HUD.spriteatlas")
                  && Directory.GetFiles(Pf, "PF_Enemy_*.prefab").All(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponentInChildren<EnemyGroundFx>());
        Debug.Log(ok ? "V14 FX PASS" : "V14 FX CHECK FAILED");
        if (!ok) throw new System.Exception("V14 FX CHECK FAILED");
    }
}
