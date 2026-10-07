using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// v17.3 fix: textureless URP particle materials rendered as hard white squares.
/// Root cause (found on the v17.2 project files, not assumed):
///   * M_FX_Snow (URP Particles/Unlit) was created with `new Material(shader)` and never given a texture or a transparent
///     surface -> opaque white quads. It is used by FX_Weather_Snow inside PF_Env_LevelDressing_16x12 (the small white
///     squares over the water/board in every v17.2 shot) AND by FX_DropDust in the block placement ghost (dust burst = white squares).
///   * M_VFX_PortalEmber (PF_VFX_StatusFx: poison/slow/burn/shock/frost particles) has _BaseMap = none -> additive squares.
///   * (not a material) the white blocks around the spawn ring at t=0.35 are the 8 rubble chunks: SM_Portal_Rubble.fbx had
///     Blender auto UVs that sample the unused (240,240,240) palette cells -> re-exported with palette UVs (portal_rubble_v17_3.py).
///   The spawn Dust/Flare/Flash, death puff and footprint materials DO have their _BaseMap (checked in the .mat files).
/// Fix(): gives both materials T_FX_SoftDot + a proper alpha/additive transparent setup. Materials are referenced by GUID,
/// so every prefab that uses them is fixed without rebuilding it. Checks(): fails BatchImport loudly if any particle renderer
/// in the stylized prefabs has no material, or a texture-sampling particle material (URP Particles/*, SS_GroundPrint) has a
/// null _BaseMap or an opaque surface.
/// v17.4: Checks() only judges renderers that can draw (VfxMaterialRules): disabled renderers and non-emitting container
/// systems (StylizedVFXBuilder.Root: emission off, renderer off, no material - FX_Explosion_*/FX_Hit_*/FX_Muzzle_*/FX_Enemy_*)
/// are skipped; an enabled renderer whose system emits (rate/bursts, sub-emitter, or a system a script field references for
/// Emit()) with a null material still FAILS, and so does any enabled leaf system without a material. StoneSignal/FXAdditive +
/// FXAlpha are procedural (SS_FXCore.hlsl, no texture property) and are whitelisted explicitly, never given T_FX_SoftDot.
public static class StylizedVfxTexturesV173
{
    const string MatDir = "Assets/Game/Materials/Stylized/";
    const string SoftDot = StylizedArtIntegration.ArtDir + "FX/Common/T_FX_SoftDot.png";
    static readonly string[] PrefabDirs = { "Assets/Game/Prefabs/Stylized", "Assets/Game/VFX/Stylized" };

    [MenuItem("StoneSignal/Stylized art/Fix textureless VFX materials (v17.3)")]
    public static void Fix()
    {
        AssetDatabase.ImportAsset(SoftDot, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(SoftDot);
        if (ti != null) { ti.alphaIsTransparency = true; ti.sRGBTexture = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.SaveAndReimport(); }
        var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftDot);
        if (!dot) throw new System.Exception("VFX TEXTURES FAIL: " + SoftDot + " did not import");
        Setup("M_FX_Snow", dot, false, new Color(1f, 1f, 1f, .9f));        // snow flakes + block drop dust: soft round, alpha blended
        Setup("M_VFX_PortalEmber", dot, true, Color.white);                // status FX (additive)
        AssetDatabase.SaveAssets();
        Debug.Log("VFX TEXTURES v17.3: M_FX_Snow + M_VFX_PortalEmber -> T_FX_SoftDot (transparent)");
    }

    static void Setup(string name, Texture2D tex, bool additive, Color tint)
    {
        string p = MatDir + name + ".mat"; var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", tint);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0); m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
        m.renderQueue = 3000; EditorUtility.SetDirty(m);
    }

    static bool SamplesBaseMap(Material m) => StoneSignal.VFX.VfxMaterialRules.SamplesBaseMap(m);

