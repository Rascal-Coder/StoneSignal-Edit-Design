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
            for (int k = 0; k < sets.Length; k++)
            {
                if (!ui.DebugShowRewardPick(sets[k])) { info += "reward pick UI unavailable\n"; break; }
                float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < .8f) yield return null; // glow fade-in done
                for (int f = 0; f < 4; f++) yield return null; long dOn = draws.LastValue, bOn = batches.LastValue;
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_glow_" + (k == 0 ? "CRE" : "REL") + ".png"));
                ui.PickGlow.enabled = false; for (int f = 0; f < 6; f++) yield return null; long dOff = draws.LastValue, bOff = batches.LastValue;
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_noglow_" + (k == 0 ? "CRE" : "REL") + ".png"));
                ui.PickGlow.enabled = true; for (int f = 0; f < 4; f++) yield return null;
                info += "set " + (k == 0 ? "Common,Rare,Epic" : "Rare,Epic,Legendary") + ": glow ON drawCalls=" + dOn + " batches=" + bOn + " | glow OFF drawCalls=" + dOff + " batches=" + bOff + " | delta DC=" + (dOn - dOff) + " batches=" + (bOn - bOff) + "\n";
            }
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
            int bottom = -1, left = -1;
            for (int i = 0; i < s.grid.Spawns.Count; i++) { var e = s.grid.Spawns[i]; if (e.y == 0) bottom = i; else if (e.x == 0) left = i; }
            foreach (var run in new[] { ("drifter_bottom_bridge", drifter, bottom), ("skimmer_left_bridge", skimmer, left) })
            {
                if (run.Item2 == null || run.Item3 < 0) { info += run.Item1 + ": data/spawn missing\n"; continue; }
                if (s.Game.State != GameState.Combat) info += "(state " + s.Game.State + " -> StartWave=" + s.Waves.StartWave() + ")\n"; // enemies only move in Combat
                var entry = s.grid.Spawns[run.Item3]; var en = s.Enemies.Spawn(run.Item2, 40, 1, run.Item3);
                float t0 = Time.realtimeSinceStartup; var bridge = s.grid.BridgeWaterPoint(entry);
                // follow the enemy once it reaches the bridge; frames while it crosses toward the board
                float shotAt = -1; int k = 0;
                while (Time.realtimeSinceStartup - t0 < 9f && k < 3 && en != null && en.Alive)
                {
                    var p = en.transform.position; float along = Vector3.Dot(p - bridge, -GridManager.OutwardOf(entry, s.grid.width, s.grid.height));
                    if (shotAt < 0 && along > -.4f) shotAt = Time.realtimeSinceStartup;
                    if (shotAt >= 0 && Time.realtimeSinceStartup - shotAt >= k * .45f)
                    {
                        cam.transform.position = p - cam.transform.forward * 6f; yield return null; yield return new WaitForEndOfFrame();
                        long dc = draws.LastValue; Capture(Path.Combine(dir, run.Item1 + "_" + k + ".png"));
                        info += run.Item1 + "_" + k + ": " + Describe(en) + " | frame drawCalls=" + dc + "\n" + Prints(en) + "\n";
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
                if (near++ < 6) sb.Append(" [" + wp.x.ToString("F2") + "," + wp.y.ToString("F2") + "," + wp.z.ToString("F2") + " rot=" + ps[i].rotation.ToString("F0") + " age=" + (ps[i].startLifetime - ps[i].remainingLifetime).ToString("F2") + "]");
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
        bool captureNoUi;
        void Capture(string path)
        {
            var cam = s.viewCamera; var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var hidden = new List<Canvas>(); // URP draws overlay UI into the camera target itself, so hide canvases synchronously around Render
            if (captureNoUi) foreach (var c in FindObjectsOfType<Canvas>()) if (c.enabled) { c.enabled = false; hidden.Add(c); }
            var canvases = captureNoUi ? new Canvas[0] : FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { modes[i] = canvases[i].renderMode; canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = cam; canvases[i].planeDistance = cam.nearClipPlane + .3f - Mathf.Clamp(canvases[i].sortingOrder, 0, 20) * .01f; } /* was -order*.05: order-20 reward canvas went behind the near plane */
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render(); Canvas.ForceUpdateCanvases(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
            foreach (var c in hidden) c.enabled = true;
        }
        void OnDestroy() { draws.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); }
    }
}
#endif
