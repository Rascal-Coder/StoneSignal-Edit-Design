#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace StoneSignal
{
    // -stonesignal-shot <png>: plays a short scripted build + combat on the real runtime board and saves a 1920x1080 frame
    // with the real HUD (canvases are rendered through the game camera for the capture), then quits.
    public sealed class GameplayShot : MonoBehaviour
    {
        GameBootstrap s; string path;
        public void Initialize(GameBootstrap game, string output) { s = game; path = output; Application.runInBackground = true; StartCoroutine(Run()); }
        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1f);
            var grid = s.grid; s.Economy.AddGold(600);
            // a few wall pieces with towers on top, away from the routes
            int built = 0;
            for (int t = 0; t < s.config.towers.Length; t++)
                for (int y = 1; y < grid.height - 1 && built <= t; y++) for (int x = 1; x < grid.width - 1 && built <= t; x++)
                {
                    var size = TowerManager.SizeOf(s.config.towers[t], 0); var o = new Vector2Int(x, y); var cells = grid.Footprint(o, size);
                    if (!cells.TrueForAll(grid.CanPlace) || cells.Exists(c => s.Paths.CurrentPaths.Exists(p => p.Contains(c))) || s.Validator.ValidatePlacement(cells) != null) continue;
                    if ((grid.FootprintCenter(o, size) - grid.CoreCenter).magnitude < 2.5f || (x + y + t) % 3 != 0) continue;
                    grid.Commit(cells, CellState.Blocked); foreach (var c in cells) ArtVisual.Wall(s.config.palette, s.transform, grid.ToWorld(c), grid.cellSize);
                    if (s.Towers.TryBuild(o, t, 0)) built++;
                }
            s.Waves.StartWave();
            yield return new WaitForSecondsRealtime(4.5f);
            StoneSignal.VFX.HitStop.Cancel(); Time.timeScale = 1;
            yield return new WaitForEndOfFrame();
            Capture();
            Debug.Log("GAMEPLAY SHOT " + path);
            Application.Quit(0);
        }
        void Capture()
        {
            var cam = s.viewCamera; var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var canvases = FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { modes[i] = canvases[i].renderMode; canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = cam; canvases[i].planeDistance = cam.nearClipPlane + .05f + i * .01f; }
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); Canvas.ForceUpdateCanvases(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
        }
    }
}
#endif
