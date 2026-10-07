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
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cnshots") >= 0) { yield return CnShots(); Debug.Log("CN SHOTS DONE " + path); Application.Quit(0); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-toastshots") >= 0) { yield return ToastShots(); Debug.Log("TOAST SHOTS DONE " + path); Application.Quit(0); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-camcompare") >= 0) { yield return CamCompare(); Debug.Log("CAM COMPARE DONE " + path); Application.Quit(0); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-dragthrough") >= 0) { yield return DragThrough(); Debug.Log("DRAG THROUGH DONE " + path); Application.Quit(0); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cardtext") >= 0) { yield return CardText(); Debug.Log("CARD TEXT DONE " + path); Application.Quit(0); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hudshots") >= 0) { yield return HudShots(); Debug.Log("HUD SHOTS DONE " + path); Application.Quit(0); yield break; }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cnshots3") >= 0) { yield return CnShots3(); Debug.Log("CN SHOTS3 DONE " + path); Application.Quit(0); yield break; }
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
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-landprobe") >= 0) yield return LandProbe();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-corecheck") >= 0) yield return CoreCheck();
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
        /// -cnshots (玩法策划 v18 text acceptance): fresh Build state -> draw pile 免费 / 再抽 (video badge) / 已用完 / 已满,
        /// one placement notice, reward cards with 墙牌补给 + 符文保底 (+ 建塔折扣 / rune option), defeat screen; text audit + CN glyph check.
        IEnumerator CnShots()
        {
            var ui = FindObjectOfType<GameUI>(); string dir = Path.GetDirectoryName(path), res = Screen.width + "x" + Screen.height;
            var sb = new System.Text.StringBuilder("cn shots " + res + "\n" + CnAtlas());
            IEnumerator Shot(string name) { for (int f = 0; f < 6; f++) yield return null; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, name + "_" + res + ".png")); }
            string Pile() { var dp = FindObjectOfType<StoneSignal.VFX.DrawPileUI>(); return dp == null ? "no DrawPileUI" : "state=" + dp.State + " label='" + (dp.statusLabel ? dp.statusLabel.text : "-") + "' icon=" + (dp.statusIcon && dp.statusIcon.gameObject.activeSelf && dp.statusIcon.sprite ? dp.statusIcon.sprite.name : "hidden") + " interactable=" + (dp.button && dp.button.interactable); }
            var hand = s.Blocks.Hand;
            while (hand.Cards.Count > 1) { hand.Select(0); hand.Consume(); }
            s.Blocks.NotifyChanged();
            yield return Shot("drawpile_free"); sb.Append("draw pile FREE: " + Pile() + "\n");
            bool d1 = s.Draw(); s.Blocks.NotifyChanged();
            yield return Shot("drawpile_ad"); sb.Append("draw pile AD (after free draw ok=" + d1 + ", hand " + hand.Cards.Count + "): " + Pile() + "\n");
            bool d2 = s.Draws.Consume(true); s.Blocks.NotifyChanged(); // the rewarded-video draw, without the ad placeholder
            yield return Shot("drawpile_used"); sb.Append("draw pile USED (ad draw consumed ok=" + d2 + "): " + Pile() + "\n");
            sb.Append(TextAudit("HUD build"));
            // placement notice: a wall on the core cell -> "Cell occupied / protected" -> 这里不能放
            s.Blocks.SelectCard(0); bool placed = s.Blocks.CommitPlacement(s.grid.goal);
            yield return Shot("notice"); sb.Append("notice: commit on core cell placed=" + placed + "\n" + TextAudit("notice"));
            var notices = new[] { "Cell occupied / protected", "Duplicate block cell", "No tower selected", "Not enough gold" };
            foreach (var n in notices) sb.Append("  notice '" + n + "' -> '" + StoneSignal.UI.Loc.Notice(n) + "'\n");
            { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 2.6f) yield return null; } // notice toast expires before the reward / defeat shots
            // reward cards
            var C = StoneSignal.UI.RewardRarity.Common; var R = StoneSignal.UI.RewardRarity.Rare; var E = StoneSignal.UI.RewardRarity.Epic;
            var setA = new[] { C, C, E }; var effA = new[] { RewardEffect.AddBlock, RewardEffect.NextDraw, RewardEffect.TowerDiscount };
            if (ui != null && ui.DebugShowRewardPick(setA, effA)) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < .9f) yield return null; sb.Append(TextAudit("reward cards A")); yield return Shot("reward_addblock_nextdraw"); }
            else sb.Append("reward pick UI unavailable\n");
            var setB = new[] { R, C, E }; var effB = new[] { RewardEffect.AllDamage, RewardEffect.NextDraw, RewardEffect.BaseHP };
            if (ui != null && ui.DebugShowRewardPick(setB, effB)) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < .9f) yield return null; sb.Append(TextAudit("reward cards B")); yield return Shot("reward_rune_nextdraw"); }
            if (ui != null && ui.PickUi != null) ui.PickUi.gameObject.SetActive(false);
            foreach (var r in s.config.rewards) if (r != null) sb.Append("  reward " + r.effect + ": '" + StoneSignal.UI.Loc.RewardTitle(r) + "' / '" + StoneSignal.UI.Loc.RewardDesc(r).Replace("\n", "\\n") + "'\n");
            // defeat
            s.Game.SetState(GameState.GameOver);
            yield return Shot("defeat"); sb.Append(TextAudit("defeat"));
            sb.Append(CnAtlas());
            File.WriteAllText(Path.Combine(dir, "cn_shots_" + res + ".txt"), sb.ToString());
        }
        /// -cnshots3 (玩法策划 decisions after 4b10d0b): notice toast rules + placement (clearance from core / route arrows), draw pile with the
        /// ExtraDraw free 2nd draw and its carry-over, ExtraDraw reward card (精良), drag hand fade at the bottom-left corner cell (+ DC).
        IEnumerator CnShots3()
        {
            var ui = FindObjectOfType<GameUI>(); var pic = PlacementInputController.Instance; var cam = s.viewCamera; var grid = s.grid;
            string dir = Path.GetDirectoryName(path), res = Screen.width + "x" + Screen.height;
            var sb = new System.Text.StringBuilder("cn shots v3 " + res + " aspect " + ((float)Screen.width / Mathf.Max(1, Screen.height)).ToString("F3") + "\n" + CnAtlas());
            IEnumerator Shot(string name) { for (int f = 0; f < 6; f++) yield return null; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, name + "_" + res + ".png")); }
            IEnumerator Wait(float sec) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < sec) yield return null; }
            string Pile() { var dp = FindObjectOfType<StoneSignal.VFX.DrawPileUI>(); return dp == null ? "no DrawPileUI" : "state=" + dp.State + " label='" + (dp.statusLabel ? dp.statusLabel.text : "-") + "' icon=" + (dp.statusIcon && dp.statusIcon.gameObject.activeSelf && dp.statusIcon.sprite ? dp.statusIcon.sprite.name : "hidden") + " interactable=" + (dp.button && dp.button.interactable) + " | used=" + s.Draws.Used + " freeSecond=" + s.Draws.FreeSecond + " pendingExtraDraw=" + s.Modifiers.ExtraDraw + " offer=" + s.DrawOffer; }
            var hand = s.Blocks.Hand; void Trim() { while (hand.Cards.Count > 1) { hand.Select(0); hand.Consume(); } s.Blocks.NotifyChanged(); }
            // ---- 1. notice toast rules (1.5 s, one at a time, same text not re-shown within 1 s) and placement
            if (ui != null)
            {
                const string A = "Cell occupied / protected", B = "No tower selected";
                ui.DebugNotice(A); float t0 = Time.unscaledTime; yield return null; string s0 = ui.NoticeShown;
                while (Time.unscaledTime - t0 < .5f) yield return null; ui.DebugNotice(A); float tRe = Time.unscaledTime - t0; // same text at ~0.5 s
                while (Time.unscaledTime - t0 < 1.4f) yield return null; string s14 = ui.NoticeShown; float t14 = Time.unscaledTime - t0;
                while (Time.unscaledTime - t0 < 1.6f) yield return null; string s16 = ui.NoticeShown; float t16 = Time.unscaledTime - t0;
                ui.DebugNotice(A); yield return null; string sAgain = ui.NoticeShown; float tAgain = Time.unscaledTime - t0;
                ui.DebugNotice(B); yield return null; string sB = ui.NoticeShown;
                sb.Append("toast rules: shown '" + s0 + "'; same text re-sent at " + tRe.ToString("F2") + " s (ignored); at " + t14.ToString("F2") + " s '" + s14 + "'; at " + t16.ToString("F2") + " s '" + s16 + "' (expected empty: 1.5 s, not extended); same text again at " + tAgain.ToString("F2") + " s -> '" + sAgain + "'; new text -> '" + sB + "' (replaces)\n");
                sb.Append("toast rules PASS=" + (s0 == "这里不能放" && s14 == "这里不能放" && s16 == "" && sAgain == "这里不能放" && sB == "先选一座塔") + "\n");
                yield return Wait(1.7f);
                ui.DebugNotice(A); yield return Shot("toast"); sb.Append("toast placement: " + ui.NoticeDebug + "\n");
                yield return Wait(1.7f);
            }
            // ---- 2. draw pile: ExtraDraw free 2nd draw, then carry-over (ExtraDraw while the 2nd draw is already used)
            Trim(); s.Modifiers.ExtraDraw = 1; s.BeginIntermission(); Trim();
            bool e1 = s.Draw(); Trim();
            yield return Shot("drawpile_extradraw_2nd_free"); sb.Append("draw pile, ExtraDraw wave, after the 1st draw (ok=" + e1 + "): " + Pile() + "\n");
            bool e2 = s.Draw(); Trim(); bool now = s.GrantExtraDraw(); s.Blocks.NotifyChanged();
            yield return Shot("drawpile_carry_used"); sb.Append("draw pile, 2nd draw used (free ok=" + e2 + "), ExtraDraw granted now -> applied this wave=" + now + ": " + Pile() + "\n");
            s.BeginIntermission(); Trim(); bool c1 = s.Draw(); Trim();
            yield return Shot("drawpile_carry_next_2nd_free"); sb.Append("draw pile, next wave (carried charge) after the 1st draw (ok=" + c1 + "): " + Pile() + "\n");
            s.Modifiers.ExtraDraw = 0; s.BeginIntermission(); Trim();
            // ---- 3. ExtraDraw reward card (精良)
            var C = StoneSignal.UI.RewardRarity.Common; var R = StoneSignal.UI.RewardRarity.Rare;
            if (ui != null && ui.DebugShowRewardPick(new[] { C, R, C }, new[] { RewardEffect.AddBlock, RewardEffect.ExtraDraw, RewardEffect.TowerDiscount }))
            { yield return Wait(.9f); sb.Append(TextAudit("reward cards ExtraDraw")); yield return Shot("reward_extradraw"); ui.PickUi.gameObject.SetActive(false); }
            else sb.Append("reward pick UI unavailable\n");
            sb.Append("reward pool: " + s.config.rewards.Length + " rewards; ExtraDraw rarity=" + GameUI.RarityOf(RewardEffect.ExtraDraw) + "\n");
            // ---- 4. drag hand fade: block card dragged so the ghost sits on the bottom-left board corner (under the block row)
            if (pic != null)
            {
                Vector2 Pt(Vector2Int c) => cam.WorldToScreenPoint(grid.ToWorld(c) + Vector3.up * grid.tileTop);
                Vector2Int corner = Vector2Int.zero; float bestSum = float.MaxValue;
                for (int x = 0; x < grid.width; x++) for (int y = 0; y < grid.height; y++) { var c = new Vector2Int(x, y); var p = Pt(c); if (p.x + p.y < bestSum) { bestSum = p.x + p.y; corner = c; } }
                Trim(); s.Blocks.DrawCards(2); s.Blocks.NotifyChanged(); yield return null;
                RectTransform Card() { foreach (var r in pic.HandRects) if (r && r.name.StartsWith("Block card")) return r; return null; }
                var card = Card(); var k4 = new Vector3[4]; card.GetWorldCorners(k4); Vector2 start = (k4[0] + k4[2]) * .5f;
                bool force = PointerInput.ForceTouch; PointerInput.ForceTouch = true;
                for (int f = 0; f < 6; f++) yield return null; long dIdle = draws.LastValue;
                PointerInput.Inject(start, true, true, false); pic.CardDown(false, 0, start); yield return null;
                Vector2 tp = Pt(corner); float cellPx = (Pt(corner + Vector2Int.right) - tp).magnitude; Vector2? finger = null; float window = 0;
                foreach (float dx in new[] { 0f, .2f, -.2f, .35f, -.35f })
                {
                    int top = -1;
                    for (int y = Mathf.RoundToInt(tp.y); y >= 1 && !finger.HasValue; y--)
                    {
                        var f = new Vector2(tp.x + dx * cellPx, y);
                        bool hit = !pic.InCancelZone(f) && grid.RaycastCell(cam.ScreenPointToRay(pic.AimFor(f)), out var c, out _) && c == corner;
                        if (hit && top < 0) top = y;
                        if ((!hit || y == 1) && top >= 0) { finger = new Vector2(f.x, (top + y) * .5f); window = top - y; }
                    }
                    if (finger.HasValue) break;
                }
                sb.Append("drag fade: corner cell " + corner + " at " + tp.ToString("F0") + " cellPx=" + cellPx.ToString("F0") + " finger=" + (finger.HasValue ? finger.Value.ToString("F0") + " window " + window.ToString("F0") + " px" : "NOT FOUND") + " start(card)=" + start.ToString("F0") + "\n");
                Vector2 fp = finger ?? new Vector2(tp.x, tp.y - pic.DragOffset * pic.CanvasScale);
                for (int i = 1; i <= 8; i++) { PointerInput.Inject(Vector2.Lerp(start, fp, i / 8f), false, true, false); yield return null; }
                float tf = Time.realtimeSinceStartup; string ramp = "";
                while (Time.realtimeSinceStartup - tf < .3f) { ramp += (Time.realtimeSinceStartup - tf).ToString("F2") + ":" + ui.HandAlpha.ToString("F2") + " "; yield return null; }
                yield return Clean(); long dFade = draws.LastValue;
                sb.Append("drag fade: state=" + pic.Machine.State + " HandFade=" + pic.HandFade + " GhostOverHand=" + pic.GhostOverHand + " fingerOverHand=" + pic.IsOverHand(fp) + " preview anchor=" + s.Blocks.PreviewAnchor + " shown=" + s.Blocks.PreviewShown + " valid=" + s.Blocks.PreviewValid + "\n  alpha ramp (s:alpha) " + ramp + "\n  card alphas " + ui.HandAlphas + "\n");
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "drag_fade_corner_" + res + ".png"));
                // draw calls of the fade itself: same frame content with the card alphas forced back to 1 for a few frames
                var groups = new List<(CanvasGroup g, float a)>(); foreach (var g in FindObjectsOfType<CanvasGroup>()) if (g.transform.parent != null && (g.transform.parent.name == "Tower hand" || g.transform.parent.name == "Block hand")) groups.Add((g, g.alpha));
                yield return Clean(); long dFade2 = draws.LastValue, bFade = batches.LastValue;
                foreach (var g in groups) g.g.alpha = 1f; for (int f = 0; f < 4; f++) yield return null; long dOpaque = draws.LastValue, bOpaque = batches.LastValue;
                foreach (var g in groups) g.g.alpha = g.a; yield return null;
                sb.Append("drag fade DC: idle hand (before drag) " + dIdle + " | dragging, cards faded " + dFade + "/" + dFade2 + " (batches " + bFade + ") | same frame, alphas forced to 1: " + dOpaque + " (batches " + bOpaque + ") -> fade delta " + (dFade2 - dOpaque) + " DC\n");
                // leave + release over the card's lower half (cancel), cards restore
                card = Card(); if (card != null) { card.GetWorldCorners(k4); var low = new Vector2((k4[0].x + k4[2].x) * .5f, Mathf.Lerp(k4[0].y, k4[1].y, .25f)); PointerInput.Inject(low, false, true, false); yield return null; PointerInput.Inject(low, false, false, true); yield return null; yield return null; }
                PointerInput.ClearInjection(); PointerInput.ForceTouch = force;
                yield return Wait(.3f);
                sb.Append("after release: state=" + pic.Machine.State + " HandFade=" + pic.HandFade + " alpha=" + ui.HandAlpha.ToString("F2") + " card alphas " + ui.HandAlphas + "\n");
                yield return Shot("drag_fade_released");
            }
            File.WriteAllText(Path.Combine(dir, "cn3_" + res + ".txt"), sb.ToString());
        }
        // ---- v18.3 diagnostics ------------------------------------------------------------------------------------------------
        bool Notch() { bool n = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-notch") >= 0; if (n) HudScaler.SimulatedSafeArea = new Rect(132, 63, Screen.width - 264, Screen.height - 63); return n; }
        IEnumerator WaitRt(float sec) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < sec) yield return null; }
        /// Worst-case hand: free draw, then top up to BlockHandManager.MaxCards (7) block cards.
        IEnumerator FullHand()
        {
            if (s.Game.State == GameState.Build) s.Draw();
            var hand = s.Blocks.Hand; int g = 0;
            while (hand.Cards.Count < BlockHandManager.MaxCards && g++ < 20) hand.AddCard(s.config.blocks[g % s.config.blocks.Length], RuneRules.NoRune);
            s.Blocks.NotifyChanged(); yield return null; yield return null;
        }
        IEnumerator ShotNamed(string dir, string name) { yield return Clean(); for (int f = 0; f < 6; f++) yield return null; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, name + ".png")); }
        /// -camcompare [-touch] [-notch]: full 7-card hand, CameraFit Screen vs HudFree: report, coverage, toast anchor / guard, full-frame HUD shot.
        IEnumerator CamCompare()
        {
            bool notch = Notch(); var ui = FindObjectOfType<GameUI>(); var fit = CameraFit.Instance; string dir = Path.GetDirectoryName(path);
            string res = Screen.width + "x" + Screen.height + (notch ? "_notch" : "");
            var sb = new System.Text.StringBuilder("camera compare " + res + " aspect " + ((float)Screen.width / Screen.height).ToString("F3") + " touch=" + PointerInput.TouchMode + " safe=" + HudScaler.SafeArea + "\n");
            yield return FullHand(); yield return WaitRt(.6f);
            sb.Append("hand: " + s.Blocks.Hand.Cards.Count + " block cards + " + s.config.towers.Length + " tower cards\n");
            foreach (var mode in new[] { CameraFitMode.Screen, CameraFitMode.HudFree })
            {
                CameraFit.Mode = mode; int f0 = fit.Fits; fit.Refit(); float tw = Time.realtimeSinceStartup;
                while (fit.Fits == f0 && Time.realtimeSinceStartup - tw < 4f) yield return null;
                yield return WaitRt(.5f);
                bool pass = fit.CoveredCells == 0 && fit.LandingsVisible && fit.ArrowsVisible && fit.CellPx1080 >= fit.minCellPx1080 && fit.EnemyPx1080 >= fit.minEnemyPx1080;
                sb.Append("=== mode " + mode + "\n" + fit.Report + fit.Coverage + "\n");
                yield return ShotNamed(dir, "hud_" + mode.ToString().ToLowerInvariant() + "_" + res);
                yield return WaitRt(.3f);
                ui.DebugNotice("Cell occupied / protected"); yield return WaitRt(.4f);
                string toast = ui.NoticeDebug; bool guard = ui.ToastGuardTriggered; yield return WaitRt(1.4f);
                sb.Append("  toast: " + toast + " | guard triggered=" + guard + "\n");
                sb.Append("SUMMARY\t" + mode + "\t" + res + "\tortho " + fit.Size.ToString("F2") + "\tpan " + fit.Pan.ToString("F2") + "\tcell1080 " + fit.CellPx1080.ToString("F1") + "\tenemy1080 " + fit.EnemyPx1080.ToString("F1") +
                          "\tcovered " + fit.CoveredCells + "\tbad " + fit.CoveredBad + "\tlandings " + fit.LandingsVisible + "\tarrows " + fit.ArrowsVisible + "\tpanfix " + fit.PanFixed + "\ttoastGuard " + guard + "\tHUDFREE-CRITERIA " + (pass ? "PASS" : "FAIL") + "\n");
            }
            CameraFit.Mode = CameraFitMode.Screen; fit.Refit();
            File.WriteAllText(Path.Combine(dir, "cam_" + res + ".txt"), sb.ToString());
            HudScaler.SimulatedSafeArea = null;
        }
        /// -dragthrough [-touch]: full hand (Screen fit); a wall ghost dragged onto a board cell under a FADED block card (shot mid-drag),
        /// release -> the cell is accepted (direction select); then a tower dragged onto a wall under the faded hand is placed.
        IEnumerator DragThrough()
        {
            var ui = FindObjectOfType<GameUI>(); var pic = PlacementInputController.Instance; var grid = s.grid; var cam = s.viewCamera != null ? s.viewCamera : Camera.main;
            string dir = Path.GetDirectoryName(path), res = Screen.width + "x" + Screen.height; var sb = new System.Text.StringBuilder("drag through " + res + " touch=" + PointerInput.TouchMode + " passThrough=" + pic.HandPassThrough + "\n");
            yield return FullHand(); yield return WaitRt(.6f);
            var cards = ui.DebugHandCards(); var blockRects = new List<(Rect r, int index)>(); var c4 = new Vector3[4];
            foreach (var h in cards) if (!h.tower) { h.rt.GetWorldCorners(c4); blockRects.Add((Rect.MinMaxRect(c4[0].x, c4[0].y, c4[2].x, c4[2].y), h.index)); }
            Vector2 Top(Vector2Int c, bool wall) => cam.WorldToScreenPoint(grid.ToWorld(c) + Vector3.up * (wall ? grid.wallTop : grid.tileTop));
            Vector2Int target = new Vector2Int(-1, -1); int under = -1; float baseOff = pic.DragOffset * pic.CanvasScale;
            for (int y = 0; y < grid.height && target.x < 0; y++) for (int x = 0; x < grid.width && target.x < 0; x++)
            {
                var c = new Vector2Int(x, y); if (!grid.CanPlace(c) || OnRoute(c) || s.Validator.ValidatePlacement(new List<Vector2Int> { c }) != null) continue;
                var sp = Top(c, false); var fg = sp - Vector2.up * baseOff; foreach (var b in blockRects) if (b.r.Contains(sp) && !pic.InCancelZone(fg) && fg.y > 0 && grid.RaycastCell(cam.ScreenPointToRay(pic.AimFor(fg)), out var hc, out _) && hc == c) { target = c; under = b.index; break; }
            }
            Vector2 fingerOverride = default; bool nearest = false;
            if (target.x < 0)
            {
                // no board cell lies under a block card (e.g. 4:3): take the placeable cell nearest above the block row whose aim is reachable with the finger ON a block card
                float rowTop = float.MinValue; foreach (var b in blockRects) rowTop = Mathf.Max(rowTop, b.r.yMax); float best = float.MaxValue;
                for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
                {
                    var c = new Vector2Int(x, y); if (!grid.CanPlace(c) || OnRoute(c) || s.Validator.ValidatePlacement(new List<Vector2Int> { c }) != null) continue;
                    var sp = Top(c, false); var fg = sp - Vector2.up * baseOff; if (sp.y - rowTop >= best) continue;
                    foreach (var b in blockRects) if (b.r.Contains(fg) && !pic.InCancelZone(fg) && grid.RaycastCell(cam.ScreenPointToRay(pic.AimFor(fg)), out var hc, out _) && hc == c) { best = sp.y - rowTop; target = c; under = b.index; fingerOverride = fg; nearest = true; break; }
                }
                if (target.x < 0) { sb.Append("no placeable board cell under or reachable from a block card at this aspect\n"); File.WriteAllText(Path.Combine(dir, "drag_" + res + ".txt"), sb.ToString()); yield break; }
                sb.Append("no placeable board cell lies under the hand at this aspect (worst-case hand covers edge cells only); nearest cell " + target + " is " + best.ToString("F0") + " px above the block row; finger rests on faded block card #" + under + "\n");
            }
            int dragIdx = -1; Rect dragR = default; foreach (var b in blockRects) if (b.index != under && !b.r.Contains(Top(target, false))) { dragIdx = b.index; dragR = b.r; break; }
            sb.Append("target cell " + target + (nearest ? " (finger over" : " under") + " block card #" + under + " (screen " + Top(target, false).ToString("F0") + "), dragging block card #" + dragIdx + "\n");
            // --- wall ghost
            Vector2 start = dragR.center, finger = nearest ? fingerOverride : Top(target, false) - Vector2.up * baseOff;
            PointerInput.Inject(start, true, true, false); pic.CardDown(false, dragIdx, start); yield return null;
            for (int i = 1; i <= 12; i++) { PointerInput.Inject(Vector2.Lerp(start, finger, i / 12f), false, true, false); yield return null; }
            // let the hand fade settle (0.45 s) without triggering hold-still direction: the finger wiggles more than HoldStill each frame
            float wig = (pic.Machine.HoldStill + 3f) * pic.CanvasScale; int fr = 0;
            float tw0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - tw0 < .45f) { PointerInput.Inject(finger + new Vector2((fr++ & 1) == 0 ? wig : 0f, 0), false, true, false); yield return null; }
            PointerInput.Inject(finger, false, true, false); yield return null;
            var hits = new List<UnityEngine.EventSystems.RaycastResult>(); var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = Top(target, false) }, hits);
            string hitNames = ""; foreach (var h in hits) hitNames += h.gameObject.name + ";";
            sb.Append("mid-drag: state=" + pic.Machine.State + " cell=" + pic.Machine.Cell + " handFade=" + pic.HandFade + " passThrough(raycasts off)=" + ui.HandPassThroughActive + " alphas " + ui.HandAlphas + "\n  UI raycast at the target cell: " + hits.Count + " hit(s) " + hitNames + "\n");
            yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "drag_wall_" + res + ".png"));
            // the capture stalls a frame: step one cell aside and back (resets the hold-still timer), then release
            PointerInput.Inject(finger + Vector2.right * (Top(target + Vector2Int.right, false) - Top(target, false)).magnitude, false, true, false); yield return null;
            PointerInput.Inject(finger, false, true, false); yield return null;
            string preRelease = pic.Machine.State + " " + pic.Machine.Cell;
            PointerInput.Inject(finger, false, false, true); yield return null; yield return null;
            sb.Append("  just before release: " + preRelease + "\n");
            sb.Append("after release: state=" + pic.Machine.State + " cell=" + pic.Machine.Cell + " -> " + (pic.Machine.State == PlacementState.Direction && pic.Machine.Cell == target ? "PASS wall accepted on the cell under the faded hand (direction select open)" : "FAIL") + "\n");
            PointerInput.ClearInjection(); pic.CancelPlacement(); yield return WaitRt(.4f);
            // --- tower onto a wall under the faded hand
            bool wall = TryWall(target); s.Economy.AddGold(500); int towers0 = s.Towers.Towers.Count; yield return null;
            Rect tr = default; foreach (var h in ui.DebugHandCards()) if (h.tower && h.index == 0) { h.rt.GetWorldCorners(c4); tr = Rect.MinMaxRect(c4[0].x, c4[0].y, c4[2].x, c4[2].y); }
            Vector2 tStart = tr.center, tFinger = Top(target, true) - Vector2.up * baseOff;
            PointerInput.Inject(tStart, true, true, false); pic.CardDown(true, 0, tStart); yield return null;
            for (int i = 1; i <= 12; i++) { PointerInput.Inject(Vector2.Lerp(tStart, tFinger, i / 12f), false, true, false); yield return null; }
            yield return WaitRt(.2f);
            string mid = "state=" + pic.Machine.State + " cell=" + pic.Machine.Cell + " cancelZone=" + pic.InCancelZone(tFinger);
            PointerInput.Inject(tFinger, false, false, true); yield return null; yield return null; PointerInput.ClearInjection();
            bool placed = s.Towers.Towers.Count == towers0 + 1; bool dirOpen = !placed && pic.Machine.State == PlacementState.Direction && pic.Machine.Cell == target; if (dirOpen) mid += " | released into direction select on the target cell";
            sb.Append("tower drag onto the wall at " + target + " (wall committed " + wall + "): mid " + mid + " -> towers " + towers0 + " -> " + s.Towers.Towers.Count + " " + (placed ? "PASS tower placed under the faded hand" : dirOpen ? "PASS (accepted on the cell; tower needs direction select)" : "FAIL") + "\n");
            pic.CancelPlacement();
            File.WriteAllText(Path.Combine(dir, "drag_" + res + ".txt"), sb.ToString());
        }
        /// -cardtext [-touch] [-notch]: orphan-character audit of every reward / rune card text, then the ExtraDraw card (new text) on a reward screen.
        IEnumerator CardText()
        {
            bool notch = Notch(); var ui = FindObjectOfType<GameUI>(); string dir = Path.GetDirectoryName(path), res = Screen.width + "x" + Screen.height + (notch ? "_notch" : "");
            yield return WaitRt(.5f);
            var sb = new System.Text.StringBuilder(ui.DebugCardTextAudit());
            var C = StoneSignal.UI.RewardRarity.Common; var R = StoneSignal.UI.RewardRarity.Rare; var E = StoneSignal.UI.RewardRarity.Epic;
            if (ui.DebugShowRewardPick(new[] { R, C, E }, new[] { RewardEffect.ExtraDraw, RewardEffect.NextDraw, RewardEffect.AllDamage }))
            { yield return WaitRt(1.2f); sb.Append(TextAudit("reward screen ExtraDraw / NextDraw / AllDamage")); yield return ShotNamed(dir, "reward_extradraw_" + res); }
            File.WriteAllText(Path.Combine(dir, "cardtext_" + res + ".txt"), sb.ToString());
            HudScaler.SimulatedSafeArea = null;
        }
        // v18.2 -hudshots [-touch] [-notch]: full HUD frame with the aspect-adaptive camera (CameraFit report), touch-mode hotkey badge check,
        // clean-frame draw calls, then a real weighted reward offer (RewardRoll) on the reward screen. Writes hud_<res>.txt.
        IEnumerator HudShots()
        {
            var args = System.Environment.GetCommandLineArgs(); bool notch = System.Array.IndexOf(args, "-notch") >= 0;
            if (notch) HudScaler.SimulatedSafeArea = new Rect(132, 63, Screen.width - 264, Screen.height - 63); // landscape notch L/R 132 px, home bar 63 px
            string dir = Path.GetDirectoryName(path), res = Screen.width + "x" + Screen.height + (notch ? "_notch" : "") + (PointerInput.TouchMode ? "_touch" : "_pc");
            var sb = new System.Text.StringBuilder("hud shots " + res + " aspect " + ((float)Screen.width / Mathf.Max(1, Screen.height)).ToString("F3") + " touchMode=" + PointerInput.TouchMode + " (ForceTouch=" + PointerInput.ForceTouch + " -touch=" + (System.Array.IndexOf(args, "-touch") >= 0) + " mobilePlatform=" + Application.isMobilePlatform + ") safe=" + HudScaler.SafeArea + "\n");
            IEnumerator Wait(float sec) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < sec) yield return null; }
            IEnumerator Shot(string name) { for (int f = 0; f < 6; f++) yield return null; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, name + "_" + res + ".png")); }
            string Dc() => "drawCalls=" + draws.LastValue + " batches=" + batches.LastValue + " setPass=" + setPass.LastValue + " tris=" + tris.LastValue;
            if (s.Game.State == GameState.Build) s.Draw(); // free draw: 3 block cards in the hand (typical build HUD)
            var fit = CameraFit.Instance; if (notch && fit != null) fit.Refit();
            float tw = Time.realtimeSinceStartup; while ((fit == null || fit.Fits == 0 || notch && fit.Fits < 2) && Time.realtimeSinceStartup - tw < 3f) yield return null;
            yield return Wait(.6f);
            sb.Append("camera fit: ").Append(fit != null ? fit.Report : "NO CameraFit").Append('\n');
            int badges = 0, badgesAll = 0; foreach (var t in FindObjectsOfType<RectTransform>(true)) if (t.name == "Hotkey") { badgesAll++; if (t.gameObject.activeInHierarchy) badges++; }
            sb.Append("hotkey badges (1-4) visible ").Append(badges).Append(" of ").Append(badgesAll).Append(PointerInput.TouchMode ? (badges == 0 ? " -> hidden in touch mode OK" : " -> VISIBLE IN TOUCH MODE (bug)") : " (PC mode: shown)").Append('\n');
            yield return Clean(); for (int f = 0; f < 10; f++) yield return null;
            sb.Append("HUD frame (clean, no capture): ").Append(Dc()).Append('\n');
            yield return Shot("hud");
            // reward screen with a real weighted offer
            s.Game.SetState(GameState.Combat); s.Game.SetState(GameState.Reward); s.Rewards.DebugOffer();
            yield return Wait(1.4f);
            sb.Append("reward offer (seed ").Append(GameRng.Seed).Append("):");
            for (int i = 0; i < s.Rewards.Choices.Count; i++) { int rn = s.Rewards.RuneChoices[i]; var r = s.Rewards.Choices[i]; sb.Append(rn != RuneRules.NoRune ? " rune " + RuneRules.Names[rn] + " (精良)" : " " + r.effect + " (" + r.tier + ")"); }
            sb.Append('\n');
            yield return Clean(); for (int f = 0; f < 10; f++) yield return null;
            sb.Append("reward screen (clean): ").Append(Dc()).Append('\n');
            yield return Shot("reward");
            File.WriteAllText(Path.Combine(dir, "hud_" + res + ".txt"), sb.ToString());
            HudScaler.SimulatedSafeArea = null;
        }
        // v17.6 -toastshots [-touch]: art toast (ui9_toast + warn/info icon + ToastFx) WARNING and INFO in the hold phase, timing samples,
        // toast rules, sprite/atlas page check, draw calls with / without the toast; then a pick-one-of-three with ExtraDraw + NextDraw.
        IEnumerator ToastShots()
        {
            var ui = FindObjectOfType<GameUI>(); string dir = Path.GetDirectoryName(path), res = Screen.width + "x" + Screen.height;
            var sb = new System.Text.StringBuilder("toast shots v17.6 " + res + " aspect " + ((float)Screen.width / Mathf.Max(1, Screen.height)).ToString("F3") + " touch=" + PointerInput.TouchMode + "\n" + CnAtlas());
            IEnumerator Wait(float sec) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < sec) yield return null; }
            IEnumerator Shot(string name) { yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, name + "_" + res + ".png")); }
            string Dc() => "drawCalls=" + draws.LastValue + " batches=" + batches.LastValue + " setPass=" + setPass.LastValue;
            if (s.Game.State == GameState.Build) s.Draw();
            yield return Wait(1f);
            var fx = ui != null ? ui.NoticeFx : null; var rt = ui != null ? ui.NoticeRect : null;
            if (fx == null || rt == null) { sb.Append("NO ToastFx on the notice pill (ToastStyle.Apply not wired)\n"); File.WriteAllText(Path.Combine(dir, "toast_" + res + ".txt"), sb.ToString()); yield break; }
            var img = rt.GetComponent<UnityEngine.UI.Image>(); Transform ic = rt.Find("Icon"); var icon = ic != null ? ic.GetComponent<UnityEngine.UI.Image>() : null;
            sb.Append("ToastFx popIn " + fx.popIn.ToString("F2") + " hold " + fx.hold.ToString("F2") + " fadeOut " + fx.fadeOut.ToString("F2") + " -> total " + fx.TotalSeconds.ToString("F2") + " s\n");
            yield return Clean(); for (int f = 0; f < 8; f++) yield return null; string dcNo = Dc();
            foreach (var kind in new[] { ("warn", "Cell occupied / protected"), ("info", "Tower ready") })
            {
                ui.DebugNotice(kind.Item2); float t0 = Time.unscaledTime; var samples = new System.Text.StringBuilder();
                float[] at = { 0f, .06f, .12f, .18f, .5f, 1.25f, 1.35f, 1.45f, 1.52f }; int k = 0;
                while (k < at.Length && Time.unscaledTime - t0 < 2f)
                {
                    yield return null; float e = Time.unscaledTime - t0;
                    if (e >= at[k]) { var cg = rt.GetComponent<CanvasGroup>(); samples.Append(" " + e.ToString("F2") + "s:" + (rt.gameObject.activeInHierarchy ? "a" + (cg ? cg.alpha : 1f).ToString("F2") + "/s" + rt.localScale.x.ToString("F2") : "hidden")); k++; }
                    if (k == 4) // hold phase (0.5 s): sprite / icon / DC / capture
                    {
                        sb.Append(kind.Item1 + " '" + ui.NoticeShown + "' kind=" + fx.Kind + " pill sprite=" + (img && img.sprite ? img.sprite.name + " tex=" + img.sprite.texture.name + " border=" + img.sprite.border + " type=" + img.type : "NONE") +
                                  " icon=" + (icon && icon.enabled && icon.sprite ? icon.sprite.name + " tex=" + icon.sprite.texture.name : "none") + " rect=" + rt.rect.size + " | " + ui.NoticeDebug + "\n");
                        yield return Clean(); for (int f = 0; f < 3; f++) yield return null;
                        sb.Append("  DC with toast (clean frame) " + Dc() + " | without toast " + dcNo + "\n");
                        yield return Shot("toast_" + kind.Item1);
                    }
                }
                sb.Append("  timing samples (alpha/scale):" + samples + "\n");
                yield return Wait(1.2f);
            }
            {   // rules with the art toast: 1.5 s total, same text within 1 s ignored (no pop restart), one at a time (new text replaces + pops)
                const string A = "Cell occupied / protected", B = "No tower selected";
                ui.DebugNotice(A); float t0 = Time.unscaledTime; yield return null; string s0 = ui.NoticeShown;
                while (Time.unscaledTime - t0 < .5f) yield return null; ui.DebugNotice(A); yield return null; float el = fx.Elapsed;
                while (Time.unscaledTime - t0 < 1.4f) yield return null; string s14 = ui.NoticeShown;
                while (Time.unscaledTime - t0 < 1.6f) yield return null; string s16 = ui.NoticeShown;
                ui.DebugNotice(A); yield return null; string sAgain = ui.NoticeShown;
                while (Time.unscaledTime - t0 < 2.0f) yield return null; ui.DebugNotice(B); yield return null; string sB = ui.NoticeShown; float elB = fx.Elapsed;
                bool ok = s0 == "这里不能放" && el > .4f && s14 == "这里不能放" && s16 == "" && sAgain == "这里不能放" && sB == "先选一座塔" && elB < .1f;
                sb.Append("toast rules: shown '" + s0 + "'; same text re-sent at 0.5 s -> ToastFx elapsed " + el.ToString("F2") + " s (not restarted); 1.4 s '" + s14 + "'; 1.6 s '" + s16 + "' (1.5 s, not extended); again -> '" + sAgain + "'; new text -> '" + sB + "' elapsed " + elB.ToString("F2") + " (replaces, pops) PASS=" + ok + "\n");
                yield return Wait(1.7f);
            }
            // atlas: same page for the toast parts and the rest of the HUD
            var names = new[] { "ui9_toast", "ui_icon_warn", "ui_icon_info", "ui_reward_extra_draw", "ui_reward_next_draw", "ui9_panel_navy", "ui9_pill_gold", "ui_draw_pile" };
            var pages = new HashSet<string>();
            foreach (var n in names) { var sp = ui.DebugSprite(n); sb.Append("  sprite " + n + " -> " + (sp ? sp.texture.name + " packed=" + sp.packed : "MISSING") + "\n"); if (sp) pages.Add(sp.texture.name); }
            sb.Append("HUD atlas textures used by these sprites: " + pages.Count + " [" + string.Join(", ", pages) + "]\n");
            // reward pick-one-of-three with ExtraDraw (精良) + NextDraw (符文保底) + AddBlock
            var C = StoneSignal.UI.RewardRarity.Common; var R = StoneSignal.UI.RewardRarity.Rare;
            if (ui.DebugShowRewardPick(new[] { R, C, C }, new[] { RewardEffect.ExtraDraw, RewardEffect.NextDraw, RewardEffect.AddBlock }))
            {
                yield return Wait(1.2f); yield return Clean(); for (int f = 0; f < 6; f++) yield return null;
                sb.Append("reward pick (ExtraDraw / NextDraw / AddBlock): " + Dc() + "\n" + TextAudit("reward cards v17.6"));
                yield return Shot("reward_extradraw_nextdraw");
            }
            File.WriteAllText(Path.Combine(dir, "toast_" + res + ".txt"), sb.ToString());
        }
        IEnumerator RewardShots()
        {
            var ui = FindObjectOfType<GameUI>(); if (ui == null) yield break;
            string dir = Path.GetDirectoryName(path), info = "";
            var sets = new[] { new[] { StoneSignal.UI.RewardRarity.Common, StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic },
                               new[] { StoneSignal.UI.RewardRarity.Rare, StoneSignal.UI.RewardRarity.Epic, StoneSignal.UI.RewardRarity.Legendary },
                               new[] { StoneSignal.UI.RewardRarity.Common, StoneSignal.UI.RewardRarity.Epic, StoneSignal.UI.RewardRarity.Legendary } }; // v17.4: 3 wave rewards, no rune card
            string Tag(int k) => k == 0 ? "CRE" : k == 1 ? "REL" : "CEL";
            TimeController.SetSpeed(0);
            info += TextAudit("HUD");
            // v17.4 reward icons: every RewardEffect -> RewardIconMap sprite resolved from ArtCatalog.uiSprites (BatchWire) and packed in the HUD atlas?
            var cat = Resources.FindObjectsOfTypeAll<ArtCatalog>(); var ac = cat.Length > 0 ? cat[0] : null; int found = 0, total = 0;
            foreach (RewardEffect e in System.Enum.GetValues(typeof(RewardEffect)))
            {
                var n = StoneSignal.UI.RewardIconMap.For(e); var sp = ac != null && n != null ? ac.UiSprite(n) : null; total++; if (sp) found++;
                info += "  icon " + e + " -> " + (n ?? "null") + (sp ? " OK tex=" + sp.texture.name + " " + sp.texture.width + "x" + sp.texture.height + " packed=" + sp.packed : " MISSING (old fallback)") + "\n";
            }
            info += "reward icons resolved " + found + "/" + total + "\n";
            if (ac != null && ac.uiSprites != null)
            {
                var pages = new Dictionary<Texture2D, int>(); int loose = 0, all = 0;
                foreach (var u in ac.uiSprites) { if (u == null) continue; all++; if (u.packed) { pages.TryGetValue(u.texture, out var c); pages[u.texture] = c + 1; } else loose++; }
                info += "HUD sprites: " + all + " in ArtCatalog.uiSprites, " + loose + " not packed, atlas pages=" + pages.Count + ":";
                foreach (var kv in pages) info += " " + kv.Key.name + " " + kv.Key.width + "x" + kv.Key.height + " (" + kv.Value + " sprites)";
                info += "\n";
            }
            for (int k = 0; k < sets.Length; k++)
            {
                if (!ui.DebugShowRewardPick(sets[k])) { info += "reward pick UI unavailable\n"; break; }
                float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < .8f) yield return null; // glow fade-in done
                for (int f = 0; f < 4; f++) yield return null; long dOn = draws.LastValue, bOn = batches.LastValue;
                info += TextAudit("reward cards " + Tag(k));
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_glow_" + Tag(k) + ".png"));
                ui.PickGlow.enabled = false; for (int f = 0; f < 6; f++) yield return null; long dOff = draws.LastValue, bOff = batches.LastValue;
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "reward_noglow_" + Tag(k) + ".png"));
                ui.PickGlow.enabled = true; for (int f = 0; f < 4; f++) yield return null;
                info += "set " + string.Join(",", sets[k]) + ": glow ON drawCalls=" + dOn + " batches=" + bOn + " | glow OFF drawCalls=" + dOff + " batches=" + bOff + " | delta DC=" + (dOn - dOff) + " batches=" + (bOn - bOff) + "\n";
            }
            // v17.4: what the reward overlay and its icons cost (C,E,L = 3 wave-reward icons)
            {
                ui.DebugShowRewardPick(sets[2]); for (int f = 0; f < 8; f++) yield return null; yield return Clean(); long dAll = draws.LastValue, bAll = batches.LastValue;
                var pcv = ui.PickUi.GetComponentInParent<Canvas>(); pcv.enabled = false; for (int f = 0; f < 4; f++) yield return null; long dScene = draws.LastValue, bScene = batches.LastValue; pcv.enabled = true;
                var icons = new List<UnityEngine.UI.Image>(); foreach (var im in ui.PickUi.GetComponentsInChildren<UnityEngine.UI.Image>()) if (im.enabled && (im.name == "Icon" || im.name == "IconShadow")) { im.enabled = false; icons.Add(im); }
                for (int f = 0; f < 4; f++) yield return null; long dNoIcon = draws.LastValue, bNoIcon = batches.LastValue; foreach (var im in icons) im.enabled = true;
                info += "C,E,L overlay cost: with cards DC=" + dAll + " batches=" + bAll + " | reward canvas off DC=" + dScene + " batches=" + bScene + " -> overlay +" + (dAll - dScene) + " DC +" + (bAll - bScene) + " batches | icons (" + icons.Count + " Images) off DC=" + dNoIcon + " -> icons +" + (dAll - dNoIcon) + " DC\n";
                ui.PickGlow.enabled = true; for (int f = 0; f < 4; f++) yield return null;
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
            info += "WalkSurface tiles=" + WalkSurface.TileCount + " profiles=" + WalkSurface.ProfileCount + " plank samples=" + WalkSurface.PlankSamples + "\n";
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
                    if (shotAt < 0 && !en.HeldBySpawn && along > (run.Item1.Contains("right") ? -3.6f : -2.2f)) shotAt = Time.realtimeSinceStartup; // v17.3: from the island end of the bridge onto the tiles (v17.5 right bridge: from the sand)
                    if (shotAt >= 0 && Time.realtimeSinceStartup - shotAt >= k * .7f)
                    {
                        cam.transform.position = p - cam.transform.forward * 6f; yield return null; yield return Clean(); yield return new WaitForEndOfFrame();
                        long dc = draws.LastValue; Capture(Path.Combine(dir, run.Item1 + "_" + k + ".png"));
                        var cell = s.grid.ToCell(p); string where = s.grid.InBounds(cell) ? "tiles" : along > -.4f ? "bridge" : "island/bridge start";
                        where += WalkSurface.TryGet(p, out var wy, out var wk) ? ", WalkSurface " + wk + " y=" + wy.ToString("F2") : ", WalkSurface -";
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
                if (near++ < 6) sb.Append(" [" + wp.x.ToString("F2") + "," + wp.y.ToString("F2") + "," + wp.z.ToString("F2") + " surf=" + (WalkSurface.TryGet(wp, out var sy, out var sk) ? sy.ToString("F2") + " " + sk : "-") + " colR=" + ps[i].startColor.r + (ps[i].startColor.r < 128 ? "(plank print)" : "") + " rot=" + ps[i].rotation.ToString("F0") + " age=" + (ps[i].startLifetime - ps[i].remainingLifetime).ToString("F2") + "]");
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
        // -landprobe (v18): per-frame draw calls around a block landing, no screenshots in the measured window (a Capture renders the
        // camera twice inside the frame it runs in, so any DC sampled across a capture is inflated). Typical board (Build) and a
        // late-wave-like board (Combat, many walls/towers/enemies). One run captures at +6 frames on purpose to show the capture artefact.
        IEnumerator LandProbe()
        {
            string dir = Path.GetDirectoryName(path); var sb = new System.Text.StringBuilder();
            captureNoUi = false; TimeController.ResetAll();
            // camera: wherever the main shot left it (run -landprobe without the close-up steps = default gameplay view)
            HashSet<Renderer> baseVis = null;
            string Row(int f, float t)
            {
                int vis = 0, ps = 0, alive = 0, wallsOn = 0;
                foreach (var r in FindObjectsOfType<Renderer>()) { if (!r.enabled || !r.isVisible) continue; vis++; if (r is ParticleSystemRenderer) { ps++; var p = r.GetComponent<ParticleSystem>(); if (p != null) alive += p.particleCount; } }
                foreach (var b in InstancedBatch.All) if (b != null && b.name.Contains("lace")) foreach (var mr in b.GetComponentsInChildren<MeshRenderer>()) if (mr.enabled) wallsOn++;
                return f + "\t" + t.ToString("F3") + "\t" + draws.LastValue + "\t" + batches.LastValue + "\t" + setPass.LastValue + "\t" + vis + "\t" + ps + "\t" + alive + "\t" + wallsOn;
            }
            HashSet<Renderer> Visible() { var h = new HashSet<Renderer>(); foreach (var r in FindObjectsOfType<Renderer>()) if (r.enabled && r.isVisible) h.Add(r); return h; }
            string Path2(Transform t) { string n = t.name; for (var p = t.parent; p != null && n.Length < 120; p = p.parent) n = p.name + "/" + n; return n; }
            BlockShapeData shape0 = s.Blocks.Hand.Cards.Count > 0 ? s.Blocks.Hand.Cards[0] : (s.Blocks.Deck.Cards.Count > 0 ? s.Blocks.Deck.Cards[0] : null);
            void EnsureCard() { if (s.Blocks.Hand.Cards.Count == 0) s.Blocks.DrawCards(2); if (s.Blocks.Hand.Cards.Count == 0 && shape0 != null) s.Blocks.Hand.AddCard(shape0, RuneRules.NoRune); s.Blocks.SelectCard(0); }
            bool FindCell(out Vector2Int cell)
            {
                cell = default;
                for (int pass = 0; pass < 2; pass++)
                    for (int y = 1; y < s.grid.height - 1; y++) for (int x = 1; x < s.grid.width - 1; x++) { var c = new Vector2Int(x, y); if (s.Blocks.CurrentShape != null && s.Blocks.ValidatePlacement(c) == null && (pass == 1 || !OnRoute(c))) { cell = c; return true; } }
                return false;
            }
            IEnumerator KillAll(float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (s.Enemies.Active.Count > 0 && Time.realtimeSinceStartup - t0 < timeout) { foreach (var e in new List<Enemy>(s.Enemies.Active)) if (e != null && e.Alive) e.TakeDamage(1e7f); yield return new WaitForSecondsRealtime(.25f); }
            }
            IEnumerator One(string label, int captureAt)
            {
                s.Blocks.enabled = true; s.Blocks.Pinned = false; EnsureCard();
                bool have = FindCell(out var valid);
                if (!have) { sb.Append(label + ": no valid placement\n"); yield break; }
                for (int f = 0; f < 40; f++) yield return null;
                sb.Append("== " + label + " (state " + s.Game.State + ", enemies " + s.Enemies.Active.Count + ", walls " + System.Linq.Enumerable.Count(s.Blocks.WallCells) + ", towers " + s.Towers.Towers.Count + ", cell " + valid + ")\n");
                sb.Append("frame\tt\tdrawCalls\tbatches\tsetPass\tvisibleRenderers\tvisiblePS\tparticles\twallRenderersOn   (values = previous frame)\n");
                for (int f = -10; f < 0; f++) { yield return null; sb.Append(Row(f, 0) + "\n"); }
                baseVis = Visible(); long baseDc = draws.LastValue;
                bool ok = s.Blocks.CommitPlacement(valid); float t0 = Time.realtimeSinceStartup; long peak = 0; int peakF = 0; string peakNew = "";
                for (int f = 0; f < 75; f++)
                {
                    if (f == captureAt) { yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "land_capture_f" + f + ".png")); sb.Append("   <capture at frame " + f + ">\n"); }
                    yield return null;
                    sb.Append(Row(f, Time.realtimeSinceStartup - t0) + "\n");
                    if (draws.LastValue > peak) { peak = draws.LastValue; peakF = f; var now = Visible(); now.ExceptWith(baseVis); var names = new List<string>(); foreach (var r in now) if (names.Count < 12) names.Add(r.GetType().Name + " " + Path2(r.transform)); peakNew = now.Count + " new visible renderers vs baseline: " + string.Join(" | ", names); }
                }
                sb.Append("committed=" + ok + " baseline DC=" + baseDc + " peak DC=" + peak + " at frame " + peakF + " (" + (peak - baseDc) + ")\n  " + peakNew + "\n\n");
            }
            // 1. typical board, Build
            ToBuild(); yield return One("typical board, Build, no capture", -1);
            yield return One("typical board, Build, no capture (2nd)", -1);
            yield return One("typical board, Build, CAPTURE at frame 6 (old -vfxcheck method)", 6);
            // 1b. typical board in a quiet Build phase (wave cleared, towers idle)
            yield return KillAll(10f); float tw = Time.realtimeSinceStartup; while (s.Game.State == GameState.Combat && Time.realtimeSinceStartup - tw < 15f) { yield return KillAll(1f); yield return null; }
            ToBuild(); yield return new WaitForSecondsRealtime(2.5f);
            yield return One("typical board, quiet Build (wave " + s.Waves.WaveIndex + ")", -1);
            yield return One("typical board, quiet Build (2nd)", -1);
            // 2. late waves: clear 4 more waves, add walls, then a dense wave (+30 enemies) in Combat
            for (int w = 0; w < 4; w++)
            {
                ToBuild(); if (!s.Waves.StartWave()) break; yield return new WaitForSecondsRealtime(1f);
                tw = Time.realtimeSinceStartup; while (s.Game.State == GameState.Combat && Time.realtimeSinceStartup - tw < 30f) { yield return KillAll(1f); yield return null; }
            }
            ToBuild(); int placed = 0;
            for (int k = 0; k < 10; k++) { EnsureCard(); if (FindCell(out var cc) && s.Blocks.CommitPlacement(cc)) placed++; }
            s.Blocks.MergeWalls(); yield return new WaitForSecondsRealtime(1.2f);
            s.Waves.StartWave(); EnemyData[] types = Resources.FindObjectsOfTypeAll<EnemyData>();
            for (int k = 0; k < 30 && types.Length > 0; k++) { s.Enemies.Spawn(types[k % types.Length], 60, 1, k % Mathf.Max(1, s.grid.Spawns.Count)); if (k % 5 == 4) yield return new WaitForSecondsRealtime(.3f); }
            yield return new WaitForSecondsRealtime(4f);
            sb.Append("late-wave setup: wave index " + s.Waves.WaveIndex + ", +" + placed + " walls placed, " + s.Enemies.Active.Count + " live enemies, state " + s.Game.State + "\n");
            yield return One("late-wave-like, Combat, no capture", -1);
            yield return One("late-wave-like, Combat, no capture (2nd)", -1);
            File.WriteAllText(Path.Combine(dir, "land_probe.txt"), sb.ToString()); Debug.Log("LAND PROBE\n" + sb);
        }
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
                yield return Clean(); for (int f = 0; f < 4; f++) yield return null; long dPre = draws.LastValue;
                s.Blocks.Pinned = false; bool ok = s.Blocks.CommitPlacement(valid); float t0 = Time.realtimeSinceStartup;
                info += "landing dust: drawCalls before commit=" + dPre + " (clean frame)\n";
                // v17.4 check: the dust ring must stay on the placed wall in world space. The ghost is moved 3 cells away right after the
                // t=.06 sample (before the ~0.1 s dust emit) -> the particle centroid must stay at the wall, not follow the ghost.
                var pg = s.Blocks.GetComponentInChildren<StoneSignal.VFX.PlacementGhost>(true); /* block ghost, not the tower ghost */ var parts = new ParticleSystem.Particle[64];
                string DustInfo()
                {
                    if (pg == null || pg.dust == null) return " dust: no PlacementGhost.dust";
                    var ps = pg.dust; int n = ps.GetParticles(parts); var cen = Vector3.zero; float rMax = 0;
                    bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                    for (int i = 0; i < n; i++) cen += world ? parts[i].position : ps.transform.TransformPoint(parts[i].position);
                    if (n > 0) cen /= n;
                    for (int i = 0; i < n; i++) rMax = Mathf.Max(rMax, Vector3.Distance(world ? parts[i].position : ps.transform.TransformPoint(parts[i].position), cen));
                    var pr = ps.GetComponent<ParticleSystemRenderer>(); var c0 = n > 0 ? parts[0].GetCurrentColor(ps) : default;
                    return " dust: particles=" + n + " simSpace=" + ps.main.simulationSpace + " material=" + (pr && pr.sharedMaterial ? pr.sharedMaterial.name : "NONE") +
                           " centroid=" + cen.ToString("F2") + " spread=" + rMax.ToString("F2") + " colour0=#" + ColorUtility.ToHtmlStringRGBA(c0) + " | wall=" + at.ToString("F2") + " ghost=" + pg.transform.position.ToString("F2") +
                           " centroid->wall=" + (n > 0 ? Vector2.Distance(new Vector2(cen.x, cen.z), new Vector2(at.x, at.z)).ToString("F2") : "-") + " centroid->ghost=" + (n > 0 ? Vector2.Distance(new Vector2(cen.x, cen.z), new Vector2(pg.transform.position.x, pg.transform.position.z)).ToString("F2") : "-");
                }
                // v17.5b (art-requested): capture at REAL time since landing = the dust emit frame (v18.2: the drop-in landing callback, PlacementGhost.LandTime after the commit).
                // Measured on the rendered frame: oldest dust particle age (timeScale 1 -> sim time = real time) and wall clock since the land
                // frame. Frames are grabbed into memory and PNG-encoded afterwards so the capture cost does not push the next sample late.
                float commitT = Time.time, landWall = -1f; var grabs = new List<(Texture2D tex, string file)>();
                float Age() { if (pg == null || pg.dust == null) return -1f; int n = pg.dust.GetParticles(parts); float a = -1f; for (int i = 0; i < n; i++) a = Mathf.Max(a, parts[i].startLifetime - parts[i].remainingLifetime); return a; }
                foreach (var t in new[] { .1f, .25f, .4f })
                {
                    float age = -1f;
                    while (true)
                    {
                        yield return new WaitForEndOfFrame(); age = Age();
                        if (age >= 0 && landWall < 0) { landWall = Time.realtimeSinceStartup - age; info += "landing dust: emitted " + (Time.time - commitT - age).ToString("F3") + " s after the commit (landing callback; drop-in touchdown at " + StoneSignal.VFX.PlacementGhost.LandTime.ToString("F3") + " s; callback fired " + (pg != null && pg.LastLandTime >= 0 ? (pg.LastLandTime - commitT).ToString("F3") : "-") + " s after the commit)\n"; }
                        if (age >= t - .008f || Time.time - commitT > 2f) break;
                    }
                    long d = draws.LastValue; string di = DustInfo(); string file = "vfx_landing_dust_land" + Mathf.RoundToInt(t * 100).ToString("000") + ".png";
                    grabs.Add((Grab(), file));
                    info += "landing dust target " + t.ToString("F2") + " s after land: ACTUAL particle age " + age.ToString("F3") + " s, wall clock since land frame " + (Time.realtimeSinceStartup - landWall).ToString("F3") + " s, since commit " + (Time.time - commitT).ToString("F3") + " s (since block touchdown " + (Time.time - commitT - StoneSignal.VFX.PlacementGhost.LandTime).ToString("F3") + " s), frame dt " + Time.deltaTime.ToString("F3") + " -> " + file + " committed=" + ok + " drawCalls=" + d + di + "\n";
                }
                foreach (var g in grabs) { File.WriteAllBytes(Path.Combine(dir, g.file), g.tex.EncodeToPNG()); Destroy(g.tex); }
                if (pg != null && pg.dust != null) { var mn = pg.dust.main; info += "landing dust system: startColor=#" + ColorUtility.ToHtmlStringRGBA(mn.startColor.color) + " startSize=" + mn.startSize.constantMin.ToString("F2") + "-" + mn.startSize.constantMax.ToString("F2") + " maxParticles=" + mn.maxParticles + " emit=landing callback (LandTime " + StoneSignal.VFX.PlacementGhost.LandTime.ToString("F3") + " s)" + "\n"; }
                float tw = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - tw < .8f) yield return null; yield return Clean();
                info += "landing dust: drawCalls 1.1 s+ after commit (walls merged)=" + draws.LastValue + "\n";
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
                    TimeController.SetPaused(true); yield return null; yield return null; yield return Clean();
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
                    bool bad = (tex == null && !StoneSignal.VFX.VfxMaterialRules.IsProcedural(m)) || surf == "OPAQUE"; // v17.4: FXAdditive/FXAlpha are procedural (no texture by design)
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
                yield return Clean(); long d = draws.LastValue, b = batches.LastValue; yield return new WaitForEndOfFrame();
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
        // -corecheck (art core questions, values only, nothing changed): (a) enclosure material + core renderer materials, Bloom actually
        // active (volume + resolved stack) and camera HDR; (b) CoreDamageFx smoke/sparks state in Broken/Critical at NORMAL game speed;
        // (c) v17.5: 4 stage captures from the real hp, hit flash, Critical over 3 consecutive frames, _StatusTint/_StatusRim per renderer;
        // (d) sparks re-entry investigation (Critical -> Intact -> Critical in one frame / consecutive frames).
        static string PathOf(Transform t) { string n = t.name; for (var p = t.parent; p != null && n.Length < 120; p = p.parent) n = p.name + "/" + n; return n; }
        IEnumerator CoreCheck()
        {
            string dir = Path.GetDirectoryName(path); var sb = new System.Text.StringBuilder();
            var cam = s.viewCamera; var home = cam.transform.position; float ortho = cam.orthographicSize, fov = cam.fieldOfView;
            void Zoom(float k) { if (cam.orthographic) cam.orthographicSize = ortho * k; else cam.fieldOfView = fov * k; }
            var fx = s.MapView != null ? s.MapView.CoreFx : null;
            if (fx == null) { File.WriteAllText(Path.Combine(dir, "core_check.txt"), "CoreDamageFx missing\n"); yield break; }
            captureNoUi = true; TimeController.ResetAll(); TimeController.SetSpeed(1);
            string Props(Material m)
            {
                if (m == null) return "NONE";
                var o = m.name + " shader=" + m.shader.name + " queue=" + m.renderQueue + " keywords=[" + string.Join(",", m.shaderKeywords) + "]";
                for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                {
                    var n = m.shader.GetPropertyName(i); var t = m.shader.GetPropertyType(i);
                    if (t == UnityEngine.Rendering.ShaderPropertyType.Color) { var c = m.GetColor(n); o += "\n      " + n + "=#" + ColorUtility.ToHtmlStringRGBA(c) + " (" + c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3") + "," + c.a.ToString("F3") + ")"; }
                    else if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) o += "\n      " + n + "=" + m.GetFloat(n).ToString("0.###");
                    else if (t == UnityEngine.Rendering.ShaderPropertyType.Vector) o += "\n      " + n + "=" + m.GetVector(n).ToString("F3");
                    else if (t == UnityEngine.Rendering.ShaderPropertyType.Texture) { var tx = m.GetTexture(n); o += "\n      " + n + "=" + (tx ? tx.name : "none"); }
                }
                return o;
            }
            // (a)
            var encR = fx.enclosure ? fx.enclosure.GetComponent<MeshRenderer>() : null;
            sb.Append("(a) ENCLOSURE renderer=" + (encR ? PathOf(encR.transform) : "-") + " material=" + (encR ? Props(encR.sharedMaterial) : "-") + "\n");
            foreach (var mm in new[] { fx.intactMesh, fx.crackedMesh, fx.brokenMesh })
            {
                if (mm == null) { sb.Append("    mesh: null\n"); continue; }
                if (!mm.isReadable) { sb.Append("    mesh " + mm.name + ": not readable (vertex colours unknown)\n"); continue; }
                var cols = mm.colors; int nv = cols.Length; float sr = 0, sg = 0, sbb = 0; int r50 = 0, r90 = 0;
                foreach (var c in cols) { sr += c.r; sg += c.g; sbb += c.b; if (c.r > .5f) r50++; if (c.r > .9f) r90++; }
                sb.Append("    mesh " + mm.name + ": verts=" + mm.vertexCount + " colours=" + nv + (nv > 0 ? " meanRGB=(" + (sr / nv).ToString("F2") + "," + (sg / nv).ToString("F2") + "," + (sbb / nv).ToString("F2") + ") R>0.5: " + (100f * r50 / nv).ToString("F0") + "% R>0.9: " + (100f * r90 / nv).ToString("F0") + "% -> emission = albedo x R x _VColorEmission" : " (no vertex colours -> shader default)") + "\n");
            }
            sb.Append("    core renderers (" + (fx.coreRenderers != null ? fx.coreRenderers.Length : 0) + "):\n");
            if (fx.coreRenderers != null) foreach (var r in fx.coreRenderers) if (r) sb.Append("    - " + PathOf(r.transform) + " pos=" + r.transform.position.ToString("F2") + " bounds=" + r.bounds.size.ToString("F2") + " hasStatusTint=" + (r.sharedMaterial && r.sharedMaterial.HasProperty("_StatusTint")) + " hasStatusRim=" + (r.sharedMaterial && r.sharedMaterial.HasProperty("_StatusRim")) + " material=" + Props(r.sharedMaterial) + "\n");
            sb.Append("    " + Bloom());
            var cp = fx.transform.position; cam.transform.position = cp - cam.transform.forward * 6f; Zoom(.4f);
            for (int f = 0; f < 3; f++) yield return null;
            var stack = UnityEngine.Rendering.VolumeManager.instance.stack; var sbl = stack != null ? stack.GetComponent<UnityEngine.Rendering.Universal.Bloom>() : null;
            sb.Append("    resolved volume stack (what the camera renders with): " + (sbl != null ? "bloom active=" + sbl.active + " IsActive=" + sbl.IsActive() + " threshold=" + sbl.threshold.value + " intensity=" + sbl.intensity.value + " scatter=" + sbl.scatter.value + " tint=#" + ColorUtility.ToHtmlStringRGB(sbl.tint.value) + " clamp=" + sbl.clamp.value + " highQualityFiltering=" + sbl.highQualityFiltering.value : "no Bloom in stack") + "\n");
            var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var qrp = QualitySettings.renderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            sb.Append("    camera '" + cam.name + "' allowHDR=" + cam.allowHDR + " | pipeline asset=" + (urpAsset ? urpAsset.name + " supportsHDR=" + urpAsset.supportsHDR : "-") + " | quality '" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "' asset=" + (qrp ? qrp.name + " supportsHDR=" + qrp.supportsHDR : "(uses default)") + " | Time.timeScale=" + Time.timeScale + "\n");
            // (b)
            string Ps(string label, ParticleSystem p)
            {
                if (!p) return "    " + label + ": NOT ASSIGNED\n";
                var pr = p.GetComponent<ParticleSystemRenderer>(); var em = p.emission; var mn = p.main;
                return "    " + label + " " + PathOf(p.transform) + ": activeInHierarchy=" + p.gameObject.activeInHierarchy + " isPlaying=" + p.isPlaying + " isEmitting=" + p.isEmitting + " particleCount=" + p.particleCount +
                       " emission.enabled=" + em.enabled + " rate=" + em.rateOverTime.constant + "(mode " + em.rateOverTime.mode + ") bursts=" + em.burstCount +
                       " maxParticles=" + mn.maxParticles + " looping=" + mn.loop + " playOnAwake=" + mn.playOnAwake + " unscaledTime=" + mn.useUnscaledTime + " simSpace=" + mn.simulationSpace + " scalingMode=" + mn.scalingMode +
                       " startLifetime=" + mn.startLifetime.constant + " startSize=" + mn.startSize.constant + "/" + mn.startSize.constantMax + " startColor=#" + ColorUtility.ToHtmlStringRGBA(mn.startColor.color) +
                       " | renderer enabled=" + (pr && pr.enabled) + " isVisible=" + (pr && pr.isVisible) + " mode=" + (pr ? pr.renderMode.ToString() : "-") + " bounds=" + (pr ? pr.bounds.center.ToString("F2") + " size " + pr.bounds.size.ToString("F2") : "-") +
                       " material=" + (pr && pr.sharedMaterial ? pr.sharedMaterial.name + " shader=" + pr.sharedMaterial.shader.name : "NONE") +
                       " | pos=" + p.transform.position.ToString("F2") + " localPos=" + p.transform.localPosition.ToString("F2") + " lossyScale=" + p.transform.lossyScale.ToString("F3") + " (core at " + cp.ToString("F2") + ")\n";
            }
            // v17.5 status flash props (the v17.4 _HiAmount is always 0 now): core renderers + enclosure (MPB, null when idle)
            var mpb = new MaterialPropertyBlock();
            string Status()
            {
                string o = "";
                var rs = new List<Renderer>(); if (fx.coreRenderers != null) rs.AddRange(fx.coreRenderers); if (encR) rs.Add(encR);
                foreach (var r in rs) if (r) { r.GetPropertyBlock(mpb); o += " " + r.name + " tint " + mpb.GetVector("_StatusTint").ToString("F2") + " rim " + mpb.GetVector("_StatusRim").ToString("F2") + (r.HasPropertyBlock() ? "" : " (no MPB)") + ";"; }
                return o;
            }
            string Sp() => "sparks isPlaying=" + (fx.sparks && fx.sparks.isPlaying) + " isEmitting=" + (fx.sparks && fx.sparks.isEmitting) + " particles=" + (fx.sparks ? fx.sparks.particleCount : -1) + " | smoke isPlaying=" + (fx.smoke && fx.smoke.isPlaying) + " stage=" + fx.Current;
            // (d) v17.5 sparks investigation: why were sparks off at Critical after a stage reset (v17.4 core_check)?
            //     The v17.4 harness forced stages with SetHealth01 while Economy.Changed (kill gold) re-applied the REAL hp (Intact) in between.
            //     Reproduce both orders: reset + re-assert in the SAME frame, and in consecutive frames.
            fx.SetHealth01(.1f); for (int f = 0; f < 20; f++) yield return null;
            sb.Append("(d) critical held 20 frames: " + Sp() + "\n");
            fx.SetHealth01(1f); string afterStop = Sp(); fx.SetHealth01(.1f);
            sb.Append("(d) same frame Critical -> Intact -> Critical: right after Intact: " + afterStop + "\n    right after Critical again: " + Sp() + "\n");
            yield return null; sb.Append("    +1 frame: " + Sp() + "\n"); for (int f = 0; f < 10; f++) yield return null; sb.Append("    +11 frames: " + Sp() + "\n");
            fx.SetHealth01(1f); yield return null; sb.Append("(d) consecutive frames: Intact (+1 frame): " + Sp() + "\n"); fx.SetHealth01(.1f); yield return null;
            sb.Append("    Critical again +1 frame: " + Sp() + "\n"); for (int f = 0; f < 10; f++) yield return null; sb.Append("    +11 frames: " + Sp() + "\n");
            // stages from the REAL hp now (diagnostic: set RunEconomy.HP through its private setter), so Economy.Changed agrees with the forced stage
            var hpProp = typeof(RunEconomy).GetProperty("HP"); int hp0 = s.Economy.HP, maxHp = Mathf.Max(1, s.Economy.MaxHP);
            void SetHp(float h01) { hpProp.SetValue(s.Economy, Mathf.Clamp(Mathf.RoundToInt(h01 * maxHp), 1, maxHp)); fx.SetHealth01(s.Economy.HP / (float)maxHp); }
            int resets = 0; StoneSignal.VFX.CoreDamageFx.Stage want = fx.Current;
            IEnumerator Hold(float seconds) { float h0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - h0 < seconds) { if (fx.Current != want) resets++; yield return null; } }
            foreach (var st in new[] { (1f, "intact"), (.55f, "cracked"), (.3f, "broken"), (.1f, "critical") })
            {
                SetHp(st.Item1); want = fx.Current;
                sb.Append("(b) " + st.Item2 + " hp " + s.Economy.HP + "/" + maxHp + " stage=" + fx.Current + "\n    right after: " + Sp() + "\n");
                resets = 0; yield return Hold(1.5f);
                sb.Append("    after 1.5 s at timeScale " + Time.timeScale + " (stage changed by game events " + resets + " frames): " + Sp() + "\n" + Ps("smoke", fx.smoke) + Ps("sparks", fx.sparks) + "    status:" + Status() + "\n");
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "core_state_" + st.Item2 + ".png"));
            }
            // hit flash from Intact (0.12 s): capture the frame after PlayHit
            Zoom(.25f); SetHp(1f); for (int f = 0; f < 10; f++) yield return null;
            fx.PlayHit(); yield return null; yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "core_hit.png"));
            sb.Append("(c) hit +1 frame t=" + Time.time.ToString("F2") + ":" + Status() + "\n");
            for (int f = 0; f < 3; f++) yield return null; sb.Append("    hit +4 frames:" + Status() + "\n");
            float th = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - th < .3f) yield return null; sb.Append("    hit +0.3 s:" + Status() + "\n");
            // (e) v17.5b Toggle fix (art-requested): Broken -> core HIT (PlayHit emits 3 sparks, isPlaying stays true ~0.8 s) -> Critical
            //     within 0.8 s, through REAL escapes (RunEconomy lowers HP -> SetHealth01, then GameBootstrap's PlayHit), then 3 frames.
            {
                var leak = s.config.waves[0].groups[0].enemy; int dmg = Mathf.Max(1, leak.damageToBase);
                int hc = Mathf.CeilToInt(fx.criticalBelow * maxHp) - 1, hbMax = Mathf.CeilToInt(fx.brokenBelow * maxHp) - 1;
                bool real = hc + dmg <= hbMax && hc >= 1;
                Zoom(.3f); SetHp(real ? (hc + 2 * dmg) / (float)maxHp : .3f); yield return Hold(1f);
                sb.Append("(e) repro Broken->hit->Critical (" + (real ? "real escapes, " + leak.name + " damageToBase=" + dmg : "scripted PlayHit (damage " + dmg + " too big for a 2-step repro)") + "): start hp " + s.Economy.HP + "/" + maxHp + " stage=" + fx.Current + " " + Sp() + "\n");
                if (real) s.Enemies.Spawn(leak).Advance(100); else { SetHp(.3f); fx.PlayHit(); }
                yield return null; sb.Append("    hit 1: hp " + s.Economy.HP + " stage=" + fx.Current + " " + Sp() + "\n");
                float th1 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - th1 < .25f) yield return null;
                sb.Append("    +0.25 s (still inside the 0.8 s spark life): " + Sp() + "\n");
                if (real) s.Enemies.Spawn(leak).Advance(100); else SetHp(.1f);
                sb.Append("    hit 2 -> hp " + s.Economy.HP + " stage=" + fx.Current + " right after: " + Sp() + "\n");
                yield return null; sb.Append("    +1 frame: " + Sp() + "\n");
                float th2 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - th2 < .35f) yield return null;
                sb.Append("    +0.35 s: " + Sp() + "\n");
                for (int k = 0; k < 3; k++)
                {
                    yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "core_critical_repro_frame" + k + ".png"));
                    sb.Append("(e) critical repro frame " + Time.frameCount + " t=" + Time.time.ToString("F3") + ": " + Sp() + "\n");
                    yield return null;
                }
                float th3 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - th3 < 1.2f) yield return null;
                sb.Append("    +1.5 s in Critical (sparks keep emitting): " + Sp() + "\n");
                Zoom(.25f);
            }
            // Critical: 3 consecutive frames
            SetHp(.1f); yield return Hold(.5f);
            for (int k = 0; k < 3; k++)
            {
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "core_critical_frame" + k + ".png"));
                sb.Append("(c) critical frame " + Time.frameCount + " t=" + Time.time.ToString("F3") + ":" + Status() + " " + Sp() + "\n");
                yield return null;
            }
            hpProp.SetValue(s.Economy, hp0);
            fx.SetHealth01(s.Economy.HP / (float)maxHp); yield return null;
            cam.transform.position = home; cam.orthographicSize = ortho; cam.fieldOfView = fov; captureNoUi = false;
            File.WriteAllText(Path.Combine(dir, "core_check.txt"), sb.ToString()); Debug.Log("CORE CHECK\n" + sb);
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
            var uniq = new string(new List<char>(set).ToArray()); cn.TryAddCharacters(uniq, out string notAdded);
            // TryAddCharacters also lists characters that were ALREADY in the font (2nd call in one run listed every char), so the real
            // missing set is what the font still cannot render afterwards (dynamic: tries the TTF).
            string missing = ""; foreach (char c in uniq) if (!cn.HasCharacter(c, false, true)) missing += c;
            int pages = 0; if (cn.atlasTextures != null) foreach (var tx in cn.atlasTextures) if (tx != null) pages++;
            return "CN font " + cn.name + ": unique CJK/fullwidth chars in UI strings=" + uniq.Length + " glyphs in font now=" + cn.characterTable.Count +
                   " atlas pages=" + pages + " (" + cn.atlasWidth + "x" + cn.atlasHeight + ", padding " + cn.atlasPadding + ", sampling " + cn.faceInfo.pointSize + ") multiAtlas=" + cn.isMultiAtlasTexturesEnabled +
                   " missing=[" + missing + "]\n";
        }
        bool captureNoUi;
        int capFrame = -10;
        // Capture() renders the camera twice more into a RenderTexture, so the profiler's Draw Calls Count for that frame is ~3x the real
        // frame (e.g. vfx landing dust 69 -> 159). LastValue reports the previous frame, and the end-of-frame renders can be booked on the NEXT
        // frame's counters (measured: capFrame+1 still read 163 once) -> read only from capFrame+4 on (two clean frames in between).
        IEnumerator Clean() { while (Time.frameCount < capFrame + 4) yield return null; }
        /// Capture into memory (no PNG encode): the caller encodes later (timing-critical sequences).
        Texture2D Grab()
        {
            capFrame = Time.frameCount;
            var cam = s.viewCamera; int W = Screen.width, H = Screen.height; var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            var hidden = new List<Canvas>(); if (captureNoUi) foreach (var c in FindObjectsOfType<Canvas>()) if (c.enabled) { c.enabled = false; hidden.Add(c); }
            var canvases = captureNoUi ? new Canvas[0] : FindObjectsOfType<Canvas>(); var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { modes[i] = canvases[i].renderMode; canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = cam; canvases[i].planeDistance = cam.nearClipPlane + .3f - Mathf.Clamp(canvases[i].sortingOrder, 0, 20) * .01f; }
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
            cam.targetTexture = null; RenderTexture.active = null; rt.Release(); Destroy(rt);
            for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
            foreach (var c in hidden) c.enabled = true;
            return tex;
        }
        void Capture(string path)
        {
            capFrame = Time.frameCount;
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
