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
            TimeController.ResetAll();
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            string stats = Stats() + Layout();
            Capture();
            yield return Breakdown(r => stats += r);
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), stats);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-portalcloseups") >= 0) yield return PortalCloseups();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-groundshot") >= 0) yield return GroundShot();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-stepshots") >= 0) yield return StepShots();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vfxcheck") >= 0) yield return VfxCheck(valid, fv);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rewardshot") >= 0) yield return RewardShots();
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
            yield return Try("ghosts/labels", none, R(r => Under(r, "Ghost") || Under(r, "Range")));
            yield return Try("particles/VFX", none, R(r => r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer));
            var inst = new List<Behaviour>(FindObjectsOfType<InstancedBatch>());
            yield return Try("instanced batches (ground/walls/flow)", inst, noR);
            add(report.ToString() + "\n");
        }
        // Runtime UI geometry in screen pixels (overlay canvases: world corners = screen px), taken before the capture switches modes.
        string Layout()
        {
            var sb = new System.Text.StringBuilder("\nlayout screen=" + Screen.width + "x" + Screen.height + " safeArea=" + Screen.safeArea);
            foreach (var c in FindObjectsOfType<Canvas>()) if (c.isRootCanvas)
            { var sc = c.GetComponent<UnityEngine.UI.CanvasScaler>(); sb.Append("\n  canvas " + c.name + " mode=" + c.renderMode + " scaleFactor=" + c.scaleFactor + (sc ? " scaler=" + sc.uiScaleMode + " ref=" + sc.referenceResolution : "")); }
            foreach (var rt in FindObjectsOfType<RectTransform>())
            {
                if (rt.name != "BATTLE" && rt.name != "DrawPileRoot" && rt.name != "SafeAreaRoot" && !rt.name.StartsWith("Tower card")) continue;
                var k = new Vector3[4]; rt.GetWorldCorners(k);
                sb.Append("\n  " + rt.name + " parent=" + (rt.parent ? rt.parent.name : "-") + " x " + k[0].x.ToString("0") + ".." + k[2].x.ToString("0") + " y " + k[0].y.ToString("0") + ".." + k[2].y.ToString("0") +
                          " (right margin " + (Screen.width - k[2].x).ToString("0") + ", bottom " + k[0].y.ToString("0") + ") rotZ=" + rt.localEulerAngles.z.ToString("0.0") + " pos=" + rt.anchoredPosition);
            }
            return sb.ToString() + "\n";
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
        void Capture() => Capture(path);
        // -rewardshot: forced reward cards (C,R,E then R,E,L), glow on/off draw-call + batch delta, mid-pick frame.
        IEnumerator RewardShots()
        {
            var ui = FindObjectOfType<GameUI>(); if (ui == null) yield break;
            string dir = Path.GetDirectoryName(path), info = "";
            var sets = new[] { new[] { StoneSignal.UI.RewardRarity.Common, StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic },
                               new[] { StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic, StoneSignal.UI.RewardRarity.Legendary } };
            TimeController.SetSpeed(0);
            info += TextAudit("HUD");
            for (int k = 0; k < sets.Length; k++)
            {
                if (!ui.DebugShowRewardPick(sets[k])) { info += "reward pick UI unavailable\n"; break; }
                float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < .8f) yield return null; // glow fade-in done
                for (int f = 0; f < 4; f++) yield return null; long dOn = draws.LastValue, bOn = batches.LastValue;
                info += TextAudit("reward cards " + (k == 0 ? "C,R,E" : "R,E,L"));
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_glow_" + (k == 0 ? "CRE" : "REL") + ".png"));
                ui.PickGlow.enabled = false; for (int f = 0; f < 6; f++) yield return null; long dOff = draws.LastValue, bOff = batches.LastValue;
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_noglow_" + (k == 0 ? "CRE" : "REL") + ".png"));
                ui.PickGlow.enabled = true; for (int f = 0; f < 4; f++) yield return null;
                info += "set " + (k == 0 ? "Common,Rare,Epic" : "Rare,Epic,Legendary") + ": glow ON drawCalls=" + dOn + " batches=" + bOn + " | glow OFF drawCalls=" + dOff + " batches=" + bOff + " | delta DC=" + (dOn - dOff) + " batches=" + (bOn - bOff) + "\n";
            }
            // Legendary +1 batch probe: batch / draw-call range over 2.5 s (two 1.2 s legendary pulses) per variant
            string probe = "legendary batch probe (min..max over 2.5 s, " + Screen.width + "x" + Screen.height + "):\n";
            IEnumerator Range(string name)
            {
                long bMin = long.MaxValue, bMax = 0, dMin = long.MaxValue, dMax = 0; for (int f = 0; f < 4; f++) yield return null;
                float r0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - r0 < 2.5f) { long b = batches.LastValue, d = draws.LastValue; if (b > 0) { bMin = System.Math.Min(bMin, b); bMax = System.Math.Max(bMax, b); dMin = System.Math.Min(dMin, d); dMax = System.Math.Max(dMax, d); } yield return null; }
                probe += "  " + name + ": batches " + bMin + ".." + bMax + " drawCalls " + dMin + ".." + dMax + "\n";
            }
            void Act(string n, bool on) { var t = ui.PickGlow.transform.Find(n); if (t == null) t = ui.PickUi.transform.Find(n); if (t != null) t.gameObject.SetActive(on); }
            var rel = sets[1];
            ui.DebugShowRewardPick(rel); ui.PickGlow.enabled = true; yield return Range("R,E,L all glows on");
            ui.PickGlow.enabled = false; yield return Range("R,E,L glows off"); ui.PickGlow.enabled = true;
            Act("Glow2", false); yield return Range("R,E,L legendary glow hidden"); Act("Glow2", true);
            Act("Glow0", false); Act("Glow1", false); yield return Range("R,E,L only legendary glow"); Act("Glow0", true); Act("Glow1", true);
            for (int k = 0; k < 3; k++) Act("RewardCard2/Sparkle" + k, false); yield return Range("R,E,L legendary sparkles hidden"); for (int k = 0; k < 3; k++) Act("RewardCard2/Sparkle" + k, true);
            ui.PickGlow.enabled = false; for (int k = 0; k < 3; k++) Act("RewardCard2/Sparkle" + k, false); yield return Range("R,E,L glows off + sparkles hidden"); ui.PickGlow.enabled = true;
            ui.DebugShowRewardPick(new[] { StoneSignal.UI.RewardRarity.Legendary, StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic }); yield return Range("L,R,E all glows on");
            ui.PickGlow.enabled = false; yield return Range("L,R,E glows off"); ui.PickGlow.enabled = true;
            ui.DebugShowRewardPick(sets[0]); yield return Range("C,R,E all glows on");
            ui.PickGlow.enabled = false; yield return Range("C,R,E glows off"); ui.PickGlow.enabled = true;
            // v18: RewardGlow now orders all glows below all cards; re-measure the old interleaved order (Glow_i directly below Card_i) for comparison
            void Interleave() { for (int k = 0; k < 3; k++) { var g = ui.PickGlow.transform.Find("Glow" + k); var c = ui.PickGlow.transform.Find("RewardCard" + k); if (g != null && c != null) { g.SetAsLastSibling(); g.SetSiblingIndex(c.GetSiblingIndex()); } } }
            ui.DebugShowRewardPick(new[] { StoneSignal.UI.RewardRarity.Legendary, StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic }); Interleave(); yield return Range("L,R,E old interleaved order");
            ui.DebugShowRewardPick(rel); Interleave(); yield return Range("R,E,L old interleaved order");
            ui.DebugShowRewardPick(new[] { StoneSignal.UI.RewardRarity.Legendary, StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic }); yield return Range("L,R,E glows-first again (Begin re-orders)");
            ui.DebugShowRewardPick(rel); for (int f = 0; f < 30; f++) yield return null;
            info += probe;
            info += CnAtlas();
            // mid-pick: legendary card (index 2) flash
            ui.PickUi.PlayPick(2, new Vector2(Screen.width * .5f, Screen.height * .12f), null); ui.PickGlow.Pick(2);
            float p0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - p0 < .1f) yield return null;
            yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_glow_pick.png"));
            File.WriteAllText(Path.Combine(dir, "reward_glow.txt"), info); Debug.Log("REWARD SHOTS\n" + info);
        }
        // -groundshot (v17.2): enemy ground pass. A Drifter on the bottom bridge (heading +z) and a Skimmer on the left bridge (heading +x)
        // close-ups with UI hidden: one ground shadow, footprints under the feet and aligned to the heading (straight z vs x segment checks
        // footprintYawOffset / footprintYawSign), HP bar above the model. Also logs Bloom (volume + camera post-processing).
        IEnumerator GroundShot()
        {
            string dir = Path.GetDirectoryName(path), info = "";
            EnemyData skimmer = null, drifter = null;
            foreach (var d in Resources.FindObjectsOfTypeAll<EnemyData>()) { if (d.name == "Skimmer") skimmer = d; if (d.name == "Drifter") drifter = d; }
            foreach (var d in Resources.FindObjectsOfTypeAll<EnemyData>()) if (d.flying) info += "FLYING ENEMY DATA: " + d.name + " (expected none in this version)\n";
            info += Bloom();
            TimeController.ResetAll(); captureNoUi = true;
            var cam = s.viewCamera; var home = cam.transform.position; float ortho = cam.orthographicSize, fov = cam.fieldOfView;
            if (cam.orthographic) cam.orthographicSize = ortho * .38f; else cam.fieldOfView = fov * .42f;
            int bottom = -1, left = -1, right = -1;
            for (int i = 0; i < s.grid.Spawns.Count; i++) { var e = s.grid.Spawns[i]; if (e.y == 0) bottom = i; else if (e.x == 0) left = i; else if (e.x == s.grid.width - 1) right = i; }
            info += "WalkSurface tiles=" + WalkSurface.TileCount + " profiles=" + WalkSurface.ProfileCount + "\n";
            foreach (var run in new[] { ("drifter_bottom_bridge", drifter, bottom), ("skimmer_left_bridge", skimmer, left), ("drifter_right_bridge", drifter, right) })
            {
                if (run.Item2 == null || run.Item3 < 0) { info += run.Item1 + ": data/spawn missing\n"; continue; }
                if (s.Game.State != GameState.Combat) { var was = s.Game.State; ToBuild(); info += "(state " + was + " -> " + s.Game.State + " -> StartWave=" + s.Waves.StartWave() + ")\n"; } // enemies only move in Combat
                var entry = s.grid.Spawns[run.Item3]; var en = s.Enemies.Spawn(run.Item2, 40, 1, run.Item3);
                float t0 = Time.realtimeSinceStartup; var bridge = s.grid.BridgeWaterPoint(entry);
                // follow the enemy once it reaches the bridge; frames while it crosses toward the board
                float shotAt = -1; int k = 0;
                while (Time.realtimeSinceStartup - t0 < 12f && k < 5 && en != null && en.Alive)
                {
                    var p = en.transform.position; float along = Vector3.Dot(p - bridge, -GridManager.OutwardOf(entry, s.grid.width, s.grid.height));
                    if (shotAt < 0 && !en.HeldBySpawn && along > -2.2f) shotAt = Time.realtimeSinceStartup; // v17.3: from the island end of the bridge onto the tiles
                    if (shotAt >= 0 && Time.realtimeSinceStartup - shotAt >= k * .7f)
                    {
                        cam.transform.position = p - cam.transform.forward * 6f; yield return null; yield return new WaitForEndOfFrame();
                        long dc = draws.LastValue; Capture(Path.Combine(dir, run.Item1 + "_" + k + ".png"));
                        var cell = s.grid.ToCell(p); string where = s.grid.InBounds(cell) ? "tiles" : along > -.4f ? "bridge" : "island/bridge start";
                        info += run.Item1 + "_" + k + " [" + where + "]: " + Describe(en) + " | frame drawCalls=" + dc + "\n" + Prints(en) + "\n";
                        k++;
                    }
                    else yield return null;
                }
                if (k == 0) info += run.Item1 + ": enemy never reached the bridge (alive=" + (en != null && en.Alive) + " pos=" + (en != null ? en.transform.position.ToString("F2") : "-") + " state=" + s.Game.State + " held=" + (en != null && en.HeldBySpawn) + ")\n";
            }
            cam.transform.position = home; cam.orthographicSize = ortho; cam.fieldOfView = fov; captureNoUi = false;
            File.WriteAllText(Path.Combine(dir, "ground_shot.txt"), info); Debug.Log("GROUND SHOT\n" + info);
        }
        string Describe(Enemy en)
        {
            int gfx = 0, gfxActive = 0, fm = 0, fmActive = 0, blobs = 0; string blobY = "";
            foreach (var g in en.GetComponentsInChildren<StoneSignal.VFX.EnemyGroundFx>(true)) { gfx++; if (g.isActiveAndEnabled) gfxActive++; }
            foreach (var f in en.GetComponentsInChildren<StoneSignal.VFX.FlyingMotion>(true)) { fm++; if (f.isActiveAndEnabled) fmActive++; }
            foreach (var r in en.GetComponentsInChildren<Renderer>()) if (r.enabled && r.name.StartsWith("Blob")) { blobs++; blobY += " " + r.bounds.center.y.ToString("F2"); }
            Transform bar = null; float best = 9;
            foreach (Transform ch in s.Enemies.transform) if (ch.name == "HP background" && ch.gameObject.activeInHierarchy) { var dd = new Vector2(ch.position.x - en.transform.position.x, ch.position.z - en.transform.position.z).magnitude; if (dd < best) { best = dd; bar = ch; } }
            float top = VisualTop(en);
            return en.name + " root y=" + en.transform.position.y.ToString("F2") + " yaw=" + en.transform.eulerAngles.y.ToString("F0") + " EnemyGroundFx " + gfxActive + "/" + gfx + " active, FlyingMotion " + fmActive + "/" + fm +
                   " active, visible blobs=" + blobs + " (y" + blobY + "), model top y=" + top.ToString("F2") + ", HP bar y=" + (bar ? bar.position.y.ToString("F2") + (bar.position.y > top ? " (above model)" : " (BELOW model top)") : "none");
        }
        string Prints(Enemy en)
        {
            var sys = StoneSignal.VFX.EnemyGroundFxSystem.Instance; if (sys == null || sys.dust == null) return "    footprints: no EnemyGroundFxSystem";
            var ps = new ParticleSystem.Particle[StoneSignal.VFX.EnemyGroundFxSystem.MaxFootprints]; int n = sys.dust.GetParticles(ps), near = 0; var sb = new System.Text.StringBuilder();
            bool local = sys.dust.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            for (int i = 0; i < n; i++)
            {
                var wp = local ? sys.dust.transform.TransformPoint(ps[i].position) : ps[i].position;
                if ((new Vector2(wp.x - en.transform.position.x, wp.z - en.transform.position.z)).magnitude > 2.5f) continue;
                if (near++ < 6) sb.Append(" [" + wp.x.ToString("F2") + "," + wp.y.ToString("F2") + "," + wp.z.ToString("F2") + " surf=" + (WalkSurface.TryGet(wp, out var sy) ? sy.ToString("F2") : "-") + " rot=" + ps[i].rotation.ToString("F0") + " age=" + (ps[i].startLifetime - ps[i].remainingLifetime).ToString("F2") + "]");
            }
            var r = sys.dust.GetComponent<ParticleSystemRenderer>();
            return "    footprints total=" + n + " near enemy=" + near + " align=" + (r ? r.alignment.ToString() + " renderMode=" + r.renderMode : "-") + " yawOffset=" + sys.footprintYawOffset + " yawSign=" + sys.footprintYawSign + sb;
        }
        static string Bloom()
        {
            string o = "BLOOM:";
            foreach (var v in FindObjectsOfType<UnityEngine.Rendering.Volume>())
            {
                var prof = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                UnityEngine.Rendering.Universal.Bloom bl = null; if (prof != null) prof.TryGet(out bl);
                o += " volume '" + v.name + "' global=" + v.isGlobal + " weight=" + v.weight + " profile=" + (prof ? prof.name : "none") + " bloom=" + (bl != null ? "active=" + bl.active + " intensity=" + bl.intensity.value + " (override " + bl.intensity.overrideState + ") threshold=" + bl.threshold.value : "NOT IN PROFILE") + ";";
            }
            foreach (var c in FindObjectsOfType<Camera>())
            {
                var ad = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                o += " camera '" + c.name + "' renderPostProcessing=" + (ad ? ad.renderPostProcessing.ToString() : "no URP data") + " hdr=" + c.allowHDR + ";";
            }
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            o += " pipeline=" + (rp ? rp.name : "none") + (rp is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset ua ? " hdrAsset=" + ua.supportsHDR : "");
            return o + "\n";
        }
        static long Tris(Mesh m) { long t = 0; for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t; }
        static float VisualTop(Component en) { float t = float.MinValue; foreach (var r in StoneSignal.Enemy.ModelRenderers(en.transform)) t = Mathf.Max(t, StoneSignal.Enemy.MeshTop(r)); return t; }
        static string RendererTops(Component en) { var sb = new System.Text.StringBuilder(); foreach (var r in en.GetComponentsInChildren<Renderer>()) sb.Append("\n    " + r.GetType().Name + " " + r.name + " enabled=" + r.enabled + " boundsTop=" + r.bounds.max.y.ToString("F2") + " meshTop=" + StoneSignal.Enemy.MeshTop(r).ToString("F2")); return sb.ToString(); }
        // -vfxcheck (v17.3 white squares): block landing dust (M_FX_Snow), enemy status FX (M_VFX_PortalEmber), and every particle
        // material in the scene: texture + surface. Close-ups with UI hidden.
        bool ToBuild() { if (s.Game.State == GameState.Reward && !s.Rewards.Choose(0)) s.Game.SetState(GameState.Build); return s.Game.State == GameState.Build; }
        IEnumerator VfxCheck(Vector2Int valid, bool haveValid)
        {
            string dir = Path.GetDirectoryName(path), info = "";
            var cam = s.viewCamera; var home = cam.transform.position; float ortho = cam.orthographicSize, fov = cam.fieldOfView;
            void Zoom(float k) { if (cam.orthographic) cam.orthographicSize = ortho * k; else cam.fieldOfView = fov * k; }
            captureNoUi = true; TimeController.ResetAll();
            // 1. block landing dust (placement needs the Build state: earlier capture steps may have ended the wave)
            if (s.Game.State != GameState.Build) { var was = s.Game.State; ToBuild(); info += "state " + was + " -> " + s.Game.State + "\n"; }
            s.Blocks.enabled = true; s.Blocks.Pinned = false;
            if (s.Blocks.Hand.Cards.Count == 0) s.Blocks.DrawCards(1);
            s.Blocks.SelectCard(0); haveValid = false;
            for (int y = 2; y < s.grid.height - 2 && !haveValid; y++) for (int x = 2; x < s.grid.width - 2 && !haveValid; x++) { var c = new Vector2Int(x, y); if (s.Blocks.CurrentShape != null && s.Blocks.ValidatePlacement(c) == null) { valid = c; haveValid = true; } }
            if (haveValid)
            {
                var at = s.grid.ToWorld(valid); cam.transform.position = at - cam.transform.forward * 6f; Zoom(.32f);
                s.Blocks.Pinned = false; bool ok = s.Blocks.CommitPlacement(valid); float t0 = Time.realtimeSinceStartup;
                foreach (var t in new[] { .06f, .16f, .32f })
                {
                    while (Time.realtimeSinceStartup - t0 < t) yield return null;
                    long d = draws.LastValue; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "vfx_landing_dust_t" + Mathf.RoundToInt(t * 100).ToString("00") + ".png"));
                    info += "landing dust t=" + t + " committed=" + ok + " drawCalls=" + d + "\n";
                }
            }
            else info += "landing dust: no valid block placement available\n";
            // 2. status FX on a live enemy (all five statuses)
            Enemy target = null; foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn && s.grid.InBounds(s.grid.ToCell(e.transform.position))) { target = e; break; }
            if (target == null && s.Enemies.Active.Count > 0) target = s.Enemies.Active[0];
            if (target != null)
            {
                var sfx = target.GetComponentInChildren<StoneSignal.VFX.EnemyStatusFx>(true);
                if (sfx == null) info += "status fx: enemy has no EnemyStatusFx\n";
                else
                {
                    TimeController.SetSpeed(0);
                    foreach (StoneSignal.VFX.StatusId id in System.Enum.GetValues(typeof(StoneSignal.VFX.StatusId))) sfx.Apply(id, 30f);
                    var tp = target.transform.position; cam.transform.position = tp - cam.transform.forward * 6f; Zoom(.22f);
                    TimeController.ResetAll(); TimeController.SetSpeed(1);
                    float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < .9f) { cam.transform.position = target.transform.position - cam.transform.forward * 6f; yield return null; }
                    TimeController.SetPaused(true); yield return null; yield return null;
                    long d = draws.LastValue; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "vfx_status_all.png"));
                    info += "status fx: all 5 on " + target.name + " drawCalls=" + d + "\n"; TimeController.ResetAll(); sfx.ClearAll();
                }
            }
            else info += "status fx: no enemy\n";
            // 3. particle materials actually used in the scene
            var seen = new HashSet<Material>();
            foreach (var r in FindObjectsOfType<ParticleSystemRenderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    string surf = m.HasProperty("_Surface") ? (m.GetFloat("_Surface") > .5f ? "transparent" : "OPAQUE") : "queue " + m.renderQueue;
                    bool bad = tex == null || surf == "OPAQUE";
                    info += (bad ? "  PARTICLE MATERIAL PROBLEM " : "  particle material ") + m.name + " shader=" + m.shader.name + " tex=" + (tex ? tex.name : "NONE") + " " + surf + " queue=" + m.renderQueue + " (e.g. " + r.name + ")\n";
                }
            cam.transform.position = home; cam.orthographicSize = ortho; cam.fieldOfView = fov; captureNoUi = false;
            File.WriteAllText(Path.Combine(dir, "vfx_check.txt"), info); Debug.Log("VFX CHECK\n" + info);
        }
        // -stepshots: art step 2 (SpawnRipple, shader-only) and step 3 (core enclosure Intact/Cracked/Broken/Critical + CoreDamageFx hit),
        // close-ups with UI hidden, draw calls per frame and the enclosure's own draw-call cost (renderer toggled off, profiler delta).
        IEnumerator StepShots()
        {
            string dir = Path.GetDirectoryName(path), info = "";
            var cam = s.viewCamera; var home = cam.transform.position; float ortho = cam.orthographicSize, fov = cam.fieldOfView;
            void Zoom(float k) { if (cam.orthographic) cam.orthographicSize = ortho * k; else cam.fieldOfView = fov * k; }
            captureNoUi = true; TimeController.ResetAll();
            // step 2: ripple on the open water under the bottom entry's bridge
            int si = s.grid.Spawns.Count - 1; var e0 = s.grid.Spawns[si]; var wp = s.grid.BridgeWaterPoint(e0);
            cam.transform.position = wp - cam.transform.forward * 6f; Zoom(.5f);
            for (int f = 0; f < 6; f++) yield return null; long dBase = draws.LastValue;
            info += "STEP 2 SpawnRipple at bridge water " + wp.ToString("F2") + " (entry " + e0 + "), baseline drawCalls=" + dBase + "\n";
            var en = s.Enemies.Spawn(s.config.waves[0].groups[0].enemy, 30, 1, si); float t0 = Time.realtimeSinceStartup;
            foreach (var at in new[] { .35f, .8f, 1.2f, 2.0f })
            {
                while (Time.realtimeSinceStartup - t0 < at) yield return null;
                long d = draws.LastValue, b = batches.LastValue; yield return new WaitForEndOfFrame();
                Capture(Path.Combine(dir, "ripple_t" + Mathf.RoundToInt(at * 100).ToString("000") + ".png"));
                info += "  t=" + at + (at < 1.6f ? " (ripple alive)" : " (ripple over)") + " drawCalls=" + d + " batches=" + b + "\n";
            }
            // step 3: core enclosure states
            var fx = s.MapView != null ? s.MapView.CoreFx : null;
            if (fx == null || fx.enclosure == null) info += "STEP 3: CoreDamageFx / enclosure MISSING\n";
            else
            {
                TimeController.SetSpeed(0);
                var encR = fx.enclosure.GetComponent<MeshRenderer>(); var core = s.grid.CoreCenter;
                cam.transform.position = core - cam.transform.forward * 6f; Zoom(.4f);
                info += "STEP 3 core enclosure: material=" + (encR.sharedMaterial ? encR.sharedMaterial.name + " shader=" + encR.sharedMaterial.shader.name : "NONE") + " coreRenderers=" + fx.coreRenderers.Length + " smoke=" + (fx.smoke ? "yes" : "none") + " sparks=" + (fx.sparks ? "yes" : "none") + "\n";
                foreach (var st in new[] { (1f, "intact"), (.55f, "cracked"), (.3f, "broken"), (.1f, "critical") })
                {
                    fx.SetHealth01(st.Item1); for (int f = 0; f < 6; f++) yield return null; long d = draws.LastValue, b = batches.LastValue;
                    yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "core_" + st.Item2 + ".png"));
                    encR.enabled = false; for (int f = 0; f < 4; f++) yield return null; long dOff = draws.LastValue; encR.enabled = true;
                    var coreOff = new List<Renderer>(); foreach (var r in fx.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; coreOff.Add(r); }
                    for (int f = 0; f < 4; f++) yield return null; long dNoCore = draws.LastValue; foreach (var r in coreOff) r.enabled = true;
                    var m = fx.enclosure.sharedMesh;
                    info += "  " + st.Item2 + " (h=" + st.Item1 + ") stage=" + fx.Current + " mesh=" + (m ? m.name + " tris=" + Tris(m) + " submeshes=" + m.subMeshCount : "-") +
                            " | frame drawCalls=" + d + " batches=" + b + " | enclosure DC=" + (d - dOff) + " | whole core group DC=" + (d - dNoCore) + " (" + coreOff.Count + " renderers)\n";
                }
                fx.SetHealth01(1); fx.PlayHit(); yield return null; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "core_hit.png")); info += "  hit flash captured (PlayHit, intact)\n";
                TimeController.ResetAll();
            }
            cam.transform.position = home; cam.orthographicSize = ortho; cam.fieldOfView = fov; captureNoUi = false;
            File.WriteAllText(Path.Combine(dir, "step_shots.txt"), info); Debug.Log("STEP SHOTS\n" + info);
        }
        // -portalcloseups: after the main shot, one close-up per spawn portal and one mid-spawn (UI hidden), next to the main png.
        IEnumerator PortalCloseups()
        {
            RenderDiag.Log("shot");
            var cam = s.viewCamera; var home = cam.transform.position; var canvases = FindObjectsOfType<Canvas>();
            foreach (var c in canvases) c.enabled = false; captureNoUi = true;
            string dir = Path.GetDirectoryName(path), info = "";
            for (int i = 0; i < s.grid.Spawns.Count; i++)
            {
                var e = s.grid.Spawns[i]; if (!s.grid.TryPortalPoint(e, out var p)) continue;
                cam.transform.position = p - cam.transform.forward * 7f; yield return null; yield return new WaitForEndOfFrame();
                Capture(Path.Combine(dir, "portal_" + e.x + "_" + e.y + ".png")); info += "portal " + e + " at " + p.ToString("F2") + "\n";
            }
            int si = s.grid.Spawns.Count - 1; var e0 = s.grid.Spawns[si]; s.grid.TryPortalPoint(e0, out var p0); // bottom entry: not under trees
            cam.transform.position = p0 - cam.transform.forward * 6f; float o0 = cam.orthographicSize, f0 = cam.fieldOfView;
            if (cam.orthographic) cam.orthographicSize = o0 * .45f; else cam.fieldOfView = f0 * .5f; // mid-spawn frames as a real close-up
            var en = s.Enemies.Spawn(s.config.waves[0].groups[0].enemy, 1, 1, si);
            float t0 = Time.realtimeSinceStartup;
            foreach (var at in new[] { .15f, .35f, .7f, .95f }) // .6 s rise, then the HP bar eases in over Enemy.BarFadeTime
            {
                while (Time.realtimeSinceStartup - t0 < at) yield return null;
                yield return new WaitForEndOfFrame();
                Capture(Path.Combine(dir, "portal_spawn_t" + Mathf.RoundToInt(at * 100).ToString("000") + ".png"));
                info += "t=" + at + " enemy at " + (en != null ? en.transform.position.ToString("F2") : "-") + " held=" + (en != null && en.HeldBySpawn) + " hpBarVisible=" + (en != null && en.HpBarVisible) + "\n";
            }
            cam.orthographicSize = o0; cam.fieldOfView = f0;
            File.WriteAllText(Path.Combine(dir, "portal_closeups.txt"), info); Debug.Log("PORTAL CLOSEUPS\n" + info);
            cam.transform.position = home; foreach (var c in canvases) c.enabled = true; captureNoUi = false;
        }
        // v18 CN UI: every visible TMP text with its fit (size / lines / rect vs preferred), Latin letters and glyphs missing from font + fallbacks.
        string TextAudit(string label)
        {
            var sb = new System.Text.StringBuilder("text audit (" + label + ", " + Screen.width + "x" + Screen.height + "):\n");
            foreach (var t in FindObjectsOfType<TMPro.TMP_Text>())
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.canvas == null || !t.canvas.enabled || t.alpha <= 0) continue;
                t.ForceMeshUpdate(); var r = t.rectTransform.rect; bool latin = false;
                foreach (char c in t.text) if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin = true;
                uint[] miss = null; bool missing = t.font != null && !t.font.HasCharacters(t.text, out miss, true, true) && miss != null && miss.Length > 0;
                string mc = ""; if (missing) foreach (var u in miss) mc += char.ConvertFromUtf32((int)u);
                bool wide = !t.enableWordWrapping && t.preferredWidth > r.width + 1, tall = t.preferredHeight > r.height + 1;
                string path = t.name; for (var p = t.transform.parent; p != null && p.GetComponent<Canvas>() == null; p = p.parent) path = p.name + "/" + path;
                sb.Append("  ").Append(path).Append(" '").Append(t.text.Replace("\n", "\\n")).Append("' font=").Append(t.fontSize.ToString("0.#"))
                  .Append(t.enableAutoSizing ? " (auto " + t.fontSizeMin.ToString("0") + "-" + t.fontSizeMax.ToString("0") + ")" : "").Append(" lines=").Append(t.textInfo.lineCount)
                  .Append(" rect=").Append(r.width.ToString("0")).Append("x").Append(r.height.ToString("0")).Append(" pref=").Append(t.preferredWidth.ToString("0")).Append("x").Append(t.preferredHeight.ToString("0"))
                  .Append(wide ? " WIDER-THAN-RECT" : "").Append(tall ? " TALLER-THAN-RECT" : "").Append(t.isTextOverflowing ? " OVERFLOW" : "").Append(latin ? " LATIN" : "").Append(missing ? " MISSING[" + mc + "]" : "").Append('\n');
            }
            return sb.ToString();
        }
        // CN font dynamic atlas after adding every UI string (Loc.All + all reward titles/descriptions): pages, glyphs, missing chars.
        string CnAtlas()
        {
            TMPro.TMP_FontAsset cn = null; foreach (var f in TMPro.TMP_Settings.fallbackFontAssets) if (f != null && f.name.Contains("RoundedCN")) cn = f;
            if (cn == null) return "CN font: not in TMP Settings fallbacks\n";
            var all = new System.Text.StringBuilder(); foreach (var str in StoneSignal.UI.Loc.All()) all.Append(str);
            foreach (var r in s.config.rewards) if (r != null) all.Append(StoneSignal.UI.Loc.RewardTitle(r)).Append(StoneSignal.UI.Loc.RewardDesc(r));
            var set = new HashSet<char>(); foreach (char c in all.ToString()) if (c > 0x2E7F) set.Add(c);
            var uniq = new string(new List<char>(set).ToArray()); cn.TryAddCharacters(uniq, out string missing);
            int pages = 0; if (cn.atlasTextures != null) foreach (var tx in cn.atlasTextures) if (tx != null) pages++;
            return "CN font " + cn.name + ": unique CJK/fullwidth chars in UI strings=" + uniq.Length + " glyphs in font now=" + cn.characterTable.Count +
                   " atlas pages=" + pages + " (" + cn.atlasWidth + "x" + cn.atlasHeight + ", padding " + cn.atlasPadding + ", sampling " + cn.faceInfo.pointSize + ") multiAtlas=" + cn.isMultiAtlasTexturesEnabled +
                   " missing=[" + missing + "]\n";
        }
        bool captureNoUi;
        void Capture(string path)
        {
            var cam = s.viewCamera; int W = Screen.width, H = Screen.height; var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 }; // screen size: overlay UI layout matches the real screen
            var hidden = new List<Canvas>(); // URP draws overlay UI into the camera target itself, so hide canvases synchronously around Render
            if (captureNoUi) foreach (var c in FindObjectsOfType<Canvas>()) if (c.enabled) { c.enabled = false; hidden.Add(c); }
            var canvases = captureNoUi ? new Canvas[0] : FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { modes[i] = canvases[i].renderMode; canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = cam; canvases[i].planeDistance = cam.nearClipPlane + .3f - Mathf.Clamp(canvases[i].sortingOrder, 0, 20) * .01f; } /* was -order*.05: order-20 reward canvas went behind the near plane */
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); Canvas.ForceUpdateCanvases(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; rt.Release(); Destroy(rt); Destroy(tex);
            for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
            foreach (var c in hidden) c.enabled = true;
        }
        void OnDestroy() { draws.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); }
    }
}
#endif
