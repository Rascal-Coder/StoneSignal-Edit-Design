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

        // dust puff (cheap: 14 particles, one burst)
        GameObject Dust(Transform parent)
        {
            var ps = new GameObject("FX_DropDust").AddComponent<ParticleSystem>(); ps.transform.SetParent(parent, false);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var mn = ps.main; mn.playOnAwake = false; mn.loop = false; mn.duration = .6f; mn.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .6f);
            mn.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 1.6f); mn.startSize = new ParticleSystem.MinMaxCurve(.12f, .26f); mn.startColor = new Color(.86f, .78f, .68f, .7f); mn.maxParticles = 24;
            mn.gravityModifier = -.05f;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 14) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .45f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.8f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Stylized/M_FX_Snow.mat");
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