    /// v17.4: particle systems that a MonoBehaviour in the same prefab references (EnemyGroundFxSystem.dust, PlacementGhost.dust,
    /// SpawnPortal.dust ...): these are driven by ParticleSystem.Emit()/Play() from code, so "emission module off" does not mean idle.
    static HashSet<ParticleSystem> ScriptReferenced(GameObject root)
    {
        var set = new HashSet<ParticleSystem>();
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!mb) continue; var it = new SerializedObject(mb).GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || !it.objectReferenceValue) continue;
                var o = it.objectReferenceValue; var ps = o as ParticleSystem;
                if (!ps && o is GameObject g) ps = g.GetComponent<ParticleSystem>();
                if (!ps && o is Component c && !(o is Transform)) ps = c.GetComponent<ParticleSystem>();
                if (ps) set.Add(ps);
            }
        }
        return set;
    }

    /// v17.4: can this renderer put pixels on screen? Disabled -> no. A container system (has child systems, nothing in its own
    /// emission module, not a sub-emitter, not referenced by a script) -> no. Leaf systems always count (they may be Emit()-driven
    /// by name lookup), so a leaf without a material still fails.
    static bool CanDraw(ParticleSystemRenderer r, GameObject root, HashSet<ParticleSystem> scripted)
    {
        if (!r.enabled) return false;
        var ps = r.GetComponent<ParticleSystem>(); if (!ps) return true;
        if (StoneSignal.VFX.VfxMaterialRules.EmitsByModules(ps) || scripted.Contains(ps) || StoneSignal.VFX.VfxMaterialRules.IsSubEmitter(ps, root)) return true;
        bool container = ps.GetComponentsInChildren<ParticleSystem>(true).Length > 1;
        return !container;
    }

    public static void Checks()
    {
        var errs = new List<string>(); int renderers = 0, idle = 0, procedural = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabDirs.Where(AssetDatabase.IsValidFolder).ToArray()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid); var go = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!go) continue;
            HashSet<ParticleSystem> scripted = null;
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                renderers++; var m = r.sharedMaterial; string where = System.IO.Path.GetFileNameWithoutExtension(path) + "/" + r.name;
                if (scripted == null) scripted = ScriptReferenced(go);
                if (!CanDraw(r, go, scripted)) { idle++; continue; }   // v17.4: disabled / non-emitting container root draws nothing
                if (!m) { errs.Add(where + ": emitting particle renderer has no material (renders Unity's default particle)"); continue; }
                if (StoneSignal.VFX.VfxMaterialRules.IsProcedural(m)) { procedural++; continue; }   // FXAlpha/FXAdditive: procedural shape, no texture
                if (!SamplesBaseMap(m)) continue;   // other custom shaders: not judged here
                if (!m.GetTexture("_BaseMap")) errs.Add(where + ": " + m.name + " has a NULL _BaseMap (white squares)");
                if (m.shader.name.StartsWith("Universal Render Pipeline/Particles") && m.GetFloat("_Surface") < .5f) errs.Add(where + ": " + m.name + " is opaque (_Surface 0)");
                var ps = r.GetComponent<ParticleSystem>(); var tex = m.GetTexture("_BaseMap");
                if (ps && tex && (tex.name.EndsWith("_2x2") || tex.name.EndsWith("_4x4")))
                {
                    int n = tex.name.EndsWith("_2x2") ? 2 : 4; var tsa = ps.textureSheetAnimation;
                    if (!tsa.enabled || tsa.numTilesX != n || tsa.numTilesY != n) errs.Add(where + ": " + tex.name + " needs texture sheet animation " + n + "x" + n);
                }
            }
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir.TrimEnd('/') }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m && SamplesBaseMap(m) && !m.GetTexture("_BaseMap")) errs.Add("material " + m.name + " (" + m.shader.name + ") has a NULL _BaseMap");
        }
        if (errs.Count > 0) throw new System.Exception("VFX TEXTURES FAIL (" + errs.Count + ")\n" + string.Join("\n", errs));
        Debug.Log("VFX TEXTURES v17.4 PASS (" + renderers + " particle renderers: " + idle + " idle containers/disabled skipped, " + procedural + " procedural FXAlpha/FXAdditive, no null main textures)");
    }
}
