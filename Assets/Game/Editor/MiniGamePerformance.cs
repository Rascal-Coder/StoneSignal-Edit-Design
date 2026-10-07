using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StoneSignal.EditorTools
{
    // WeChat mini-game / WebGL performance profile. Idempotent; never touches the demo/showcase scenes or their volume profile.
    // Menu: StoneSignal > Performance > Apply mini-game settings
    // Batch: Tuanjie.exe -batchmode -nographics -quit -projectPath <p> -executeMethod StoneSignal.EditorTools.MiniGamePerformance.Apply -logFile <f>
    public static class MiniGamePerformance
    {
        const string BaseUrp = "Assets/Game/Settings/StoneSignalURP.asset";
        const string MiniUrp = "Assets/Game/Settings/StoneSignalURP_MiniGame.asset";
        const string MiniQuality = "Medium"; // WeixinMiniGame default quality index 2
        static readonly string[] SkipPaths = { "VP_StylizedDemo", "StylizedArtDemo", "StylizedVFXShowcase" };
        static readonly Type[] ExpensivePost = { typeof(DepthOfField), typeof(MotionBlur), typeof(ChromaticAberration), typeof(FilmGrain), typeof(LensDistortion), typeof(PaniniProjection) };

        [MenuItem("StoneSignal/Performance/Apply mini-game settings")]
        public static void Apply()
        {
            var log = new System.Text.StringBuilder();
            // 1) SRP Batcher on the shared URP asset; cheap copy for the mini-game quality level.
            var baseAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(BaseUrp);
            if (baseAsset == null) throw new Exception("Missing " + BaseUrp);
            baseAsset.useSRPBatcher = true; EditorUtility.SetDirty(baseAsset);
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MiniUrp) == null) AssetDatabase.CopyAsset(BaseUrp, MiniUrp);
            var mini = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MiniUrp);
            mini.useSRPBatcher = true;
            mini.shadowDistance = 18; mini.shadowCascadeCount = 1; mini.msaaSampleCount = 1; mini.supportsHDR = false; mini.renderScale = 1;
            var so = new SerializedObject(mini);
            Set(so, "m_MainLightShadowmapResolution", 1024); Set(so, "m_AdditionalLightShadowsSupported", 0);
            Set(so, "m_SoftShadowsSupported", 0); Set(so, "m_AdditionalLightsPerObjectLimit", 2);
            Set(so, "m_RequireDepthTexture", 1) /* v8 water/edge FX need opaque depth; URP copy-depth pass is cheap */; Set(so, "m_RequireOpaqueTexture", 0);
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(mini);
            log.Append("URP: SRP batcher on; mini-game asset shadows 1024/18m/1 cascade/hard, MSAA off, HDR off; ");

            // 2) Point the WeChat/WebGL quality level at the cheap asset.
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs != null)
            {
                var q = new SerializedObject(qs);
                var levels = q.FindProperty("m_QualitySettings"); int mi = -1;
                for (int i = 0; i < levels.arraySize; i++)
                {
                    var l = levels.GetArrayElementAtIndex(i);
                    if (l.FindPropertyRelative("name").stringValue != MiniQuality) continue;
                    mi = i;
                    l.FindPropertyRelative("customRenderPipeline").objectReferenceValue = mini;
                    l.FindPropertyRelative("shadows").intValue = 1;          // hard only
                    l.FindPropertyRelative("shadowResolution").intValue = 0;
                    l.FindPropertyRelative("shadowDistance").floatValue = 18;
                    l.FindPropertyRelative("pixelLightCount").intValue = 1;
                    l.FindPropertyRelative("antiAliasing").intValue = 0;
                }
                var per = q.FindProperty("m_PerPlatformDefaultQuality");
                if (mi >= 0 && per != null)
                    for (int i = 0; i < per.arraySize; i++)
                    {
                        var e = per.GetArrayElementAtIndex(i);
                        var key = e.FindPropertyRelative("first"); var val = e.FindPropertyRelative("second");
                        if (key != null && val != null && (key.stringValue == "WebGL" || key.stringValue == "WeixinMiniGame")) val.intValue = mi;
                    }
                q.ApplyModifiedPropertiesWithoutUndo();
                log.Append("quality '" + MiniQuality + "'(" + mi + ") -> " + mini.name + " for WebGL/WeixinMiniGame; ");
            }

            // 3) No SSAO renderer features; expensive post effects off in game volume profiles.
            int ssao = 0;
            foreach (var g in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Game" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(g));
                foreach (var f in data.rendererFeatures) if (f != null && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion") && f.isActive) { f.SetActive(false); ssao++; EditorUtility.SetDirty(data); }
            }
            int post = 0;
            foreach (var g in AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets/Game" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (SkipPaths.Any(path.Contains)) continue;
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                foreach (var c in profile.components)
                {
                    if (c == null) continue;
                    if (ExpensivePost.Contains(c.GetType()) && c.active) { c.active = false; post++; }
                    if (c is Bloom b) { b.highQualityFiltering.overrideState = true; b.highQualityFiltering.value = false; }
                }
                EditorUtility.SetDirty(profile);
            }
            log.Append("SSAO features disabled=" + ssao + "; expensive post overrides disabled=" + post + "; ");

            // 4) GPU instancing on game materials (SRP Batcher still takes precedence for compatible shaders).
            int inst = 0;
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Game/Materials", "Assets/Game/VFX" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (m != null && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); inst++; }
            }
            log.Append("instancing enabled on " + inst + " materials; ");

            // 5) ASTC texture compression for WebGL (and the WeChat mini-game target when this Tuanjie build exposes it).
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC;
            log.Append("WebGL texture subtarget=ASTC; ");
            var wx = typeof(EditorUserBuildSettings).GetProperties().FirstOrDefault(p => p.Name.IndexOf("MiniGame", StringComparison.OrdinalIgnoreCase) >= 0 && p.Name.IndexOf("Subtarget", StringComparison.OrdinalIgnoreCase) >= 0 && p.CanWrite);
            if (wx != null && wx.PropertyType.IsEnum && Enum.GetNames(wx.PropertyType).Contains("ASTC")) { wx.SetValue(null, Enum.Parse(wx.PropertyType, "ASTC")); log.Append(wx.Name + "=ASTC; "); }
            else log.Append("mini-game subtarget API not found (set ASTC in the WeChat mini-game build panel); ");

            AssetDatabase.SaveAssets();
            Debug.Log("MINIGAME PERF: " + log);
        }
        static void Set(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) { if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0; else p.intValue = v; } }
    }
}
