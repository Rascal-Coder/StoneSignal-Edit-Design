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
        BuildCoinDrop(); BuildEnemyGround(); BuildAtlas(); AssetDatabase.SaveAssets();
        Previews();
    }

    static void BuildCoinDrop()
    {
        var root = new GameObject("PF_VFX_CoinDrop"); var fx = root.AddComponent<CoinDropFx>();
        var coins = EmitPS(root.transform, "Coins", M("M_FX_Alpha_Coin", false, 6), 10, .34f, Color.white, 48);
        var r = coins.GetComponent<ParticleSystemRenderer>(); r.sortingFudge = -10;
        var tr = coins.trails; tr.enabled = true; tr.mode = ParticleSystemTrailMode.PerParticle; tr.lifetime = .18f; tr.minVertexDistance = .08f;
        tr.widthOverTrail = new PSS.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 0)); tr.dieWithParticles = true; tr.inheritParticleColor = false;
        var tg = new Gradient(); tg.SetKeys(new[] { new GradientColorKey(H("FFE27A"), 0), new GradientColorKey(H("FFB020"), 1) }, new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(0, 1) });
        tr.colorOverTrail = tg; r.trailMaterial = M("M_FX_Add_Trail", true, 7);
        var sp = EmitPS(root.transform, "Sparkle", M("M_FX_Add_Dot", true, 0, .7f), .45f, .12f, H("FFE07A"), 40);
        var spm = sp.main; spm.startSpeed = new PSS.MinMaxCurve(.8f, 2.2f); spm.startSize = new PSS.MinMaxCurve(.06f, .14f); spm.gravityModifier = .3f;
        var sh = sp.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .15f; FadeOut(sp);
        var fl = EmitPS(root.transform, "PickupFlash", M("M_FX_Add_Flash", true, 4), .25f, .9f, H("FFF2B0"), 8);
        var so = fl.sizeOverLifetime; so.enabled = true; so.size = new PSS.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 1.6f)); FadeOut(fl);
        fx.coins = coins; fx.sparkle = sp; fx.flash = fl;
        PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_VFX_CoinDrop.prefab"); Object.DestroyImmediate(root);
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

    static void BuildEnemyGround()
    {
        // global dust system (one PS, Emit only, cap 64)
        var sysGo = new GameObject("PF_VFX_EnemyGroundSystem"); var sys = sysGo.AddComponent<EnemyGroundFxSystem>();
        var dust = EmitPS(sysGo.transform, "Dust", M("M_FX_Alpha_Smoke", false, 3), 2f, .22f, new Color(.9f, .78f, .66f, .45f), EnemyGroundFxSystem.MaxFootprints);
        dust.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        var so = dust.sizeOverLifetime; so.enabled = true; so.size = new PSS.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 1.3f));
        FadeOut(dust, .45f); sys.dust = dust;
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

    // ---------------- HUD sprite atlas
    static void BuildAtlas()
    {
        const string ui = StylizedArtIntegration.ArtDir + "UI";
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ui }))
        {
            var tp = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled) { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
        }
        string ap = ui + "/HUD.spriteatlas";
        var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(ap);
        if (!atlas) { atlas = new UnityEngine.U2D.SpriteAtlas(); AssetDatabase.CreateAsset(atlas, ap); }
        UnityEditor.U2D.SpriteAtlasExtensions.Remove(atlas, UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(atlas));
        UnityEditor.U2D.SpriteAtlasExtensions.Add(atlas, new Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(ui) });
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
        StylizedArtIntegration.Capture(cam, PreviewDir + "towers_nobase_v15.png");

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
            var fxGo = Put("PF_VFX_CoinDrop", Vector3.zero); var fx = fxGo.GetComponent<CoinDropFx>(); fx.cam = null;
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
