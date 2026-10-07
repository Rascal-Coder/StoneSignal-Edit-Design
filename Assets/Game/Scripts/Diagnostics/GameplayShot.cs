#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;

namespace StoneSignal
{
    // -stonesignal-shot <png>: scripted build + combat on the real runtime board; pins a VALID block ghost, a BLOCKED tower
    // ghost and the slot highlights (tower selected), saves a 1920x1080 frame with the real HUD, writes <png>.txt with the
    // measured frame stats (ProfilerRecorder: draw calls, batches, set-pass, tris), then quits.
    public sealed class GameplayShot : MonoBehaviour
    {
        GameBootstrap s; string path;
        ProfilerRecorder draws, batches, setPass, tris;
        public void Initialize(GameBootstrap game, string output)
        {
            s = game; path = output; Application.runInBackground = true;
            draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            StartCoroutine(Run());
        }
        bool OnRoute(Vector2Int c) { foreach (var p in s.Paths.CurrentPaths) if (p.Contains(c)) return true; return false; }
        bool TryWall(Vector2Int c)
        {
            var cells = new List<Vector2Int> { c };
            if (!s.grid.CanPlace(c) || s.Validator.ValidatePlacement(cells) != null) return false;
            s.grid.Commit(cells, CellState.Blocked); s.Blocks.SpawnWall(c);
            return true;
        }
        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1f);
            var grid = s.grid; s.Economy.AddGold(900);
            // wall clusters (deterministic pattern) that leave every route open
            for (int y = 1; y < grid.height - 1; y++) for (int x = 1; x < grid.width - 1; x++)
                if (((x * 7 + y * 3) % 6) < 2 && (new Vector2(x, y) - new Vector2(grid.width * .5f, grid.height * .5f)).magnitude > 2.2f) TryWall(new Vector2Int(x, y));
            // towers on wall tops, two of each, spread over the board
            for (int t = 0; t < s.config.towers.Length; t++)
            {
                int built = 0; var size = TowerManager.SizeOf(s.config.towers[t], 0);
                for (int y = (t * 3) % grid.height; y < grid.height && built < 2; y++) for (int x = (t * 5) % grid.width; x < grid.width && built < 2; x++)
                {
                    var o = new Vector2Int(x, y);
                    if (size != Vector2Int.one && s.Towers.Validate(o, t, 0) != null)
                    {
                        // multi-cell towers: raise a matching wall pad first (all cells free, off-route, route stays open)
                        var pad = grid.Footprint(o, size);
                        if (pad.TrueForAll(c => s.grid.CanPlace(c) && !OnRoute(c)) && s.Validator.ValidatePlacement(pad) == null)
                        { s.grid.Commit(pad, CellState.Blocked); foreach (var c in pad) s.Blocks.SpawnWall(c); }
                    }
                    if (s.Towers.Validate(o, t, 0) != null) continue;
                    if (s.Towers.TryBuild(o, t, 0)) { built++; x += 3; }
                }
            }
            // runes: blade under the first tower, resonance under its neighbour-most tower, frost inlaid on a free wall
            var tw = s.Towers.Towers;
            if (tw.Count > 0) s.Blocks.InlayRune(tw[0].Cells[0], (int)StoneSignal.VFX.RuneId.Blade);
            if (tw.Count > 1) s.Blocks.InlayRune(tw[1].Cells[0], (int)StoneSignal.VFX.RuneId.Resonance);
            foreach (var c in s.Blocks.WallCells) if (grid.Get(c) == CellState.Blocked) { s.Blocks.InlayRune(c, (int)StoneSignal.VFX.RuneId.Frost); break; }
            // hand: identical cards -> one stacked xN card; one rune-carrying card
            var h = s.Blocks.Hand; if (h.Cards.Count > 0) { var first = h.Cards[0]; h.AddCard(first, h.Runes[0]); h.AddCard(first, h.Runes[0]); h.AddCard(h.Cards[h.Cards.Count - 1], (int)StoneSignal.VFX.RuneId.Swift); }
            s.Blocks.NotifyChanged();
            s.Blocks.MergeWalls();
            s.Waves.StartWave();
            yield return new WaitForSecondsRealtime(6f);
            // pinned previews: tower (index 1) selected -> slot highlights + BLOCKED ghost on open ground; VALID block ghost
            Vector2Int blocked = default, valid = default; bool fb = false, fv = false;
            for (int y = 1; y < grid.height - 1 && !(fb && fv); y++) for (int x = 1; x < grid.width - 1; x++)
            {
                var c = new Vector2Int(x, y);
                if (!fb && x > grid.width * .6f && y < grid.height * .4f && grid.CanPlace(c) && !OnRoute(c)) { blocked = c; fb = true; }
                if (!fv && x < grid.width * .4f && y < grid.height * .4f && s.Blocks.CurrentShape != null && s.Blocks.ValidatePlacement(c) == null) { valid = c; fv = true; }
            }
            s.Blocks.SelectCard(0);
            s.Towers.PinPreview(1, blocked);
            s.Blocks.Pinned = true; s.Blocks.SetToolActive(true); if (fv) s.Blocks.Preview(valid);
            if (fv) Label("VALID", s.grid.ToWorld(valid), new Color(.55f, 1f, .6f));
            if (fb) Label("BLOCKED", s.grid.ToWorld(blocked), new Color(1f, .45f, .55f));
            TimeController.ResetAll();
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            string stats = Stats();
            Capture();
            yield return Breakdown(r => stats += r);
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), stats);
            Debug.Log("GAMEPLAY SHOT " + path + "\n" + stats);
            Application.Quit(0);
        }
        // Draw-call breakdown: switch one group off at a time and read the profiler delta (2 frames later).
        IEnumerator Breakdown(System.Action<string> add)
        {
            long Dc() => draws.LastValue;
            for (int f = 0; f < 6; f++) yield return null; long baseDc = Dc();
            var report = new System.Text.StringBuilder("\nbreakdown (draw calls removed when the group is hidden; base " + baseDc + "):");
            IEnumerator Try(string name, List<Behaviour> beh, List<Renderer> rs)
            {
                yield return null; yield return null; long b0 = Dc(); // fresh baseline per group (capture renders inflate earlier frames)
                foreach (var b in beh) if (b) b.enabled = false; foreach (var r in rs) if (r) r.enabled = false;
                yield return null; yield return null; long d = Dc();
                foreach (var b in beh) if (b) b.enabled = true; foreach (var r in rs) if (r) r.enabled = true;
                report.Append("\n  " + name + ": " + (b0 - d) + " of " + b0 + " (" + (beh.Count + rs.Count) + " objects)");
                yield return null; yield return null;
            }
            string Path(Transform t) { var n = t.name; for (var q = t.parent; q != null; q = q.parent) n = q.name + "/" + n; return n; }
            report.Append("\nprobe line renderers:");
            foreach (var lr in FindObjectsOfType<LineRenderer>()) if (lr.enabled && lr.gameObject.activeInHierarchy && lr.isVisible)
            {   var pb = new MaterialPropertyBlock(); lr.GetPropertyBlock(pb);
                report.Append("\n  " + Path(lr.transform) + " mpb_Speed=" + pb.GetFloat(GridView.FlowSpeedId) + " pts=" + lr.positionCount + " w=" + lr.startWidth + " col=" + lr.startColor + " mat=" + (lr.sharedMaterial ? lr.sharedMaterial.name : "-") + (lr.positionCount > 1 ? " " + lr.GetPosition(0) + "->" + lr.GetPosition(lr.positionCount - 1) : "")); }
            report.Append("\nprobe renderers in screen box x1650-1920 y(bottom)150-330:");
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                var sp = s.viewCamera.WorldToScreenPoint(r.bounds.center);
                if (sp.z > 0 && sp.x > 1650 && sp.y > 150 && sp.y < 330) report.Append("\n  " + Path(r.transform) + " at " + r.bounds.center);
            }
            foreach (var n in new[] { "BATTLE", "DrawPileRoot" })
            {
                var go = GameObject.Find(n); if (go == null) { foreach (var rt in FindObjectsOfType<RectTransform>()) if (rt.name.StartsWith(n)) { go = rt.gameObject; break; } }
                if (go == null) continue; var c = new Vector3[4]; ((RectTransform)go.transform).GetWorldCorners(c);
                report.Append("\nrect " + n + " screen min " + c[0] + " max " + c[2]);
            }
            var all = new List<Renderer>(FindObjectsOfType<Renderer>()); var none = new List<Behaviour>(); var noR = new List<Renderer>();
            List<Renderer> R(System.Func<Renderer, bool> f) => all.FindAll(r => r.enabled && r.gameObject.activeInHierarchy && f(r));
            bool Under(Component c, string n) { for (var t = c.transform; t != null; t = t.parent) if (t.name.Contains(n)) return true; return false; }
            var canv = new List<Behaviour>(); foreach (var c in FindObjectsOfType<Canvas>()) if (c.isRootCanvas) canv.Add(c);
            foreach (var c in FindObjectsOfType<Canvas>()) if (c.isRootCanvas)
            { var one = new List<Behaviour> { c }; yield return Try("UI canvas '" + c.name + "'", one, noR); }
            yield return Try("UI all canvases", canv, noR);
            yield return Try("tower buff icons", none, R(r => r.GetComponentInParent<StoneSignal.VFX.TowerBuffIcons>() != null));
            yield return Try("resonance aura", none, R(r => Under(r, "ResonanceAura")));
            yield return Try("rune walls (MPB)", none, R(r => StoneSignal.VFX.RuneInlay.HasRune(r)));
            yield return Try("placed walls (renderers left)", none, R(r => Under(r, "Placed blocks") && !StoneSignal.VFX.RuneInlay.HasRune(r)));
            yield return Try("towers", none, R(r => r.GetComponentInParent<Tower>() != null && r.GetComponentInParent<StoneSignal.VFX.TowerBuffIcons>() == null && !Under(r, "ResonanceAura")));
            yield return Try("enemies", none, R(r => r.GetComponentInParent<Enemy>() != null));
            yield return Try("dressing", none, R(r => Under(r, "LevelDressing") || Under(r, "Dressing")));
            yield return Try("ghosts/labels", none, R(r => Under(r, "Ghost") || Under(r, "label") || Under(r, "Range")));
            yield return Try("particles/VFX", none, R(r => r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer));
            var inst = new List<Behaviour>(FindObjectsOfType<InstancedBatch>());
            yield return Try("instanced batches (ground/walls/flow)", inst, noR);
            add(report.ToString() + "\n");
        }
        void Label(string text, Vector3 at, Color color)
        {
            var go = new GameObject(text + " label"); go.transform.position = at + Vector3.up * 2.1f;
            go.transform.rotation = s.viewCamera.transform.rotation;
            var t = go.AddComponent<TMPro.TextMeshPro>(); t.text = text; t.fontSize = 3.2f; t.fontStyle = TMPro.FontStyles.Bold; t.color = color;
            t.alignment = TMPro.TextAlignmentOptions.Center; t.outlineWidth = .25f; t.outlineColor = new Color32(30, 26, 58, 255);
        }
        string Stats()
        {
            int renderers = 0, visible = 0, casters = 0; var mats = new HashSet<Material>();
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue; renderers++;
                if (!r.isVisible) continue; visible++; foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
            }
            long dc = draws.LastValue, b = batches.LastValue;
            return "source=player frame (ProfilerRecorder, " + Screen.width + "x" + Screen.height + " screen, URP quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + ")" +
                   "\ndrawCalls=" + dc + "\nbatches=" + b + "\nsetPassCalls=" + setPass.LastValue + "\ntriangles=" + tris.LastValue +
                   "\nactiveRenderers=" + renderers + "\nvisibleRenderers=" + visible + "\nvisibleShadowCasters=" + casters + "\nuniqueMaterialsVisible=" + mats.Count +
                   "\ncanvases=" + FindObjectsOfType<Canvas>().Length + "\nenemies=" + s.Enemies.Active.Count + " towers=" + s.Towers.Towers.Count +
                   "\nbudget drawCalls<150 tris<150000 -> " + (dc > 0 && dc < 150 && tris.LastValue < 150000 ? "OK" : "OVER") + "\n";
        }
        void Capture()
        {
            var cam = s.viewCamera; var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var canvases = FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { modes[i] = canvases[i].renderMode; canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = cam; canvases[i].planeDistance = cam.nearClipPlane + .3f - canvases[i].sortingOrder * .05f; }
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); Canvas.ForceUpdateCanvases(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
        }
        void OnDestroy() { draws.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); }
    }
}
#endif
