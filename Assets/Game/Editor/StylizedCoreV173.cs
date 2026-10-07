using System.Linq;
using StoneSignal;
using StoneSignal.VFX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// v17.3 art: core enclosure. Builds (BatchImport, after the portal):
///   M_Core_Enclosure        StoneSignal/ToonLit, shared palette texture, env settings, _VColorEmission (vertex R = crack/rune glow)
///   PF_VFX_CoreSmoke        pooled ParticleSystem, max 8 (Broken + Critical)
///   PF_VFX_CoreSparks       pooled ParticleSystem, max 8 (Critical loop + 3 per PlayHit)   -> 16 particles total
///   PF_Core_Enclosure       MeshFilter/MeshRenderer (SM_Core_Enclosure_Intact|Cracked|Broken, ONE merged mesh each = 1 DC)
///                           + CoreDamageFx (meshes, smoke, sparks, thresholds 0.70 / 0.40 / 0.15) + nested smoke/sparks
/// and points ArtCatalog.coreEnclosureFx (+ the legacy coreEnclosure* fields) at them, so BatchImport alone is enough.
/// GridView instantiates PF_Core_Enclosure under the core at ArtCatalog.tileTop (enclosure pivot = tile top) and fills
/// CoreDamageFx.coreRenderers with the core prop renderers. Source: ArtSource/Stylized/Core/core_enclosure_v17_3.py.
public static class StylizedCoreV173
{
    const string CoreDir = StylizedArtIntegration.ArtDir + "Environment/Core/";
    const string FxDir = StylizedArtIntegration.ArtDir + "FX/Core/";
    const string MatDir = "Assets/Game/Materials/Stylized/";
    const string Pf = StylizedArtIntegration.PrefabDir;
    const string ArtCatalogPath = "Assets/Game/Settings/ArtCatalog.asset";
    public const int SmokeMax = 8, SparksMax = 8;
    static readonly string[] States = { "Intact", "Cracked", "Broken" };
    // tunables (defaults applied on every BatchImport)
    const float VColorEmission = 2.2f;                       // crack glow strength (x palette SlotFire x vertex R; HDR -> bloom)
    static Color H(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    public static Mesh LoadMesh(string state)
    {
        string p = CoreDir + "SM_Core_Enclosure_" + state + ".fbx";
        var meshes = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().ToArray();
        if (meshes.Length != 1) throw new System.Exception($"CORE v17.3: {p} must contain exactly ONE merged mesh (found {meshes.Length}) - re-export with core_enclosure_v17_3.py");
        return meshes[0];
    }

    [MenuItem("StoneSignal/Stylized art/Build core enclosure (v17.3)")]
    public static void Build()
    {
        foreach (var s in States) AssetDatabase.ImportAsset(CoreDir + "SM_Core_Enclosure_" + s + ".fbx", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(FxDir + "T_FX_CoreSmoke_2x2.png"); AssetDatabase.ImportAsset(FxDir + "T_FX_CoreSpark.png");
        var meshes = States.Select(LoadMesh).ToArray();

        // ---- enclosure material: same toon/palette approach as the other stylized props (M_Env_Palette) + vertex-R glow
        var env = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_Env_Palette.mat");
        var toon = Shader.Find("StoneSignal/ToonLit"); if (!toon) throw new System.Exception("CORE v17.3: StoneSignal/ToonLit missing");
        string mp = MatDir + "M_Core_Enclosure.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp); if (!mat) { mat = new Material(toon); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = toon;
        if (env) mat.CopyPropertiesFromMaterial(env);
        else { mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StylizedArtIntegration.ArtDir + "Textures/T_Env_Palette_D.png")); mat.SetFloat("_ShadowSmooth", .02f); mat.SetFloat("_RimIntensity", .25f); }
        if (!mat.HasProperty("_VColorEmission")) throw new System.Exception("CORE v17.3: ToonLit has no _VColorEmission (StoneSignalToonCore.hlsl / SS_ToonLit.shader v17.3 not installed)");
        mat.SetFloat("_VColorEmission", VColorEmission); mat.SetFloat("_WindStrength", 0);   // vertex R = glow here, never wind
        mat.SetFloat("_LayerTint", 0); mat.SetFloat("_Wobble", 0); mat.SetFloat("_BaseAOHeight", .22f);
        mat.enableInstancing = false; EditorUtility.SetDirty(mat);

        // ---- particle materials (URP Particles/Unlit; smoke alpha-blended, sparks additive)
        var smokeMat = PartMat("M_VFX_CoreSmoke", FxDir + "T_FX_CoreSmoke_2x2.png", false, Color.white);
        var sparkMat = PartMat("M_VFX_CoreSpark", FxDir + "T_FX_CoreSpark.png", true, new Color(2.2f, 1.5f, 1.0f, 1f));

        // ---- PF_VFX_CoreSmoke (max 8): slow warm-grey puffs rising out of the far wall breach
        var smokeGo = new GameObject("PF_VFX_CoreSmoke");
        {
            var ps = NewPS(smokeGo, smokeMat, SmokeMax, ParticleSystemRenderMode.Billboard);
            var m = ps.main; m.duration = 2; m.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(.12f, .28f);
            m.startSize = new ParticleSystem.MinMaxCurve(.42f, .66f); m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(.24f, .21f, .20f, .78f), new Color(.38f, .33f, .30f, .66f));
            var em = ps.emission; em.enabled = true; em.rateOverTime = 3.2f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .45f; sh.rotation = new Vector3(-90, 0, 0); sh.position = new Vector3(0, .3f, .45f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(.06f); vel.y = new ParticleSystem.MinMaxCurve(.38f); vel.z = new ParticleSystem.MinMaxCurve(.02f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .12f; noise.frequency = .45f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, .6f, 1, 1.5f));
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            Fade(ps, new[] { (0f, 0f), (.15f, 1f), (.55f, .8f), (1f, 0f) });
            var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.mode = ParticleSystemAnimationMode.Grid; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, .99f);   // random fixed puff per particle
        }
        var smokePf = PrefabUtility.SaveAsPrefabAsset(smokeGo, Pf + "PF_VFX_CoreSmoke.prefab"); Object.DestroyImmediate(smokeGo);

        // ---- PF_VFX_CoreSparks (max 8): ember sparks spitting up around the crystal base (Critical; 3 per hit otherwise)
        var sparkGo = new GameObject("PF_VFX_CoreSparks");
        {
            var ps = NewPS(sparkGo, sparkMat, SparksMax, ParticleSystemRenderMode.Stretch);
            var m = ps.main; m.duration = 1; m.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .8f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            m.startSize = new ParticleSystem.MinMaxCurve(.06f, .11f); m.gravityModifier = .9f;
            m.startColor = new ParticleSystem.MinMaxGradient(H("FFD27A"), H("FF7A1F"));
            var em = ps.emission; em.enabled = true; em.rateOverTime = 5f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = .35f; sh.rotation = new Vector3(-90, 0, 0); sh.position = new Vector3(0, .5f, 0);
            Fade(ps, new[] { (0f, 1f), (.6f, 1f), (1f, 0f) });
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .4f));
            var r = sparkGo.GetComponent<ParticleSystemRenderer>(); r.velocityScale = .05f; r.lengthScale = 1.4f;
        }
        var sparkPf = PrefabUtility.SaveAsPrefabAsset(sparkGo, Pf + "PF_VFX_CoreSparks.prefab"); Object.DestroyImmediate(sparkGo);

        // ---- PF_Core_Enclosure
        var root = new GameObject("PF_Core_Enclosure", typeof(MeshFilter), typeof(MeshRenderer));
        var mf = root.GetComponent<MeshFilter>(); mf.sharedMesh = meshes[0];
        var mr = root.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
        var smoke = (GameObject)PrefabUtility.InstantiatePrefab(smokePf, root.transform); smoke.name = "Smoke";
        var sparks = (GameObject)PrefabUtility.InstantiatePrefab(sparkPf, root.transform); sparks.name = "Sparks";
        var fx = root.AddComponent<CoreDamageFx>();
        fx.crackedBelow = .70f; fx.brokenBelow = .40f; fx.criticalBelow = .15f;
        fx.enclosure = mf; fx.intactMesh = meshes[0]; fx.crackedMesh = meshes[1]; fx.brokenMesh = meshes[2];
        fx.smoke = smoke.GetComponent<ParticleSystem>(); fx.sparks = sparks.GetComponent<ParticleSystem>();
        fx.coreRenderers = new Renderer[0];   // filled at runtime by GridView (core prop renderers)
        var pf = PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_Core_Enclosure.prefab"); Object.DestroyImmediate(root);

        // ---- catalog (BatchWire does the same; set here so BatchImport alone is enough)
        var art = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalogPath);
        if (art != null)
        {
            art.coreEnclosureFx = pf; art.coreEnclosureIntact = meshes[0]; art.coreEnclosureCracked = meshes[1]; art.coreEnclosureBroken = meshes[2]; art.coreEnclosureMaterial = mat;
            EditorUtility.SetDirty(art);
        }
        else Debug.LogWarning("CORE v17.3: ArtCatalog not found at " + ArtCatalogPath + " - run BatchWire");
        AssetDatabase.SaveAssets();
        Debug.Log("CORE v17.3 built: PF_Core_Enclosure (" + string.Join(", ", meshes.Select(x => x.name + " tris=" + (x.GetIndexCount(0) / 3) + " sub=" + x.subMeshCount))
                  + "), M_Core_Enclosure _VColorEmission " + VColorEmission + ", PF_VFX_CoreSmoke max " + SmokeMax + " + PF_VFX_CoreSparks max " + SparksMax + ", thresholds 0.70/0.40/0.15");
    }

    static Material PartMat(string name, string tex, bool additive, Color tint)
    {
        string p = MatDir + name + ".mat"; var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex)); m.SetColor("_BaseColor", tint);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0); m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = additive ? 3100 : 3050; m.enableInstancing = true;
        EditorUtility.SetDirty(m); return m;
    }
    static ParticleSystem NewPS(GameObject go, Material mat, int max, ParticleSystemRenderMode mode)
    {
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = true; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = max;
        m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.stopAction = ParticleSystemStopAction.None;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = mode; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance;
        return ps;
    }
    static void Fade(ParticleSystem ps, (float t, float a)[] keys)
    {
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, keys.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
        col.color = g;
    }

    public static void Checks()
    {
        var errs = new System.Collections.Generic.List<string>();
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "PF_Core_Enclosure.prefab");
        if (!pf) errs.Add("PF_Core_Enclosure missing");
        else
        {
            var fx = pf.GetComponent<CoreDamageFx>();
            if (!fx) errs.Add("CoreDamageFx missing");
            else
            {
                if (!fx.enclosure || !fx.intactMesh || !fx.crackedMesh || !fx.brokenMesh) errs.Add("enclosure meshes not wired");
                else if (fx.intactMesh == fx.crackedMesh || fx.crackedMesh == fx.brokenMesh) errs.Add("state meshes are not distinct");
                foreach (var m in new[] { fx.intactMesh, fx.crackedMesh, fx.brokenMesh })
                    if (m && (m.subMeshCount != 1 || !m.HasVertexAttribute(VertexAttribute.Color) || !m.HasVertexAttribute(VertexAttribute.TexCoord0))) errs.Add(m.name + ": needs 1 submesh + vertex colour + palette UV");
                if (!fx.smoke || !fx.sparks) errs.Add("smoke/sparks not wired");
                else if (fx.smoke.main.maxParticles + fx.sparks.main.maxParticles > 16) errs.Add("core particles > 16");
                if (!Mathf.Approximately(fx.crackedBelow, .70f) || !Mathf.Approximately(fx.brokenBelow, .40f) || !Mathf.Approximately(fx.criticalBelow, .15f)) errs.Add("thresholds not 0.70/0.40/0.15");
            }
            var mr = pf.GetComponent<MeshRenderer>();
            if (!mr || !mr.sharedMaterial || mr.sharedMaterial.shader.name != "StoneSignal/ToonLit" || mr.sharedMaterials.Length != 1) errs.Add("enclosure must use one StoneSignal/ToonLit material");
        }
        if (errs.Count > 0) throw new System.Exception("CORE v17.3 FAIL\n" + string.Join("\n", errs));
        Debug.Log("CORE v17.3 PASS (1 merged mesh per state, 1 material, smoke " + SmokeMax + " + sparks " + SparksMax + " particles)");
    }
}
