using System.Linq;
using StoneSignal.VFX;
using UnityEditor;
using UnityEngine;

/// Builds placement-feedback + path-flow art (visual only). Called from StylizedArtIntegration.BatchImport.
public static class StylizedPlacementFX
{
    const string Mat = "Assets/Game/Materials/Stylized/UI/";
    const string Pf = StylizedArtIntegration.PrefabDir;

    static Material M(string name, int mode, Color c, float speed = 1, float dashes = 32, float width = .06f)
    {
        string p = Mat + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("StoneSignal/PlaceFX")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("StoneSignal/PlaceFX");
        m.SetFloat("_Mode", mode); m.SetColor("_Color", c); m.SetFloat("_Speed", speed); m.SetFloat("_Dashes", dashes); m.SetFloat("_Width", width);
        EditorUtility.SetDirty(m); return m;
    }

    static GameObject Quad(string name, Transform parent, Material m, float size, float y)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = name;
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false); q.transform.localPosition = new Vector3(0, y, 0);
        q.transform.localRotation = Quaternion.Euler(90, 0, 0); q.transform.localScale = Vector3.one * size;
        var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return q;
    }

    /// v17.4 landing dust colour: spec FX_Build_Dust #E9C9A0 (light tan), texture is the painted soft puff sheet (light grey).
    /// v17.5: #D9B48A, alpha 1 (the puff sheet is soft: only ~15% of each 2x2 cell is above alpha 0.5, so the v17.4 0.34-0.5 m puffs read
    /// as ~0.15 m smudges at gameplay zoom).
    public static readonly Color LandingDustColor = new Color(0xD9 / 255f, 0xB4 / 255f, 0x8A / 255f, 1f);
    const string DustTex = StylizedArtIntegration.ArtDir + "FX/Placement/T_FX_LandingDust_2x2.png";   // v17.5: denser copy of the portal puff sheet
    const string DustTexFallback = StylizedArtIntegration.ArtDir + "FX/Portal/T_Portal_DustPuff_2x2.png"; // (alpha^0.6 x 1.2, 128 px; portal keeps its own)

    /// M_VFX_LandingDust: URP Particles/Unlit, alpha blended, T_FX_LandingDust_2x2 (v17.5; 2x2 soft puffs), shared by both ghost prefabs.
    static Material LandingDustMaterial()
    {
        const string p = "Assets/Game/Materials/Stylized/M_VFX_LandingDust.mat";
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DustTex);
        if (!tex) { Debug.LogWarning("LANDING DUST: " + DustTex + " missing - using " + DustTexFallback); tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DustTexFallback); }
        if (!tex) throw new System.Exception("LANDING DUST: " + DustTexFallback + " missing");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_BLENDMODE_ADD");
        m.renderQueue = 3000; m.enableInstancing = false; EditorUtility.SetDirty(m);
        return m;
    }

    public static void BuildAll()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game/Materials/Stylized/UI")) AssetDatabase.CreateFolder("Assets/Game/Materials/Stylized", "UI");
        var gV = M("M_UI_Ghost_Valid", 0, new Color(.55f, 1f, .62f, .42f));
        var gI = M("M_UI_Ghost_Invalid", 0, new Color(.898f, .282f, .302f, .5f));   // #E5484D
        var oV = M("M_UI_CellOutline_Valid", 2, new Color(.6f, 1f, .7f, .9f), width: .07f);
        var oI = M("M_UI_CellOutline_Invalid", 2, new Color(.898f, .282f, .302f, 1f), width: .07f);
        var ring = M("M_UI_RangeRing", 1, new Color(1f, .93f, .78f, .85f), .15f, 48, .05f);
        var slot = M("M_UI_SlotHighlight", 2, new Color(1f, .82f, .45f, 1f), width: .12f); slot.renderQueue = 3110;
        var path = M("M_Path_Flow", 3, new Color(1f, .8f, .4f, .9f), .8f, 10);
        var stone = AssetDatabase.LoadAllAssetsAtPath(StylizedArtIntegration.ArtDir + "Environment/SM_Env_Rock_1x1_01.fbx").OfType<Mesh>().FirstOrDefault();

        // v17.4 landing dust: short, soft, light-tan puff ring at the block's base, readable ~0.4 s at gameplay zoom.
        // v17.5: bigger + more opaque: start 0.5-0.8 m growing 1.6x, life ~0.55 s, #D9B48A a 1, slight outward push + upward drift (PlacementGhost).
        // One pooled system per ghost prefab, world space, max 12 particles, no emission module: PlacementGhost.PlayDrop queues the
        // placed cells and emits once per frame along the outer edges of the whole shape (EmitParams). Own material
        // M_VFX_LandingDust (was M_FX_Snow, shared with snow: white, 0.12-0.26 m, burst at the last cell in ghost-local space).
        var dustMat = LandingDustMaterial();
        GameObject Dust(Transform parent)
        {
            var ps = new GameObject("FX_DropDust").AddComponent<ParticleSystem>(); ps.transform.SetParent(parent, false);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var mn = ps.main; mn.playOnAwake = false; mn.loop = true; mn.duration = 1f;
            mn.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .6f); mn.startSpeed = 0;   // speed/direction come from EmitParams
            mn.startSize = new ParticleSystem.MinMaxCurve(.5f, .8f); mn.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            mn.startColor = LandingDustColor; mn.maxParticles = PlacementGhost.DustMax;
            mn.simulationSpace = ParticleSystemSimulationSpace.World; mn.scalingMode = ParticleSystemScalingMode.Shape;
            mn.gravityModifier = -.10f;   // v17.5 gentle rise mn.stopAction = ParticleSystemStopAction.None; mn.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false; em.rateOverTime = 0; em.SetBursts(new ParticleSystem.Burst[0]);
            var sh = ps.shape; sh.enabled = false;
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 2.6f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = true;
            var so = ps.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 1f, 0, 1.6f), new Keyframe(.4f, 1.38f), new Keyframe(1, 1.6f)));   // v17.5: x1.6 over life
            var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, .96f, .9f), 1) },
                       new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(.92f, .5f), new GradientAlphaKey(0, 1) });   // v17.5: holds ~0.28 s, gone by ~0.55 s
            col.color = gr;
            var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.mode = ParticleSystemAnimationMode.Grid; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, .99f);   // random fixed puff per particle
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = dustMat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.None; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.minParticleSize = 0; r.maxParticleSize = 2f; r.sortingFudge = -10;   // drawn after the ghost/blocks it sits on
            return ps.gameObject;
        }

        // PF_UI_PlaceGhost_Block
        var root = new GameObject("PF_UI_PlaceGhost_Block"); var pg = root.AddComponent<PlacementGhost>();
        var cell = new GameObject("Cell"); cell.transform.SetParent(root.transform, false);
        var st = new GameObject("Stone"); st.transform.SetParent(cell.transform, false); st.transform.localPosition = new Vector3(0, .02f, 0);
        st.AddComponent<MeshFilter>().sharedMesh = stone; var sr = st.AddComponent<MeshRenderer>(); sr.sharedMaterial = gV; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Quad("Outline", cell.transform, oV, 1f, .015f);
        pg.cellTemplate = cell; pg.validMat = gV; pg.invalidMat = gI; pg.validOutline = oV; pg.invalidOutline = oI;
        pg.dust = Dust(root.transform).GetComponent<ParticleSystem>();
        PrefabUtility.SaveAsPrefabAsset(root, Pf + "PF_UI_PlaceGhost_Block.prefab"); Object.DestroyImmediate(root);

        // PF_UI_RangeRing
        var rr = new GameObject("PF_UI_RangeRing"); rr.AddComponent<RangeRing>(); Quad("Ring", rr.transform, ring, 1f, .03f);
        rr.transform.localScale = new Vector3(6, 1, 6);
        var rrPf = PrefabUtility.SaveAsPrefabAsset(rr, Pf + "PF_UI_RangeRing.prefab"); Object.DestroyImmediate(rr);

        // PF_UI_PlaceGhost_Tower (+ range ring child)
        var tw = new GameObject("PF_UI_PlaceGhost_Tower"); var tg = tw.AddComponent<PlacementGhost>();
        tg.validMat = gV; tg.invalidMat = gI; tg.validOutline = oV; tg.invalidOutline = oI;
        var foot = new GameObject("Cell"); foot.transform.SetParent(tw.transform, false); Quad("Fill", foot.transform, gV, .94f, .012f); Quad("Outline", foot.transform, oV, 1f, .015f); foot.SetActive(false); tg.cellTemplate = foot;
        var ri = (GameObject)PrefabUtility.InstantiatePrefab(rrPf, tw.transform); ri.name = "RangeRing";
        tg.dust = Dust(tw.transform).GetComponent<ParticleSystem>();
        PrefabUtility.SaveAsPrefabAsset(tw, Pf + "PF_UI_PlaceGhost_Tower.prefab"); Object.DestroyImmediate(tw);

        // PF_UI_SlotHighlight (valid wall-top slot for a turret)
        var sl = new GameObject("PF_UI_SlotHighlight"); Quad("Glow", sl.transform, slot, .92f, .05f);   // v15: 5 cm above wall top (tile bumps hid it at 1 cm)
        PrefabUtility.SaveAsPrefabAsset(sl, Pf + "PF_UI_SlotHighlight.prefab"); Object.DestroyImmediate(sl);

        // PF_Path_FlowSegment: LineRenderer lying on the ground, uv.x along path (Tile mode -> chevrons keep constant size)
        var pth = new GameObject("PF_Path_FlowSegment"); var lr = pth.AddComponent<LineRenderer>();
        lr.sharedMaterial = path; lr.textureMode = LineTextureMode.Tile; lr.alignment = LineAlignment.TransformZ; lr.widthMultiplier = .55f;
        lr.useWorldSpace = true; lr.numCornerVertices = 3; lr.numCapVertices = 2; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        pth.transform.rotation = Quaternion.Euler(90, 0, 0);   // TransformZ alignment: ribbon faces up
        lr.positionCount = 2; lr.SetPositions(new[] { new Vector3(-2, .82f, 0), new Vector3(2, .82f, 0) });
        PrefabUtility.SaveAsPrefabAsset(pth, Pf + "PF_Path_FlowSegment.prefab"); Object.DestroyImmediate(pth);
        AssetDatabase.SaveAssets();
        Debug.Log("PLACEMENT FX built: PF_UI_PlaceGhost_Block, PF_UI_PlaceGhost_Tower, PF_UI_RangeRing, PF_UI_SlotHighlight, PF_Path_FlowSegment, M_Path_Flow");
    }
}
