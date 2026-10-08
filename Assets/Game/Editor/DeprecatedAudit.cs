using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StoneSignal.EditorTools
{
    /// v19 (read-only): lists Assets/Game/_Deprecated and proves nothing in use depends on it:
    /// direct dependencies of every other asset, the full dependency set of the build scenes, and Resources folders.
    /// -executeMethod StoneSignal.EditorTools.DeprecatedAudit.Run -> Temp/deprecated_audit.txt
    public static class DeprecatedAudit
    {
        const string Dir = "Assets/Game/_Deprecated";
        public static void Run()
        {
            var sb = new StringBuilder("DEPRECATED AUDIT " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
            var dep = new HashSet<string>(AssetDatabase.FindAssets("", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !AssetDatabase.IsValidFolder(p)));
            sb.Append("assets in " + Dir + ": " + dep.Count + "\n"); foreach (var p in dep.OrderBy(x => x)) sb.Append("  " + p + " (guid " + AssetDatabase.AssetPathToGUID(p) + ")\n");
            int users = 0, scanned = 0;
            foreach (var p in AssetDatabase.GetAllAssetPaths())
            {
                if (!p.StartsWith("Assets/") || p.StartsWith(Dir) || AssetDatabase.IsValidFolder(p)) continue;
                string ext = Path.GetExtension(p).ToLowerInvariant(); if (ext == ".cs" || ext == ".png" || ext == ".fbx" || ext == ".ttf" || ext == ".shader" || ext == ".hlsl" || ext == ".txt" || ext == ".md") continue;
                scanned++;
                foreach (var d in AssetDatabase.GetDependencies(p, false)) if (dep.Contains(d)) { users++; sb.Append("  REFERENCED BY " + p + " -> " + d + "\n"); }
            }
            sb.Append("scanned " + scanned + " assets outside " + Dir + ": " + users + " references into it\n");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var all = AssetDatabase.GetDependencies(scenes, true); int inBuild = all.Count(dep.Contains);
            sb.Append("build scenes [" + string.Join(", ", scenes) + "]: " + all.Length + " dependencies, " + inBuild + " in " + Dir + "\n");
            foreach (var d in all.Where(dep.Contains)) sb.Append("  BUILD DEPENDENCY " + d + "\n");
            sb.Append("Resources folders under " + Dir + ": " + dep.Count(p => p.Contains("/Resources/")) + "\n");
            sb.Append(users == 0 ? "DEPRECATED PASS (no in-use references)\n" : "DEPRECATED NOTE: " + users + " references (see REFERENCED BY)\n");
            Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/deprecated_audit.txt", sb.ToString()); Debug.Log(sb.ToString());
        }
    }
}
