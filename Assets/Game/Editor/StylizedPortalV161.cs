using System.Linq;
using UnityEditor;
using UnityEngine;
using StoneSignal.VFX;

/// v16.1/v16.2: builds PF_VFX_SpawnPortal (art-led v16.2) and PF_VFX_StatusFx. Called from StylizedArtIntegration.BatchImport.
public static class StylizedPortalV161
{
    const string Pf = "Assets/Game/Prefabs/Stylized/", Mat = "Assets/Game/Materials/Stylized/";
    const string Tex = "Assets/Game/Art/Stylized/FX/Portal/";
    static Texture2D T(string n, bool srgb = true)
    {
        var p = Tex + n + ".png"; var ti = (TextureImporter)AssetImporter.GetAtPath(p);
        if (ti) { ti.sRGBTexture = srgb; ti.alphaIsTransparency = srgb; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }
    /// v16.2 art-led portal: painted decal/rune/crack textures + Blender runestones (SM_Portal_Runestones.fbx, 1 mesh/1 material) + pooled rubble.
    public static void Build()
    {
        // v16.1: GPU instancing on every toon material so walls carrying a rune/highlight MPB still batch (instanced buffer in StoneSignalToonCore)
        int inst = 0; foreach (var mg in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Game/Materials/Stylized" })) { var mm = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(mg)); if (mm && mm.shader && mm.shader.name.Contains("Toon") && !mm.enableInstancing) { mm.enableInstancing = true; EditorUtility.SetDirty(mm); inst++; } }
        Debug.Log("TOON INSTANCING enabled on " + inst + " materials");
        var sh = Shader.Find("StoneSignal/SS_SpawnPortal"); if (!sh) throw new System.Exception("SS_SpawnPortal shader missing");
        var m = LoadOrCreate(Mat + "M_VFX_SpawnPortal.mat", sh); m.shader = sh; m.enableInstancing = true; m.renderQueue = 2980;
        m.SetTexture("_ScorchTex", T("T_Portal_Scorch")); m.SetTexture("_CrackTex", T("T_Portal_CrackMask", false)); m.SetTexture("_RuneTex", T("T_Portal_RuneCircle")); EditorUtility.SetDirty(m);
        var psh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material PMat(string n, Texture2D tex, bool add)
        {
            var pm = LoadOrCreate(Mat + n + ".mat", psh); pm.shader = psh; pm.SetFloat("_Surface", 1); pm.SetFloat("_Blend", add ? 2 : 0);
            pm.SetOverrideTag("RenderType", "Transparent"); pm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            pm.SetInt("_DstBlend", (int)(add ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha)); pm.SetInt("_ZWrite", 0);
            pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); if (add) pm.EnableKeyword("_BLENDMODE_ADD"); if (tex) pm.SetTexture("_BaseMap", tex); pm.renderQueue = 3000; EditorUtility.SetDirty(pm); return pm;
        }
        var flareMat = PMat("M_VFX_PortalFlare", T("T_Portal_FlareSheet_4x4"), true);
        var dustMat = PMat("M_VFX_PortalDust", null, false);
        PMat("M_VFX_PortalEmber", null, true);   // kept for PF_VFX_StatusFx

        var stonesMesh = AssetDatabase.LoadAllAssetsAtPath(StylizedArtIntegration.ArtDir + "FX/Portal/SM_Portal_Runestones.fbx").OfType<Mesh>().FirstOrDefault();
        var rubbleMesh = AssetDatabase.LoadAllAssetsAtPath(StylizedArtIntegration.ArtDir + "FX/Portal/SM_Portal_Rubble.fbx").OfType<Mesh>().FirstOrDefault();
        var rockPf = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_Env_Rock_Small.prefab");
        var toon = rockPf ? rockPf.GetComponentInChildren<Renderer>().sharedMaterial : null;   // existing toon stone material (shared, SRP batched)
        if (!stonesMesh || !rubbleMesh || !toon) throw new System.Exception("PORTAL FAIL: runestone/rubble mesh or toon stone material missing");

        var root = new GameObject("PF_VFX_SpawnPortal"); var sp = root.AddComponent<SpawnPortal>();
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Ground"; Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(root.transform, false); q.transform.localPosition = Vector3.up * .02f; q.transform.localRotation = Quaternion.Euler(90, 0, 0); q.transform.localScale = Vector3.one * 2.6f;
        var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        var st = new GameObject("Runestones"); st.transform.SetParent(root.transform, false); st.transform.localScale = Vector3.one * .72f;
        st.AddComponent<MeshFilter>().sharedMesh = stonesMesh; var sr = st.AddComponent<MeshRenderer>(); sr.sharedMaterial = toon; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var rubRoot = new GameObject("Rubble"); rubRoot.transform.SetParent(root.transform, false); var rub = new Transform[8];
        for (int i = 0; i < 8; i++)
        {
            var c = new GameObject("Chunk" + i); c.transform.SetParent(rubRoot.transform, false); c.AddComponent<MeshFilter>().sharedMesh = rubbleMesh;
            var cr = c.AddComponent<MeshRenderer>(); cr.sharedMaterial = toon; cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; c.SetActive(false); rub[i] = c.transform;
        }
        ParticleSystem Burst(string n, Material mat, int max, float life, float size, float speed, bool sheet)
        {
            var go = new GameObject(n); go.transform.SetParent(root.transform, false); go.transform.localPosition = Vector3.up * (sheet ? .7f : .1f);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var mn = ps.main; mn.playOnAwake = false; mn.loop = false; mn.maxParticles = max; mn.startLifetime = life; mn.startSize = new ParticleSystem.MinMaxCurve(size * .7f, size);
            mn.startSpeed = speed; mn.simulationSpace = ParticleSystemSimulationSpace.World; mn.startColor = sheet ? Color.white : new Color(.85f, .78f, .66f, .8f);
            var e = ps.emission; e.enabled = false; var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Circle; shp.radius = sheet ? .01f : .55f; shp.rotation = new Vector3(-90, 0, 0);
            if (sheet) { var ts = ps.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 4; ts.numTilesY = 4; ts.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, 1)); }
            else { var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.8f, 0), new GradientAlphaKey(0, 1) }); col.color = g;
                   var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 1.6f)); }
            var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = mat; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return ps;
        }
        sp.ground = r; sp.rubble = rub; sp.flare = Burst("Flare", flareMat, 1, .45f, 2.2f, 0, true); sp.dust = Burst("Dust", dustMat, 8, .7f, .45f, .8f, false);
        PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_VFX_SpawnPortal.prefab"); Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("PORTAL v16.2 built: PF_VFX_SpawnPortal (ground quad + runestones = 2 DC idle), rubble x8 pooled, dust<=8, flare 4x4");
    }
    public static void Checks()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_SpawnPortal.prefab");
        if (!p) throw new System.Exception("PORTAL FAIL: prefab missing");
        var sp = p.GetComponent<SpawnPortal>();
        int idleDC = p.GetComponentsInChildren<MeshRenderer>(false).Where(x => x.gameObject.activeSelf && x.transform.parent.gameObject.activeSelf).Sum(x => x.sharedMaterials.Length);
        if (!sp || !sp.ground || !sp.flare || !sp.dust || sp.rubble == null || sp.rubble.Length > 8 || idleDC > 2 || !sp.ground.sharedMaterial.enableInstancing) throw new System.Exception("PORTAL FAIL: structure idleDC=" + idleDC);
        Debug.Log("PORTAL v16.2 PASS (idle DC " + idleDC + ", rubble " + sp.rubble.Length + ")");
    }
    // ---- v16.2 enemy death FX: PF_VFX_EnemyDeath (EnemyDeathFx + shared 4x4 puff particle system, skull sprite)
    public static void BuildDeath()
    {
        var psh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Stylized/FX/Death/T_FX_DeathPuff_4x4.png");
        var pm = LoadOrCreate(Mat + "M_VFX_DeathPuff.mat", psh); pm.shader = psh; pm.SetFloat("_Surface", 1); pm.SetFloat("_Blend", 0);
        pm.SetOverrideTag("RenderType", "Transparent"); pm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); pm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); pm.SetInt("_ZWrite", 0);
        pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); pm.SetTexture("_BaseMap", tex); pm.renderQueue = 3000; EditorUtility.SetDirty(pm);
        var root = new GameObject("PF_VFX_EnemyDeath"); var fx = root.AddComponent<EnemyDeathFx>();
        var go = new GameObject("Puff"); go.transform.SetParent(root.transform, false); var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.playOnAwake = false; m.loop = false; m.maxParticles = 8; m.startLifetime = .55f; m.startSpeed = 0; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
        var e = ps.emission; e.enabled = false; var sh = ps.shape; sh.enabled = false;
        var ts = ps.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 4; ts.numTilesY = 4; ts.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, 1));
        var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = pm; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fx.puff = ps; fx.skullSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StylizedArtIntegration.ArtDir + "UI/ui_fx_skull.png");
        PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_VFX_EnemyDeath.prefab"); Object.DestroyImmediate(root); AssetDatabase.SaveAssets();
        Debug.Log("DEATH FX v16.2 built: PF_VFX_EnemyDeath (puff 4x4 <=8 particles + pooled skull sprite, 2 DC)");
    }
    public static void DeathChecks()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_EnemyDeath.prefab"); var fx = p ? p.GetComponent<EnemyDeathFx>() : null;
        if (!fx || !fx.puff || !fx.skullSprite || fx.puff.main.maxParticles > 8) throw new System.Exception("DEATH FX FAIL");
        Debug.Log("DEATH FX v16.2 PASS");
    }
    // ---- v16.1 enemy status FX: PF_VFX_StatusFx (EnemyStatusFxSystem + 5 world-space systems, 48 cap each, 1 shared additive material)
    public static void BuildStatus()
    {
        var em = AssetDatabase.LoadAssetAtPath<Material>(Mat + "M_VFX_PortalEmber.mat");
        var root = new GameObject("PF_VFX_StatusFx"); var sys = root.AddComponent<EnemyStatusFxSystem>();
        string[] names = { "Poison", "Slow", "Burn", "Shock", "Frost" };
        Color[] a = { new Color(.55f, 1f, .4f), new Color(.75f, .5f, 1f), new Color(1f, .75f, .3f), new Color(1f, 1f, .7f), new Color(.75f, .93f, 1f) };
        Color[] b = { new Color(.25f, .7f, .2f), new Color(.45f, .25f, .85f), new Color(1f, .35f, .1f), new Color(.6f, .8f, 1f), new Color(.4f, .7f, 1f) };
        float[] life = { .9f, 1.1f, .45f, .09f, .7f }, speed = { -.35f, .0f, .9f, 0f, .15f }, size = { .09f, .14f, .1f, .5f, .06f };
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject("FX_" + names[i]); go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = true; m.playOnAwake = true; m.maxParticles = EnemyStatusFxSystem.CapPerStatus; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life[i] * .7f, life[i]); m.startSize = new ParticleSystem.MinMaxCurve(size[i] * .6f, size[i]);
            m.startSpeed = 0; m.gravityModifier = i == 0 ? .25f : 0; m.startColor = new ParticleSystem.MinMaxGradient(a[i], b[i]);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
            var e = ps.emission; e.enabled = false; var sh = ps.shape; sh.enabled = false;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-.15f, .15f); v.z = new ParticleSystem.MinMaxCurve(-.15f, .15f); v.y = new ParticleSystem.MinMaxCurve(speed[i] * .6f, speed[i] + .01f);
            if (i == 1) { v.orbitalY = new ParticleSystem.MinMaxCurve(3f, 4f); v.radial = new ParticleSystem.MinMaxCurve(.15f, .2f); }  // slow: swirl at feet
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) }); col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, i == 2 ? AnimationCurve.Linear(0, 1, 1, .2f) : AnimationCurve.EaseInOut(0, .6f, 1, 1));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = em; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (i == 3) { r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 3; r.velocityScale = 0; m.startSize3D = false; }   // shock: flickering arc streaks
            sys.systems[i] = ps;
        }
        string[] icon = { "ui_status_poison", "ui_status_slow", "ui_status_burn", "ui_status_shock", "ui_status_frost" };
        for (int i = 0; i < 5; i++) sys.icons[i] = AssetDatabase.LoadAssetAtPath<Sprite>(StylizedArtIntegration.ArtDir + "UI/" + icon[i] + ".png");
        PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_VFX_StatusFx.prefab"); Object.DestroyImmediate(root);
        foreach (var guid in AssetDatabase.FindAssets("PF_Enemy_ t:Prefab", new[] { "Assets/Game/Prefabs/Stylized" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid); var pr = PrefabUtility.LoadPrefabContents(path);
            var fx = pr.GetComponent<EnemyStatusFx>() ?? pr.AddComponent<EnemyStatusFx>();
            var bnds = new Bounds(pr.transform.position, Vector3.zero); foreach (var rr in pr.GetComponentsInChildren<Renderer>()) if (!(rr is ParticleSystemRenderer)) bnds.Encapsulate(rr.bounds);
            fx.headHeight = Mathf.Max(.6f, bnds.max.y - pr.transform.position.y + .25f); fx.radius = Mathf.Clamp(bnds.extents.x * .8f, .2f, .8f);
            PrefabUtility.SaveAsPrefabAsset(pr, path); PrefabUtility.UnloadPrefabContents(pr);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("STATUS FX v16.1 built: PF_VFX_StatusFx (5 systems x 48), EnemyStatusFx on PF_Enemy_*");
    }
    public static void StatusChecks()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_VFX_StatusFx.prefab"); var s = p ? p.GetComponent<EnemyStatusFxSystem>() : null;
        if (!s || s.systems.Length != 5 || System.Array.Exists(s.systems, x => !x || x.main.maxParticles > 48) || System.Array.Exists(s.icons, x => !x)) throw new System.Exception("STATUS FX FAIL sys=" + (s ? s.systems.Length + " nullSys=" + System.Array.FindAll(s.systems, x => !x).Length + " max=" + string.Join(",", System.Array.ConvertAll(s.systems, x => x ? x.main.maxParticles : -1)) + " nullIcons=" + System.Array.FindAll(s.icons, x => !x).Length : "none"));
        Debug.Log("STATUS FX v16.1 PASS");
    }
    static Material LoadOrCreate(string path, Shader s)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) return m;
        m = new Material(s); AssetDatabase.CreateAsset(m, path); return m;
    }
}
