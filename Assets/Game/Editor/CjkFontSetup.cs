#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace StoneSignal.EditorTools
{
    /// v17.1 CJK font (Docs/font_cjk_tmp_spec.md, settings per lead: Dynamic, 2048 multi-atlas, sampling 48, padding 6, SDFAA).
    /// Creates Assets/Game/Art/Fonts/StoneSignalRoundedCN-Heavy SDF.asset + "- Outline" material, and puts the font FIRST in the
    /// LiberationSans SDF fallback list and in TMP Settings fallback list. Idempotent. Batch: -executeMethod StoneSignal.EditorTools.CjkFontSetup.Run
    public static class CjkFontSetup
    {
        const string Dir = "Assets/Game/Art/Fonts/", Ttf = Dir + "StoneSignalRoundedCN-Heavy.ttf", Asset = Dir + "StoneSignalRoundedCN-Heavy SDF.asset",
                     OutlineMat = Dir + "StoneSignalRoundedCN-Heavy SDF - Outline.mat", Lib = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        [MenuItem("StoneSignal/Fonts/Setup CJK TMP font")]
        public static void Setup()
        {
            var imp = (TrueTypeFontImporter)AssetImporter.GetAtPath(Ttf);
            if (imp == null) throw new System.Exception("CJK FONT: missing " + Ttf);
            if (imp.fontTextureCase != FontTextureCase.Dynamic || !imp.includeFontData) { imp.fontTextureCase = FontTextureCase.Dynamic; imp.includeFontData = true; imp.SaveAndReimport(); }
            var font = AssetDatabase.LoadAssetAtPath<Font>(Ttf);
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Asset);
            if (fa == null)
            {
                fa = TMP_FontAsset.CreateFontAsset(font, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                fa.name = "StoneSignalRoundedCN-Heavy SDF";
                AssetDatabase.CreateAsset(fa, Asset);
                fa.atlasTextures[0].name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            fa.isMultiAtlasTexturesEnabled = true; fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            // keep the shipped asset small: glyphs are rasterised at runtime from the included TTF
            var so = new SerializedObject(fa); var clr = so.FindProperty("m_ClearDynamicDataOnBuild"); if (clr != null) { clr.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(fa);
            // outline preset
            var om = AssetDatabase.LoadAssetAtPath<Material>(OutlineMat);
            if (om == null) { om = new Material(fa.material) { name = "StoneSignalRoundedCN-Heavy SDF - Outline" }; AssetDatabase.CreateAsset(om, OutlineMat); }
            om.CopyPropertiesFromMaterial(fa.material);
            om.SetColor("_FaceColor", new Color32(0xFF, 0xFA, 0xF4, 0xFF)); om.SetFloat("_FaceDilate", .05f);
            om.SetColor("_OutlineColor", new Color32(0x1E, 0x1A, 0x3A, 0xFF)); om.SetFloat("_OutlineWidth", .25f); om.EnableKeyword("OUTLINE_ON");
            EditorUtility.SetDirty(om);
            // fallback chains: first
            var lib = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Lib);
            lib.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
            lib.fallbackFontAssetTable.Remove(fa); lib.fallbackFontAssetTable.Insert(0, fa); EditorUtility.SetDirty(lib);
            var st = TMP_Settings.instance; var sso = new SerializedObject(st); var list = sso.FindProperty("m_fallbackFontAssets");
            for (int i = list.arraySize - 1; i >= 0; i--) if (list.GetArrayElementAtIndex(i).objectReferenceValue == fa) list.DeleteArrayElementAtIndex(i);
            list.InsertArrayElementAtIndex(0); list.GetArrayElementAtIndex(0).objectReferenceValue = fa; sso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(st);
            AssetDatabase.SaveAssets();
            // smoke: the tier labels must resolve to glyphs from the TTF (dynamic population works in the editor)
            bool ok = fa.HasCharacters("普通精良稀有传说", out var missing, false, true);
            Debug.Log("CJK FONT SETUP: asset=" + Asset + " sampling=" + fa.faceInfo.pointSize + " padding=" + fa.atlasPadding + " mode=" + fa.atlasRenderMode +
                      " atlas=" + fa.atlasWidth + "x" + fa.atlasHeight + " multiAtlas=" + fa.isMultiAtlasTexturesEnabled + " population=" + fa.atlasPopulationMode +
                      " | LiberationSans fallbacks=[" + string.Join(", ", lib.fallbackFontAssetTable.Select(f => f ? f.name : "null")) + "]" +
                      " | TMP Settings fallbacks=[" + string.Join(", ", TMP_Settings.fallbackFontAssets.Select(f => f ? f.name : "null")) + "]" +
                      " | tier glyphs ok=" + ok + (missing != null && missing.Length > 0 ? " missing=" + string.Join(",", missing) : "") + " | ttf includeFontData=" + imp.includeFontData);
            // do not ship the glyphs added by the check
            fa.ClearFontAssetData(true); EditorUtility.SetDirty(fa); AssetDatabase.SaveAssets();
        }
        public static void Run() { int code = 0; try { Setup(); } catch (System.Exception e) { Debug.LogError("CJK FONT SETUP FAILED: " + e); code = 1; } EditorApplication.Exit(code); }
    }
}
#endif
