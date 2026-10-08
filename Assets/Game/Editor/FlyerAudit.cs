using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StoneSignal.EditorTools
{
    /// v19 flyer audit (read-only): which visual / mesh / FBX / clips every EnemyData and PF_Enemy_Flyer really use.
    /// -executeMethod StoneSignal.EditorTools.FlyerAudit.Run  -> Temp/flyer_audit.txt
    public static class FlyerAudit
    {
        const string FlyerPrefab = "Assets/Game/Prefabs/Stylized/PF_Enemy_Flyer.prefab", FlyerFbx = "Assets/Game/Art/Stylized/Enemies/SM_Enemy_Flyer_01.fbx";
        public static void Run()
        {
            var sb = new StringBuilder("FLYER AUDIT " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
            sb.Append("EnemyData assets:\n");
            foreach (var g in AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/Game" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); var d = AssetDatabase.LoadAssetAtPath<EnemyData>(p);
                sb.Append("  " + p + ": visualPrefab=" + (d.visualPrefab ? AssetDatabase.GetAssetPath(d.visualPrefab) : "NULL") + " flying=" + d.flying + " kind=" + d.kind + " fast=" + d.fast + Model(d.visualPrefab) + "\n");
            }
            sb.Append("Waves:\n");
            foreach (var g in AssetDatabase.FindAssets("t:WaveData", new[] { "Assets/Game" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); var w = AssetDatabase.LoadAssetAtPath<WaveData>(p);
                sb.Append("  " + Path.GetFileNameWithoutExtension(p) + " interval " + w.spawnInterval + ":" + string.Join(",", w.groups.Select(x => " " + (x.enemy ? x.enemy.name : "NULL") + " x" + x.count + (x.spawnIndex >= 0 ? " @" + x.spawnIndex : ""))) + " (total " + w.Total + ", kill gold " + w.groups.Sum(x => x.enemy ? x.enemy.reward * x.count : 0) + ")\n");
            }
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(FlyerPrefab);
            sb.Append("PF_Enemy_Flyer: " + (pf ? "found" : "MISSING") + (pf ? Model(pf) : "") + "\n");
            if (pf)
            {
                foreach (var t in pf.GetComponentsInChildren<Transform>(true)) if (t.GetComponents<Component>().Length > 1) sb.Append("    node '" + t.name + "' local y " + t.localPosition.y.ToString("F3") + " components: " + string.Join(", ", t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name)) + "\n");
                var an = pf.GetComponentInChildren<Animator>(true); var ac = an ? an.runtimeAnimatorController : null;
                sb.Append("  animator controller: " + (ac ? AssetDatabase.GetAssetPath(ac) : "NONE") + "\n");
                if (ac) foreach (var c in ac.animationClips.Distinct()) sb.Append("    clip " + c.name + " length " + c.length.ToString("F3") + " s @" + c.frameRate + " fps (" + Mathf.RoundToInt(c.length * c.frameRate) + " frames) loop " + c.isLooping + " from " + AssetDatabase.GetAssetPath(c) + "\n");
                var ctl = ac as UnityEditor.Animations.AnimatorController;
                if (ctl) foreach (var st in ctl.layers[0].stateMachine.states) sb.Append("    state " + st.state.name + " motion " + (st.state.motion ? st.state.motion.name : "-") + " speed " + st.state.speed + (st.state.speedParameterActive ? " x param " + st.state.speedParameter : "") + "\n");
            }
            var mi = AssetImporter.GetAtPath(FlyerFbx) as ModelImporter;
            if (mi)
            {
                var fi = new FileInfo(FlyerFbx);
                sb.Append("SM_Enemy_Flyer_01.fbx: " + fi.Length + " bytes, mtime " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ", animationType " + mi.animationType + ", importAnimation " + mi.importAnimation + ", globalScale " + mi.globalScale + "\n");
                foreach (var c in mi.clipAnimations) sb.Append("    importer clip " + c.name + " take " + c.takeName + " frames " + c.firstFrame + ".." + c.lastFrame + " loop " + c.loopTime + "\n");
                foreach (var c in mi.defaultClipAnimations) sb.Append("    file take " + c.takeName + " frames " + c.firstFrame + ".." + c.lastFrame + "\n");
                var mesh = AssetDatabase.LoadAllAssetsAtPath(FlyerFbx).OfType<Mesh>().FirstOrDefault();
                if (mesh) sb.Append("    mesh " + mesh.name + " verts " + mesh.vertexCount + " bounds min " + mesh.bounds.min.ToString("F2") + " max " + mesh.bounds.max.ToString("F2") + "\n");
            }
            Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/flyer_audit.txt", sb.ToString()); Debug.Log(sb.ToString());
        }
        static string Model(GameObject pf)
        {
            if (!pf) return ""; var sb = new StringBuilder();
            foreach (var sm in pf.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                sb.Append(" | skinned '" + sm.name + "' mesh " + (sm.sharedMesh ? sm.sharedMesh.name + " (" + sm.sharedMesh.vertexCount + " v) from " + AssetDatabase.GetAssetPath(sm.sharedMesh) : "NULL") + " bones [" + string.Join(",", sm.bones.Where(b => b).Select(b => b.name)) + "] material " + (sm.sharedMaterial ? sm.sharedMaterial.name : "-"));
            foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh && !mf.name.StartsWith("Blob")) sb.Append(" | mesh '" + mf.name + "' " + mf.sharedMesh.name + " from " + AssetDatabase.GetAssetPath(mf.sharedMesh));
            return sb.ToString();
        }
    }
}
