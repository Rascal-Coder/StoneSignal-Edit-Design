using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StoneSignal.EditorTools
{
    // Builds the runtime board (GridView: tiles, core, cliff, water, islands, bridges) in edit mode inside Game.unity
    // and renders the Game camera to Verification/Screenshots/game.png. The scene is reopened afterwards, never saved.
    // Batch: Tuanjie.exe -batchmode -projectPath <p> -executeMethod StoneSignal.EditorTools.GameScreenshot.Capture -logFile <f>
    public static class GameScreenshot
    {
        const string ScenePath = "Assets/Game/Scenes/Game.unity";
        public const string Output = "Verification/Screenshots/game.png";

        [MenuItem("StoneSignal/Capture game screenshot")]
        public static void CaptureMenu() { Run(); EditorUtility.RevealInFinder(Output); }

        public static void Capture()
        {
            try { Run(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }

        // Budget (WeChat mini-game): < 150 draw calls, < 150k tris. Draw calls estimated as visible renderers x sub-meshes.
        public const int DrawCallBudget = 150, TriBudget = 150000;
        static string Stats(Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int renderers = 0, visible = 0, draws = 0; long tris = 0, visTris = 0;
            var mats = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                long t = 0; int subs = 1;
                if (m != null) { subs = m.subMeshCount; for (int i = 0; i < subs; i++) t += m.GetIndexCount(i) / 3; }
                renderers++; tris += t;
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) { visible++; visTris += t; draws += Mathf.Max(1, subs); foreach (var mt in r.sharedMaterials) if (mt) mats.Add(mt); }
            }
            return "renderers=" + renderers + "\nvisibleRenderers=" + visible + "\ntotalTris=" + tris + "\nvisibleTris=" + visTris +
                   "\nestimatedDrawCalls=" + draws + " (pre-batching)\nuniqueMaterials=" + mats.Count +
                   "\nbudget drawCalls<" + DrawCallBudget + " tris<" + TriBudget + " -> " + (draws < DrawCallBudget && tris < TriBudget ? "OK" : "OVER") + "\n";
        }

        static void Run()
        {
            if (EditorSceneManager.GetActiveScene().isDirty && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            var boot = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (boot == null || boot.config == null || boot.grid == null || boot.viewCamera == null) throw new Exception("Game.unity bootstrap incomplete");
            var grid = boot.grid;
            if (boot.config.layout != null) grid.layout = boot.config.layout;
            grid.Initialize();
            var root = new GameObject("[Screenshot board]");
            var paths = root.AddComponent<PathfindingManager>(); paths.Initialize(grid);
            root.AddComponent<GridView>().Initialize(grid, paths, boot.config.palette);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            StylizedArtIntegration.Capture(boot.viewCamera, Output);
            File.WriteAllText(Path.ChangeExtension(Output, ".txt"), Stats(boot.viewCamera));
            Debug.Log("GAME SCREENSHOT " + Path.GetFullPath(Output) + "\n" + File.ReadAllText(Path.ChangeExtension(Output, ".txt")));
            EditorSceneManager.OpenScene(ScenePath); // discard edit-mode board
        }
    }
}
