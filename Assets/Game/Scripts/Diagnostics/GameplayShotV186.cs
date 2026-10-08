#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace StoneSignal
{
    // v18.6 diagnostics: battle frames (-battleshot), Seismic attack / VFX probe (-seismicprobe), Build / Combat draw calls (-dc186).
    public sealed partial class GameplayShot
    {
        static bool Arg(string a) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0;
        static int ArgInt(string a, int def) { var v = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(v, a); return i >= 0 && i + 1 < v.Length && int.TryParse(v[i + 1], out int r) ? r : def; }
        IEnumerator V186()
        {
            if (Arg("-battleshot")) return BattleShot();
            if (Arg("-seismicprobe")) return SeismicProbe();
            if (Arg("-dc186")) return Dc186();
            return V186Extra();
        }
        IEnumerator V186Extra() => Arg("-combat186") ? Combat186() : V19(); // v19 / v18.6b diagnostics (GameplayShotV19.cs)

        // ================= v18.6 combat-phase HUD tests + shots (-combat186 [-touch] [-seed N]) =================
        /// Screen px rect of a UI element, via the root canvas' local space (valid even in the frame a Grab() switched the canvases to
        /// ScreenSpaceCamera and back - world corners are stale until the next canvas update).
        static Rect ScreenRect(RectTransform r)
        {
            var c = new Vector3[4]; r.GetWorldCorners(c); float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            var cv = r.GetComponentInParent<Canvas>(); var root = cv != null ? cv.rootCanvas : null; var rt = root != null ? (RectTransform)root.transform : null;
            foreach (var w in c)
            {
                Vector2 v = w; if (rt != null) { Vector2 l = rt.InverseTransformPoint(w); v = (l + Vector2.Scale(rt.rect.size, rt.pivot)) * root.scaleFactor; }
                x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y); x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }
        static Texture2D Crop(Texture2D src, Rect r, int scale)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.xMin), 0, src.width - 1), y0 = Mathf.Clamp(Mathf.RoundToInt(r.yMin), 0, src.height - 1);
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, src.width - x0), h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, src.height - y0);
            var px = src.GetPixels(x0, y0, w, h); var dst = new Texture2D(w * scale, h * scale, TextureFormat.RGB24, false); var o = new Color[w * scale * h * scale];
            for (int y = 0; y < h * scale; y++) for (int x = 0; x < w * scale; x++) o[y * w * scale + x] = px[(y / scale) * w + x / scale];
            dst.SetPixels(o); dst.Apply(false); return dst;
        }
        void Save(Texture2D t, string name) { File.WriteAllBytes(Path.Combine(Dir, name), t.EncodeToPNG()); }
        static Color Mean(Texture2D t, Rect r)
        {
            var c = Crop(t, r, 1); var px = c.GetPixels(); Color m = Color.black; foreach (var p in px) m += p; Object.Destroy(c); return px.Length > 0 ? m / px.Length : m;
        }
        /// Pad (wall footprint, no tower yet) for a tower built later in combat.
        Vector2Int BsPad(int idx, StringBuilder sb)
        {
            var grid = s.grid; var size = TowerManager.SizeOf(s.config.towers[idx], 0); Vector2Int bo = new Vector2Int(-1, -1); float best = float.MaxValue;
            for (int x = 0; x < grid.width; x++) for (int y = 0; y < grid.height; y++)
            {
                var o = new Vector2Int(x, y); var cells = grid.Footprint(o, size);
                if (!cells.TrueForAll(c => grid.InBounds(c) && grid.CanPlace(c)) || cells.Exists(OnRoute)) continue;
                int adj = 0; foreach (var c in cells) foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }) if (grid.InBounds(c + d) && OnRoute(c + d)) adj++;
                if (adj == 0) continue; var ctr = grid.FootprintCenter(o, size); float near = 99f; foreach (var t in bsTowers) near = Mathf.Min(near, (t - ctr).magnitude); if (near < 2f) continue;
                float sc = -adj + (ctr - grid.CoreCenter).magnitude * .1f; if (sc < best && s.Validator.ValidatePlacement(cells) == null) { best = sc; bo = o; }
            }
            if (bo.x >= 0) { var pad = grid.Footprint(bo, size); grid.Commit(pad, CellState.Blocked); foreach (var c in pad) s.Blocks.SpawnWall(c); s.Blocks.MergeWalls(); }
            sb.Append("  pad for a combat tower (" + s.config.towers[idx].displayName + ") at " + bo + "\n"); return bo;
        }
        int Blocked() { int n = 0; for (int x = 0; x < s.grid.width; x++) for (int y = 0; y < s.grid.height; y++) if (s.grid.Get(new Vector2Int(x, y)) == CellState.Blocked) n++; return n; }

        IEnumerator Combat186()
        {
            var sb = new StringBuilder(); int nPass = 0, nFail = 0; int seed = ArgInt("-seed", 37);
            void Check(bool ok, string what) { if (ok) nPass++; else nFail++; sb.Append(ok ? "PASS " : "FAIL ").Append(what).Append('\n'); }
            var ui = FindObjectOfType<GameUI>(); var pic = PlacementInputController.Instance; var ch = s.config.combatHud ?? new CombatHudStyle();
            float hs = HudScaler.ScaleFor(Screen.width, Screen.height);
            yield return BattleSetup(sb, seed);
            sb.Append("rules: allowCombatBlocks=" + s.config.allowCombatBlocks + " allowCombatTowers=" + s.config.allowCombatTowers + " HUD scale " + hs.ToString("F3") + " UiWhite=" + (UiWhite.Get(s.config.palette != null ? s.config.palette.art : null) != null) + " UiDisable material=" + (UiDisable.SharedMaterial != null) + "\n");
            var padO = BsPad(1, sb);
            { var NR = RuneRules.NoRune; var spec = new List<(int, int)>(); for (int i = 0; i < Mathf.Min(6, s.config.blocks.Length); i++) spec.Add((i, NR)); yield return SetHand(spec); yield return WaitRt(.8f); }
            // ---- Build baseline
            Check(s.Blocks.PhaseAllows && ui.DebugBattleGroup.alpha > .99f && !ui.DebugPlate.gameObject.activeSelf && ui.DebugPileDim < .01f,
                "BUILD: walls allowed, BATTLE shown (alpha " + ui.DebugBattleGroup.alpha.ToString("F2") + "), plate hidden, pile undimmed (" + ui.DebugPileDim.ToString("F2") + "); fan active " + ui.FanActive + " (" + ui.FanCards().Count + " groups)");
            yield return Clean(); yield return new WaitForEndOfFrame(); var texBuild = Grab();
            var towerRects = new List<Rect>(); foreach (var h in ui.DebugHandCards()) if (h.tower) { var face = h.rt.Find("Frame") as RectTransform; var r = ScreenRect(face != null ? face : h.rt); towerRects.Add(Rect.MinMaxRect(r.xMin + r.width * .2f, r.yMin + r.height * .62f, r.xMax - r.width * .2f, r.yMax - r.height * .06f)); }
            // ---- BATTLE -> plate transition (frames, bottom-right crop)
            Rect brCrop = Rect.MinMaxRect(Screen.width - 620 * hs, 0, Screen.width, 360 * hs);
            var frames = new List<(Texture2D, string)>(); var tl = new StringBuilder();
            s.Waves.StartWave(); float tIn = Time.unscaledTime;
            while (Time.unscaledTime - tIn < .32f) { yield return new WaitForEndOfFrame(); var g = Grab(); int ms = Mathf.RoundToInt((Time.unscaledTime - tIn) * 1000); frames.Add((Crop(g, brCrop, 1), "trans_in_" + Res + "_" + ms.ToString("000") + "ms.png")); tl.Append("  in +" + ms + " ms: " + ui.DebugCombatHud + "\n"); Destroy(g); yield return null; }
            Check(ui.DebugPlateGroup.alpha > .99f && Mathf.Abs(ui.DebugPlate.localScale.x - 1f) < .001f && ui.DebugBattleGroup.alpha < .01f && !ui.DebugBattleGroup.blocksRaycasts,
                "BATTLE -> plate: " + ui.DebugCombatHud + " (spec: button 0.92 + fade .12 s, plate fade .15 s 0.96 -> 1)");
            {
                var pt = ui.DebugPlate; var bt = (RectTransform)ui.DebugBattle.transform; // canvas units (ref px), scale ignored
                Vector2 pc = (Vector2)pt.localPosition + pt.rect.center, bc = (Vector2)bt.localPosition + bt.rect.center;
                Check((pc - bc).magnitude < .5f && Mathf.Abs(pt.rect.width - bt.rect.width) < .5f && Mathf.Abs(pt.rect.height - bt.rect.height * ch.plateHeightFrac) < .5f && !ui.DebugPlateGroup.blocksRaycasts,
                    "plate " + pt.rect.size + " centre " + pc + " = BATTLE " + bt.rect.size + " centre " + bc + " (x" + ch.plateHeightFrac + " high, vertically centred, ref px); raycast " + ui.DebugPlateGroup.blocksRaycasts);
            }
            yield return WaitRt(.4f);
            // ---- walls rejected in combat, towers still built for gold
            {
                int b0 = Blocked(), rem0 = s.Blocks.Remaining; s.Blocks.SelectCard(0); Vector2Int a = new Vector2Int(-1, -1);
                for (int x = 1; x < s.grid.width - 1 && a.x < 0; x++) for (int y = 1; y < s.grid.height - 1; y++) { var q = new Vector2Int(x, y); if (s.Blocks.CellsAt(q).TrueForAll(c => s.grid.InBounds(c) && s.grid.CanPlace(c))) { a = q; break; } }
                bool placed = a.x >= 0 && s.Blocks.CommitPlacement(a);
                Check(!s.Blocks.PhaseAllows && !placed && Blocked() == b0 && s.Blocks.Remaining == rem0, "COMBAT: wall placement rejected (CommitPlacement at " + a + " = " + placed + ", blocked cells " + b0 + " -> " + Blocked() + ", hand " + rem0 + " -> " + s.Blocks.Remaining + ")");
                int g0 = s.Economy.Gold, t0 = s.Towers.Towers.Count, cost = s.Towers.Cost(s.config.towers[1]);
                bool built = padO.x >= 0 && s.Towers.TryBuild(padO, 1, 0); s.Towers.Select(-1);
                Check(built && s.Towers.Towers.Count == t0 + 1 && s.Economy.Gold == g0 - cost, "COMBAT: tower built for gold (TryBuild " + s.config.towers[1].displayName + " at " + padO + " = " + built + ", towers " + t0 + " -> " + s.Towers.Towers.Count + ", gold " + g0 + " -> " + s.Economy.Gold + ", cost " + cost + ")");
            }
            // ---- draw disabled in combat, pile dimmed
            {
                bool drew = s.Draw(); yield return WaitRt(.3f);
                var pile = ui.transform.GetComponentInChildren<StoneSignal.VFX.DrawPileUI>(true); var cg = pile.GetComponent<CanvasGroup>();
                var es = UnityEngine.EventSystems.EventSystem.current; var hits = new List<UnityEngine.EventSystems.RaycastResult>(); var pc = ScreenRect((RectTransform)pile.transform).center;
                es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = pc }, hits); bool pileHit = hits.Exists(h => h.gameObject.transform.IsChildOf(pile.transform));
                var top = pile.top.color;
                Check(!drew && cg != null && !cg.blocksRaycasts && !pileHit && ui.DebugPileDim > .99f && pile.statusLabel.alpha < .01f,
                    "COMBAT: draw disabled (Draw()=" + drew + ", pile raycast hit " + pileHit + ", blocksRaycasts " + (cg != null && cg.blocksRaycasts) + "), dim " + ui.DebugPileDim.ToString("F2") + ", label '" + pile.statusLabel.text + "' alpha " + pile.statusLabel.alpha.ToString("F2") + ", top colour " + top + " (x" + ch.pileMultiply + ")");
            }
            // ---- disabled style + purple tint gone
            {
                string dims = ""; bool blockOk = true, towerOk = true; foreach (var d in ui.DebugDims()) { dims += (d.tower ? "T" : "B") + d.index + "=" + d.k.ToString("F2") + "/" + d.gray.ToString("F2") + "/" + d.brightness.ToString("F2") + " "; if (!d.tower && (Mathf.Abs(d.gray - ch.disabledGray) > .01f || Mathf.Abs(d.brightness - ch.disabledBrightness) > .01f)) blockOk = false; if (d.tower && d.k > .01f && d.index != -1 && s.Towers.Cost(s.config.towers[d.index]) <= s.Economy.Gold) towerOk = false; }
                Check(blockOk && towerOk, "COMBAT disabled style: block row grey " + ch.disabledGray + " / brightness " + ch.disabledBrightness + ", affordable tower cards undimmed (k/grey/brightness: " + dims + ")");
                bool crOk = true, hueOk = true; string bad = "";
                foreach (var h in ui.DebugHandCards()) foreach (var g in h.rt.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    var cr = g.canvasRenderer.GetColor(); if ((cr - Color.white).maxColorComponent > .002f || cr.a < .999f) { crOk = false; bad += g.name + " cr " + cr + "; "; }
                    var c = g.color; if (Mathf.Abs(c.r - c.g) > .002f || Mathf.Abs(c.g - c.b) > .002f) { hueOk = false; bad += g.name + " col " + c + "; "; }
                }
                Check(crOk && hueOk, "purple tint gone: every hand Image has CanvasRenderer colour (1,1,1,1) and a hue-free vertex colour " + (bad.Length > 0 ? "- " + bad.Substring(0, Mathf.Min(400, bad.Length)) : ""));
                yield return Clean(); yield return new WaitForEndOfFrame(); var texCombat = Grab();
                string mc = ""; float worst = 0f;
                for (int i = 0; i < towerRects.Count; i++) { var mb = Mean(texBuild, towerRects[i]); var mcb = Mean(texCombat, towerRects[i]); float dlt = Mathf.Max(Mathf.Abs(mb.r - mcb.r), Mathf.Max(Mathf.Abs(mb.g - mcb.g), Mathf.Abs(mb.b - mcb.b))); worst = Mathf.Max(worst, dlt); mc += "T" + i + " build " + (Color32)mb + " combat " + (Color32)mcb + "; "; }
                Check(worst < 4f / 255f, "tower card faces in combat = Build (full red face, no tint): worst channel delta " + (worst * 255f).ToString("F1") + "/255 - " + mc);
                Save(texCombat, "combat_" + Res + "_disabled_row.png"); Destroy(texCombat);
            }
            // ---- blocked wall drag -> toast, card does not move; tap still expands the fan
            {
                var fc = ui.FanCards(); var holder = fc[fc.Count - 1].holder; var card = holder.Find("Card"); var cp = card.GetComponent<CardPointer>();
                Vector2 pos = ScreenRect((RectTransform)card).center; var before = holder.anchoredPosition; int sel0 = s.Blocks.Hand.Selected, n0 = pic.BlockedNotices;
                var es = UnityEngine.EventSystems.EventSystem.current;
                cp.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(es) { position = pos }); PointerInput.Inject(pos, true, true, false); yield return null;
                for (int k = 1; k <= 6; k++) { PointerInput.Inject(pos + new Vector2(0, 14 * k * hs), false, true, false); yield return null; }
                var after = holder.anchoredPosition; string shown = ui.NoticeShown; var mstate = pic.Machine.State;
                yield return WaitRt(.25f); yield return new WaitForEndOfFrame(); Capture(Path.Combine(Dir, "toast_" + Res + "_wall_drag_in_combat.png"));
                PointerInput.Inject(pos + new Vector2(0, 84 * hs), false, false, true); yield return null; PointerInput.ClearInjection(); yield return null;
                Check(shown == StoneSignal.UI.Loc.Notice(StoneSignal.UI.Loc.CombatNoWalls) && pic.BlockedNotices == n0 + 1 && (after - before).sqrMagnitude < .01f && mstate == PlacementState.Idle && s.Blocks.Hand.Selected == sel0,
                    "COMBAT wall drag: toast '" + shown + "' (notices +" + (pic.BlockedNotices - n0) + "), card moved " + (after - before).magnitude.ToString("F2") + " px, machine " + mstate + ", selection " + sel0 + " -> " + s.Blocks.Hand.Selected);
                if (ui.FanActive)
                {
                    ui.DebugSetFan(false); yield return WaitRt(.4f); fc = ui.FanCards(); holder = fc[0].holder; card = holder.Find("Card"); cp = card.GetComponent<CardPointer>(); pos = ScreenRect((RectTransform)card).center;
                    yield return WaitRt(1.1f); // past the 1 s toast de-dupe
                    cp.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(es) { position = pos }); PointerInput.Inject(pos, true, true, false); yield return null;
                    PointerInput.Inject(pos, false, false, true); yield return null; PointerInput.ClearInjection(); yield return null;
                    Check(ui.FanExpanded && pic.Machine.State == PlacementState.Idle, "COMBAT tap on a dimmed block card expands the fan (expanded " + ui.FanExpanded + ", last event '" + ui.FanLastEvent + "', machine " + pic.Machine.State + ")");
                    yield return WaitRt(.3f); ui.DebugSetFan(false);
                }
                else sb.Append("NOTE tap-to-expand: block row not fanned (" + fc.Count + " groups)\n");
            }
            // ---- gold ring: fly target = coin centre, ring centred on the coin, max radius capped (orb)
            {
                var coin = ui.GoldTarget; var ring = ui.DebugGoldRing; var counter = ui.GoldCounter; var orb = ui.DebugCoreOrb;
                var cr = ScreenRect(coin); Vector2 tgt = coin.position; var rr = ScreenRect(ring); var orr = ScreenRect(orb);
                var cvs = coin.GetComponentInParent<Canvas>().rootCanvas; float sf = cvs.scaleFactor; var rootRt = (RectTransform)cvs.transform;
                Vector2 tgtPx = ((Vector2)rootRt.InverseTransformPoint(coin.position) + Vector2.Scale(rootRt.rect.size, rootRt.pivot)) * sf;
                float ringR = ring.rect.width * .5f * .92f * counter.ringMaxScale * sf, gap = (cr.center - orr.center).magnitude - orr.width * .5f, pillH = ui.DebugGoldPill.rect.height * sf;
                Check((tgtPx - cr.center).magnitude < 1f && (rr.center - cr.center).magnitude < 1f && ring.parent == ui.DebugGoldPill && ring.GetSiblingIndex() > coin.GetSiblingIndex() && ringR <= ch.goldRingCapPillFrac * pillH + .5f,
                    "gold ring: fly target " + tgtPx.ToString("F1") + " = coin centre " + cr.center.ToString("F1") + ", ring centre " + rr.center.ToString("F1") + ", above the pill (child, after the coin); max scale " + counter.ringMaxScale.ToString("F3") + " -> visible radius " + ringR.ToString("F1") + " px (cap " + ch.goldRingCapPillFrac + " x pill " + pillH.ToString("F0") + " = " + (ch.goldRingCapPillFrac * pillH).ToString("F1") + "; orb gap " + gap.ToString("F1") + " px; art 1.5 would be " + (ring.rect.width * .5f * .92f * 1.5f * sf).ToString("F1") + ")");
                s.Economy.AddGold(5); counter.Add(5); float tr = Time.unscaledTime; // = one kill-gold arrival (RewardFlyFx -> counter.Add)
                foreach (var at in new[] { .1f, .3f })
                {
                    while (Time.unscaledTime - tr < at) yield return null; yield return new WaitForEndOfFrame(); var g = Grab();
                    var cz = Crop(g, Rect.MinMaxRect(orr.xMin - 10, Mathf.Min(orr.yMin, cr.yMin) - 60, cr.xMax + 260 * sf, orr.yMax + 10), 2); Save(cz, "goldring_" + Res + "_t" + Mathf.RoundToInt(at * 1000) + "ms.png"); Destroy(cz); Destroy(g);
                }
            }
            // ---- remaining count: kill pops, split adds (no pop), plate = Waves.Remaining / Total
            {
                float wt = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - wt < 8f && s.Enemies.Active.Count < 2) yield return null;
                Enemy pick = null; foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn) { pick = e; break; }
                if (pick != null)
                {
                    int r0 = s.Waves.Remaining, t0 = s.Waves.Total; var d = pick.Data; var oc = d.splitChild; int on = d.splitCount; float osc = d.splitChildScale;
                    d.splitChild = d; d.splitCount = 2; d.splitChildScale = .7f; pick.TakeDamage(pick.HP + 9999f); d.splitChild = oc; d.splitCount = on; d.splitChildScale = osc;
                    yield return null; yield return null;
                    string num = ui.DebugPlateNumber.text; float sc = ui.DebugPlateNumber.rectTransform.localScale.x, fill = ui.DebugPlateFill.anchorMax.x;
                    Check(s.Waves.Remaining == r0 + 1 && s.Waves.Total == t0 + 2 && num == s.Waves.Remaining.ToString() && Mathf.Abs(fill - s.Waves.Remaining / (float)s.Waves.Total) < .001f && Mathf.Abs(sc - 1f) < .001f,
                        "remaining with a split (killed enemy -> 2 children): Remaining " + r0 + " -> " + s.Waves.Remaining + " (expect +1), Total " + t0 + " -> " + s.Waves.Total + " (expect +2), plate '" + num + "' fill " + fill.ToString("F3") + ", N scale " + sc.ToString("F3") + " (no pop on a split)");
                    Enemy k2 = null; foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn) { k2 = e; break; }
                    if (k2 != null)
                    {
                        int r1 = s.Waves.Remaining; k2.TakeDamage(k2.HP + 9999f); yield return null; yield return null; float sc2 = ui.DebugPlateNumber.rectTransform.localScale.x;
                        yield return WaitRt(.2f); float sc3 = ui.DebugPlateNumber.rectTransform.localScale.x;
                        Check(s.Waves.Remaining == r1 - 1 && sc2 > 1.01f && Mathf.Abs(sc3 - 1f) < .001f, "remaining on a kill: " + r1 + " -> " + s.Waves.Remaining + ", N pop " + sc2.ToString("F3") + " -> " + sc3.ToString("F3") + " (spec 1.15 -> 1 over .12 s)");
                    }
                }
                else Check(false, "remaining: no live enemy to split");
            }
            // ---- HP bars above numbers; 3 consecutive hits on one enemy (close-up)
            {
                var bars = EnemyHpBarsUI.Instance; Canvas barCv = bars != null ? bars.GetComponentInChildren<Canvas>() : null; Canvas numCv = null;
                foreach (var c in FindObjectsOfType<Canvas>()) if (c.name == "[DamageNumbers UI]") numCv = c;
                Enemy pick = null; float bestD = float.MaxValue; var mid = new Vector2(Screen.width * .5f, Screen.height * .55f);
                float wt = Time.realtimeSinceStartup;
                while (pick == null && Time.realtimeSinceStartup - wt < 10f)
                {
                    foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn && e.HP > 30f && bars != null && bars.TryGetBar(e, out var br)) { float dd = (br.center - mid).sqrMagnitude; if (dd < bestD) { bestD = dd; pick = e; } }
                    if (pick == null) yield return null;
                }
                bool cubesOff = pick != null && pick.HpBarAnchor != null && System.Array.TrueForAll(pick.HpBarAnchor.GetComponentsInChildren<Renderer>(true), r => !r.enabled);
                Check(barCv != null && numCv != null && barCv.sortingOrder > numCv.sortingOrder && barCv.sortingOrder < 0 && cubesOff,
                    "HP bars canvas order " + (barCv != null ? barCv.sortingOrder : 0) + " > damage numbers " + (numCv != null ? numCv.sortingOrder : 0) + " (both under the HUD 0); 3D bar cubes hidden " + cubesOff + ", bars visible " + (bars != null ? bars.VisibleBars : 0));
                if (pick != null)
                {
                    TimeController.SetPaused(true); yield return null; yield return null;
                    pick.TakeDamage(3f); yield return WaitRt(.1f); pick.TakeDamage(4f); yield return WaitRt(.1f); pick.TakeDamage(5f, StoneSignal.VFX.DamageKind.Physical, true); yield return WaitRt(.06f);
                    yield return new WaitForEndOfFrame(); var tex = Grab(); bars.TryGetBar(pick, out var bar);
                    var nums = new List<StoneSignal.VFX.UiDamageNumber>(); foreach (var n in FindObjectsOfType<StoneSignal.VFX.UiDamageNumber>()) if (n.gameObject.activeSelf && n.target == pick) nums.Add(n);
                    nums.Sort((a, b) => b.age.CompareTo(a.age)); string nl = ""; bool alt = nums.Count == 3, sizes = nums.Count == 3, above = true;
                    for (int i = 0; i < nums.Count; i++)
                    {
                        var n = nums[i]; var nr = ScreenRect(n.rt); nl += "#" + i + " '" + n.text.text + "' side " + n.side + " px " + n.text.fontSize.ToString("F1") + " age " + n.age.ToString("F2") + " rect " + nr + "; ";
                        if (i > 0 && n.side == nums[i - 1].side) alt = false;
                        if (Mathf.Abs(n.text.fontSize - (n.crit ? ch.numberCritPx : ch.numberNormalPx) * hs) > .05f) sizes = false;
                        if (n.rt.position.y < bar.yMax + ch.numberSpawnAbovePx * hs - .5f) above = false;
                    }
                    Check(alt && sizes && above, "3 hits on one enemy: alternate sides " + alt + ", sizes 24 / 30 (crit) x HUD " + sizes + ", all >= 14 px above the bar top " + above + " | bar " + bar + " | " + nl);
                    var crop = Rect.MinMaxRect(bar.center.x - 170 * hs, bar.yMin - 60 * hs, bar.center.x + 170 * hs, bar.yMax + 120 * hs);
                    var cz = Crop(tex, crop, 3); Save(cz, "hits3_" + Res + "_closeup.png"); Destroy(cz); Save(tex, "hits3_" + Res + "_full.png"); Destroy(tex);
                    TimeController.SetPaused(false);
                }
            }
            // ---- plate at <= 3 remaining (close-up)
            {
                TimeController.SetSpeed(2); float wt = Time.realtimeSinceStartup; bool got = false;
                while (s.Game.State == GameState.Combat && Time.realtimeSinceStartup - wt < 70f)
                {
                    int r = s.Waves.Remaining;
                    if (r > 0 && r <= 3 && !got)
                    {
                        TimeController.SetPaused(true); yield return WaitRt(.3f); yield return new WaitForEndOfFrame(); var tex = Grab(); var pr = ScreenRect(ui.DebugPlate);
                        var cz = Crop(tex, Rect.MinMaxRect(pr.xMin - 24 * hs, pr.yMin - 24 * hs, pr.xMax + 24 * hs, pr.yMax + 24 * hs), 3); Save(cz, "plate_" + Res + "_3_remaining.png"); Destroy(cz); Save(tex, "plate_" + Res + "_3_remaining_full.png"); Destroy(tex);
                        var col = ui.DebugPlateNumber.color;
                        Check(ui.DebugPlateNumber.text == s.Waves.Remaining.ToString() && (col - ch.lowColor).maxColorComponent < .01f, "plate at " + s.Waves.Remaining + " remaining: N '" + ui.DebugPlateNumber.text + "' colour " + (Color32)col + " (spec #FF8A3D at <= 3), fill " + ui.DebugPlateFill.anchorMax.x.ToString("F3") + " = " + s.Waves.Remaining + "/" + s.Waves.Total + ", dot alpha " + ui.DebugPlateDot.color.a.ToString("F2"));
                        got = true; TimeController.SetPaused(false); TimeController.SetSpeed(3);
                    }
                    yield return null;
                }
                if (!got) Check(false, "plate at <= 3 remaining: never reached (state " + s.Game.State + ", remaining " + s.Waves.Remaining + ")");
            }
            // ---- plate -> BATTLE (wave end): plate fades on Reward, BATTLE bounces on Build
            {
                float wt = Time.realtimeSinceStartup; while (s.Game.State == GameState.Combat && Time.realtimeSinceStartup - wt < 60f) yield return null;
                TimeController.ResetAll(); TimeController.SetSpeed(1);
                float t1 = Time.unscaledTime;
                while (Time.unscaledTime - t1 < .2f) { yield return new WaitForEndOfFrame(); var g = Grab(); int ms = Mathf.RoundToInt((Time.unscaledTime - t1) * 1000); frames.Add((Crop(g, brCrop, 1), "trans_out_" + Res + "_" + s.Game.State.ToString().ToLower() + "_" + ms.ToString("000") + "ms.png")); tl.Append("  out(" + s.Game.State + ") +" + ms + " ms: " + ui.DebugCombatHud + "\n"); Destroy(g); yield return null; }
                Check(!ui.DebugPlate.gameObject.activeSelf || ui.DebugPlateGroup.alpha < .01f, "wave end (" + s.Game.State + "): plate faded out - " + ui.DebugCombatHud);
                if (s.Game.State == GameState.Reward) s.Rewards.Choose(0);
                wt = Time.realtimeSinceStartup; while (s.Game.State != GameState.Build && Time.realtimeSinceStartup - wt < 8f) yield return null;
                float t2 = Time.unscaledTime, peak = 0f, minS = 9f; Texture2D bounce = null; float bounceErr = 9f;
                while (Time.unscaledTime - t2 < .36f)
                {
                    yield return new WaitForEndOfFrame(); float bs = ui.DebugBattle.transform.localScale.x; peak = Mathf.Max(peak, bs); minS = Mathf.Min(minS, bs);
                    var g = Grab(); int ms = Mathf.RoundToInt((Time.unscaledTime - t2) * 1000); var cz = Crop(g, brCrop, 1); frames.Add((cz, "trans_out_" + Res + "_build_" + ms.ToString("000") + "ms.png")); tl.Append("  build +" + ms + " ms: " + ui.DebugCombatHud + "\n");
                    if (Mathf.Abs(bs - 1.08f) < bounceErr) { bounceErr = Mathf.Abs(bs - 1.08f); if (bounce != null) Destroy(bounce); bounce = g; } else Destroy(g);
                    yield return null;
                }
                if (bounce != null) { Save(bounce, "bounce_" + Res + "_battle_peak.png"); var pr = ScreenRect((RectTransform)ui.DebugBattle.transform); var cz = Crop(bounce, Rect.MinMaxRect(pr.xMin - 40 * hs, pr.yMin - 30 * hs, pr.xMax + 40 * hs, pr.yMax + 30 * hs), 2); Save(cz, "bounce_" + Res + "_battle_peak_closeup.png"); Destroy(cz); Destroy(bounce); }
                Check(ui.DebugBattleGroup.alpha > .99f && Mathf.Abs(ui.DebugBattle.transform.localScale.x - 1f) < .001f && peak > 1.06f && peak < 1.09f && ui.DebugBattle.interactable,
                    "Build: BATTLE back (bounce min " + minS.ToString("F3") + " peak " + peak.ToString("F3") + ", spec 0.9 -> 1.08 -> 1.0 over .25 s), " + ui.DebugCombatHud);
            }
            foreach (var f in frames) { Save(f.Item1, f.Item2); Destroy(f.Item1); }
            Destroy(texBuild);
            sb.Append("transition timeline:\n").Append(tl);
            sb.Insert(0, "COMBAT186 TESTS: " + nPass + " pass, " + nFail + " fail (" + Res + ", seed " + seed + ")\n");
            File.WriteAllText(Path.Combine(Dir, "combat186_" + Res + ".txt"), sb.ToString()); Debug.Log("COMBAT186 TESTS: " + nPass + " pass, " + nFail + " fail");
        }
        string Res => Screen.width + "x" + Screen.height;
        string Dir => Path.GetDirectoryName(path);
        int RouteLen() { int n = 0; foreach (var p in s.Paths.CurrentPaths) n += p.Count; return n; }

        // ---- battle board (seed 37 style): hand cards as walls on the route, 4 towers on wall pads next to the route ----------------------
        bool BsPlaceWall(StringBuilder sb)
        {
            var grid = s.grid; Vector3 core = grid.CoreCenter;
            int n = s.Blocks.Remaining; float best = float.MaxValue; int bc = -1, br = 0; Vector2Int ba = default;
            for (int ci = 0; ci < n; ci++)
            {
                s.Blocks.SelectCard(ci);
                for (int r = 0; r < 4; r++) for (int x = 1; x < grid.width - 1; x++) for (int y = 1; y < grid.height - 1; y++)
                {
                    var a = new Vector2Int(x, y); var cells = s.Blocks.CellsAt(a, r); int on = 0; float d = 0f; bool okc = true;
                    foreach (var c in cells) { if (!grid.InBounds(c) || !grid.CanPlace(c)) { okc = false; break; } if (OnRoute(c)) on++; d += (grid.ToWorld(c) - core).magnitude; }
                    if (!okc || on == 0) continue; d /= cells.Count; if (d < 2.6f) continue;
                    float sc = -on * 1.5f + Mathf.Abs(d - 4.2f) * .6f + ((x * 7 + y * 13 + ci * 5 + r * 3) % 10) * .03f;
                    if (sc < best) { best = sc; bc = ci; br = r; ba = a; }
                }
            }
            if (bc < 0) return false;
            s.Blocks.SelectCard(bc); for (int r = 0; r < br; r++) s.Blocks.Rotate();
            if (s.Blocks.ValidatePlacement(ba) != null)
            {
                for (int x = 1; x < grid.width - 1; x++) for (int y = 1; y < grid.height - 1; y++)
                { var a = new Vector2Int(x, y); if (s.Blocks.ValidatePlacement(a) == null && s.Blocks.CellsAt(a).Exists(OnRoute)) { int r0 = RouteLen(); bool ok = s.Blocks.CommitPlacement(a); sb.Append("  wall " + s.Blocks.CurrentShape + " fallback at " + a + " route " + r0 + " -> " + RouteLen() + "\n"); return ok; } }
                return false;
            }
            int before = RouteLen(); bool done = s.Blocks.CommitPlacement(ba);
            sb.Append("  wall card " + bc + " rot " + br + " at " + ba + " route " + before + " -> " + RouteLen() + " ok=" + done + "\n"); return done;
        }
        readonly List<Vector3> bsTowers = new List<Vector3>();
        bool BsTower(int idx, StringBuilder sb)
        {
            var grid = s.grid; Vector3 core = grid.CoreCenter;
            var size = TowerManager.SizeOf(s.config.towers[idx], 0); float best = float.MaxValue; Vector2Int bo = new Vector2Int(-1, -1);
            for (int x = 0; x < grid.width; x++) for (int y = 0; y < grid.height; y++)
            {
                var o = new Vector2Int(x, y); var cells = grid.Footprint(o, size);
                if (!cells.TrueForAll(c => grid.InBounds(c) && grid.CanPlace(c)) || cells.Exists(OnRoute)) continue;
                int adj = 0; foreach (var c in cells) foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }) if (grid.InBounds(c + d) && OnRoute(c + d)) adj++;
                if (adj == 0) continue; var ctr = grid.FootprintCenter(o, size); float dc = (ctr - core).magnitude; if (dc < 2.2f) continue;
                float near = 99f; foreach (var t in bsTowers) near = Mathf.Min(near, (t - ctr).magnitude); if (near < 2.6f) continue;
                float sc = Mathf.Abs(dc - 3.6f) - adj * .25f + (near < 99f ? Mathf.Max(0f, 4f - near) * .2f : 0f);
                if (sc < best && s.Validator.ValidatePlacement(cells) == null) { best = sc; bo = o; }
            }
            if (bo.x < 0) { sb.Append("  tower " + s.config.towers[idx].displayName + ": no spot\n"); return false; }
            var pad = grid.Footprint(bo, size); grid.Commit(pad, CellState.Blocked); foreach (var c in pad) s.Blocks.SpawnWall(c); s.Blocks.MergeWalls();
            bool ok = s.Towers.TryBuild(bo, idx, 0); if (ok) bsTowers.Add(grid.FootprintCenter(bo, size));
            sb.Append("  tower " + s.config.towers[idx].displayName + " at " + bo + " size " + size + " ok=" + ok + "\n"); return ok;
        }
        /// Seeded board: 3 + 1 walls on the route, 4 towers (Seismic 2x2, Needle, Pulse, Chill) on wall pads, Build state.
        IEnumerator BattleSetup(StringBuilder sb, int seed)
        {
            UnityEngine.Random.InitState(seed); GameRng.SetSeed(seed);
            sb.Append("battle board " + Res + " seed " + seed + " touch=" + PointerInput.TouchMode + " camera " + CameraFit.Mode + "\n");
            s.Economy.AddGold(400); yield return WaitRt(.5f);
            sb.Append("build 1: hand " + s.Blocks.Remaining + ", route " + RouteLen() + "\n");
            for (int i = 0; i < 4 && s.Blocks.Remaining > 1; i++) { BsPlaceWall(sb); yield return WaitRt(.15f); }
            s.Draw(); yield return null;
            for (int i = 0; i < 2 && s.Blocks.Remaining > 3; i++) { BsPlaceWall(sb); yield return WaitRt(.15f); }
            foreach (int idx in new[] { 2, 0, 1, 3 }) { BsTower(idx, sb); yield return null; }
            s.Towers.Select(-1); s.Blocks.SelectCard(0);
            sb.Append("towers " + s.Towers.Towers.Count + ", hand " + s.Blocks.Remaining + ", gold " + s.Economy.Gold + ", route " + RouteLen() + "\n");
            yield return WaitRt(1f);
        }
        /// Wave 1 fast-forwarded (x4), reward 0, free draw + 1 more wall; ends in Build before wave 2.
        IEnumerator BattleWave1(StringBuilder sb)
        {
            s.Waves.StartWave(); TimeController.SetSpeed(4); float dl = Time.realtimeSinceStartup + 90f;
            while (s.Game.State == GameState.Combat && Time.realtimeSinceStartup < dl) yield return null;
            TimeController.SetSpeed(1); yield return WaitRt(.5f);
            sb.Append("wave 1 done: state " + s.Game.State + " HP " + s.Economy.HP + " gold " + s.Economy.Gold + "\n");
            if (s.Game.State == GameState.Reward) { s.Rewards.Choose(0); yield return WaitRt(1.2f); }
            if (s.Game.State == GameState.Build) { s.Draw(); yield return null; BsPlaceWall(sb); s.Blocks.SelectCard(0); yield return WaitRt(1.2f); }
            sb.Append("build 2: hand " + s.Blocks.Remaining + ", route " + RouteLen() + ", towers " + s.Towers.Towers.Count + "\n");
        }
        IEnumerator BattleShot()
        {
            var sb = new StringBuilder(); int seed = ArgInt("-seed", 37);
            yield return BattleSetup(sb, seed); Capture(Path.Combine(Dir, "battle_" + Res + "_build.png"));
            yield return BattleWave1(sb);
            TimeController.ResetAll(); TimeController.SetSpeed(1); bool started = s.Waves.StartWave(); float t0 = Time.realtimeSinceStartup;
            sb.Append("wave 2 started=" + started + " wave index " + s.Waves.WaveIndex + " timeScale " + Time.timeScale + "\n");
            int shot = 0; float next = 3.2f;
            while (shot < 6 && s.Game.State == GameState.Combat && Time.realtimeSinceStartup - t0 < 40f)
            {
                yield return null;
                if (Time.realtimeSinceStartup - t0 < next || s.Enemies.Active.Count < 2) continue;
                yield return new WaitForEndOfFrame(); shot++; next = Time.realtimeSinceStartup - t0 + 1.6f;
                Capture(Path.Combine(Dir, "battle_" + Res + "_c" + shot + ".png"));
                sb.Append("frame c" + shot + " t+" + (Time.realtimeSinceStartup - t0).ToString("F1") + " s: enemies " + s.Enemies.Active.Count + ", remaining " + s.Waves.Remaining + ", HP " + s.Economy.HP + ", gold " + s.Economy.Gold + ", timeScale " + Time.timeScale + "\n");
            }
            File.WriteAllText(Path.Combine(Dir, "battle_" + Res + ".txt"), sb.ToString()); Debug.Log(sb.ToString());
        }

        // ---- Seismic probe: does it attack (range / targeting / cooldown) and what VFX does a shot play ----------------------------------
        static string Mm(ParticleSystem.MinMaxCurve c) => c.mode == ParticleSystemCurveMode.Constant ? c.constant.ToString("0.###") : c.mode == ParticleSystemCurveMode.TwoConstants ? c.constantMin.ToString("0.###") + ".." + c.constantMax.ToString("0.###") : c.mode + "(x" + c.curveMultiplier.ToString("0.###") + ")";
        static string VfxInfo(string role, GameObject prefab)
        {
            if (prefab == null) return role + ": none\n";
            var sb = new StringBuilder(role + ": " + prefab.name + " scale " + prefab.transform.localScale + "\n");
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main; var em = ps.emission; var r = ps.GetComponent<ParticleSystemRenderer>(); var sh = ps.shape; var sz = ps.sizeOverLifetime; var col = ps.colorOverLifetime;
                int bursts = 0; float burstN = 0; for (int i = 0; i < em.burstCount; i++) { bursts++; burstN += em.GetBurst(i).count.constantMax; }
                sb.Append("   ps '" + ps.name + "' local " + ps.transform.localPosition.ToString("F2") + " scale " + ps.transform.lossyScale.x.ToString("0.##") + ": duration " + m.duration.ToString("0.###") + " loop " + m.loop + " delay " + Mm(m.startDelay) +
                          " lifetime " + Mm(m.startLifetime) + " size " + Mm(m.startSize) + (m.startSize3D ? " (3D)" : "") + " speed " + Mm(m.startSpeed) + " simSpace " + m.simulationSpace + " simSpeed " + m.simulationSpeed +
                          " | emission rate " + Mm(em.rateOverTime) + " bursts " + bursts + " (" + burstN + " particles) max " + m.maxParticles +
                          " | shape " + (sh.enabled ? sh.shapeType + " r" + sh.radius.ToString("0.##") : "off") + " | sizeOverLife " + (sz.enabled ? "on" : "off") + " colorOverLife " + (col.enabled ? "on" : "off") +
                          " | renderer " + (r != null ? r.renderMode + " mat " + (r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "-") + " sortFudge " + r.sortingFudge + " order " + r.sortingOrder : "-") + "\n");
            }
            foreach (var l in prefab.GetComponentsInChildren<Light>(true)) sb.Append("   light '" + l.name + "' " + l.type + " range " + l.range + " intensity " + l.intensity + "\n");
            foreach (var c in prefab.GetComponentsInChildren<MonoBehaviour>(true)) if (c != null) sb.Append("   script " + c.GetType().Name + " on '" + c.name + "'\n");
            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true)) sb.Append("   mesh '" + mr.name + "' mat " + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "-") + "\n");
            return sb.ToString();
        }
        IEnumerator SeismicProbe()
        {
            var sb = new StringBuilder(); int seed = ArgInt("-seed", 37);
            yield return BattleSetup(sb, seed);
            Tower seis = null; foreach (var t in s.Towers.Towers) if (t != null && t.Data != null && t.Data.displayName.StartsWith("Seismic")) seis = t;
            if (seis == null) { sb.Append("NO SEISMIC TOWER\n"); File.WriteAllText(Path.Combine(Dir, "seismic_" + Res + ".txt"), sb.ToString()); yield break; }
            var d = seis.Data; Vector3 c = seis.transform.position + Vector3.up * .45f;
            sb.Append("SEISMIC: origin " + seis.Origin + " size " + seis.Size + " transform " + seis.transform.position.ToString("F2") + " (footprint centre " + s.grid.FootprintCenter(seis.Origin, seis.Size).ToString("F2") + ")" +
                      " range " + seis.Range.ToString("0.##") + " attackRate " + seis.AttackRate.ToString("0.###") + "/s (cooldown " + (1f / seis.AttackRate).ToString("0.##") + " s) damage " + seis.Damage + " splash " + seis.SplashRadius +
                      " projectileSpeed " + d.projectileSpeed + " lobbed " + d.lobbedShot + " lobHeight " + d.lobHeight + " damageKind " + d.damageKind + " explosionOnEveryHit " + d.explosionOnEveryHit + " impactShake " + d.impactShake + " hitStop " + d.impactHitStop +
                      " head '" + (seis.Head != null ? seis.Head.name : "-") + "'\n");
            sb.Append("targeting: EnemyManager.ClosestToGoal(tower position + 0.45 up, Range) = 3D distance from the footprint centre, First (least remaining path); no ground-only / line-of-sight filter; Tower.Update fires when cooldown <= 0 and a target is in range.\n");
            sb.Append("recoil: none in code (Tower.Fire / Aim only yaw the head; lobbed barrels keep their authored pitch; no kick / scale animation).\n");
            sb.Append(VfxInfo("muzzleVfx", d.muzzleVfx)); sb.Append(VfxInfo("projectileVfx", d.projectileVfx)); sb.Append(VfxInfo("hitVfx", d.hitVfx)); sb.Append(VfxInfo("explosionVfx (every hit)", d.explosionVfx));
            // live: wave 1 at x1, log every Seismic shot (projectile with Owner == seis) and impact; burst frames around the 2nd impact
            var seen = new HashSet<Projectile>(); int fires = 0, impacts = 0, hitsTotal = 0; float inRangeTime = 0, combatTime = 0, firstFire = -1; float lastImpactT = -1f; Vector3 lastImpactP = default; bool forcedDone = false;
            { var tsp = s.viewCamera.WorldToScreenPoint(seis.transform.position); sb.Append("seismic tower screen " + ((Vector2)tsp).ToString("F0") + " (" + Res + ")\n"); }
            var fireTimes = new List<float>(); var impactLog = new StringBuilder();
            System.Action<Vector3, float> onImpact = (p, r) =>
            {
                if (Mathf.Abs(r - seis.SplashRadius) > .01f) return; impacts++; int hit = 0;
                foreach (var e in s.Enemies.Active) if (e != null && e.Alive && (e.transform.position - p).sqrMagnitude <= r * r) hit++;
                hitsTotal += hit; lastImpactT = Time.time; lastImpactP = p; var sp = s.viewCamera.WorldToScreenPoint(p);
                impactLog.Append("  impact #" + impacts + " t=" + Time.time.ToString("F2") + " at " + p.ToString("F1") + " screen " + ((Vector2)sp).ToString("F0") + " enemies within splash (after damage, alive) " + hit + "\n");
            };
            Projectile.Impact += onImpact;
            TimeController.ResetAll(); TimeController.SetSpeed(1); s.Waves.StartWave(); float t0 = Time.time; bool burstDone = false;
            var grabs = new List<(Texture2D tex, string name)>();
            while (s.Game.State == GameState.Combat && Time.time - t0 < 80f)
            {
                yield return null;
                combatTime += Time.deltaTime; bool any = false;
                foreach (var e in s.Enemies.Active) if (e != null && e.Alive && (e.transform.position - c).sqrMagnitude <= seis.Range * seis.Range) { any = true; break; }
                if (any) inRangeTime += Time.deltaTime;
                foreach (var p in FindObjectsOfType<Projectile>()) if (p.Owner == seis && !seen.Contains(p)) { seen.Add(p); fires++; fireTimes.Add(Time.time - t0); if (firstFire < 0) firstFire = Time.time - t0; }
                seen.RemoveWhere(p => p == null || !p.isActiveAndEnabled);
                if (!burstDone && fires >= 2)
                {   // burst at 1/4 speed right after the 2nd shot leaves the muzzle: shell flight, impact, explosion fade
                    burstDone = true; TimeController.SetSpeed(.25f); float b0 = Time.time;
                    foreach (var at in new[] { 0f, .06f, .12f, .2f, .3f, .4f, .5f, .65f, .8f, 1.0f, 1.3f })
                    {
                        while (Time.time - b0 < at) yield return null;
                        yield return new WaitForEndOfFrame(); grabs.Add((Grab(), "seismic_" + Res + "_t" + Mathf.RoundToInt(at * 1000).ToString("0000") + "ms.png"));
                    }
                    TimeController.ResetAll(); TimeController.SetSpeed(1);
                }
                if (burstDone && !forcedDone && fires >= 2)
                {   // v18.6b: forced shots (Tower.Fire is public) - DC / tris around one impact at x1, then fire + impact frames at 1/4 speed
                    Enemy tgt = null; float best = -1f; foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn && e.HP > best) { best = e.HP; tgt = e; }
                    if (tgt == null) continue;
                    forcedDone = true; yield return Clean();
                    var dl = new List<long>(); var tl2 = new List<long>(); for (int f = 0; f < 12; f++) { yield return null; dl.Add(draws.LastValue); tl2.Add(tris.LastValue); }
                    dl.Sort(); tl2.Sort(); long dBase = dl[6], tBase = tl2[6];
                    Transform barrel = null; if (seis.Head != null) foreach (var t in seis.Head.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("_Barrel")) barrel = t;
                    Transform part = barrel != null ? barrel : seis.Head; Vector3 rest = part != null ? part.position : Vector3.zero; float maxKick = 0f, kickAt = 0f;
                    float imp0 = lastImpactT; seis.Fire(tgt); float f0 = Time.time; long dMax = 0, tMax = 0; var dSeq = new StringBuilder(); int nf = 0; float impSeen = -1f; long dImpMax = 0;
                    while (Time.time - f0 < 2.2f && (impSeen < 0f || Time.time - impSeen < 1.2f))
                    {
                        yield return null; nf++; long dv = draws.LastValue, tr = tris.LastValue; dMax = System.Math.Max(dMax, dv); tMax = System.Math.Max(tMax, tr);
                        if (part != null && Time.time - f0 < .15f) { float k = (part.position - rest).magnitude; if (k > maxKick) { maxKick = k; kickAt = Time.time - f0; } }
                        if (impSeen < 0f && lastImpactT > imp0) impSeen = lastImpactT; if (impSeen >= 0f) dImpMax = System.Math.Max(dImpMax, dv);
                        if (nf % 3 == 0) dSeq.Append(dv).Append(' ');
                    }
                    sb.Append("FORCED SHOT DC (x1, ProfilerRecorder, 1 frame lag): before fire median DC " + dBase + " tris " + tBase + " | max during fire + flight + impact + 1.2 s DC " + dMax + " (+" + (dMax - dBase) + ") tris " + tMax + " (+" + (tMax - tBase) + ") | max after impact DC " + dImpMax + " | every 3rd frame: " + dSeq + "\n");
                    sb.Append("RECOIL: part '" + (part != null ? part.name : "-") + "' max displacement " + maxKick.ToString("F3") + " m at t+" + kickAt.ToString("F3") + " s (spec 0.06 m, spring back over 0.1 s)" + (impSeen < 0f ? " | NO IMPACT (target died?)" : "") + "\n");
                    // fire + impact frames at 1/4 speed (game-time stamps)
                    tgt = null; best = -1f; foreach (var e in s.Enemies.Active) if (e != null && e.Alive && !e.HeldBySpawn && e.HP > best) { best = e.HP; tgt = e; }
                    if (tgt != null)
                    {
                        TimeController.SetSpeed(.25f); yield return null; float i1 = lastImpactT; seis.Fire(tgt); float g0 = Time.time;
                        foreach (var at in new[] { 0f, .02f, .04f, .07f, .1f, .14f, .2f })
                        { while (Time.time - g0 < at) yield return null; yield return new WaitForEndOfFrame(); grabs.Add((Grab(), "seismic_fire_" + Res + "_t" + Mathf.RoundToInt(at * 1000).ToString("0000") + "ms.png")); }
                        float wt2 = Time.realtimeSinceStartup; while (lastImpactT <= i1 && Time.realtimeSinceStartup - wt2 < 12f) yield return null;
                        if (lastImpactT > i1)
                        {
                            float h0 = lastImpactT; var isp = s.viewCamera.WorldToScreenPoint(lastImpactP); sb.Append("forced impact screen " + ((Vector2)isp).ToString("F0") + " world " + lastImpactP.ToString("F2") + "\n");
                            foreach (var at in new[] { 0f, .03f, .06f, .1f, .14f, .18f, .23f, .28f, .35f, .42f, .5f, .6f, .75f })
                            {
                                while (Time.time - h0 < at) yield return null; yield return new WaitForEndOfFrame(); grabs.Add((Grab(), "seismic_impact_" + Res + "_t" + Mathf.RoundToInt(at * 1000).ToString("0000") + "ms.png"));
                                sb.Append("  impact frame t+" + at.ToString("F2") + ": " + ImpactFxState() + "\n");
                            }
                        }
                        else sb.Append("forced impact: none within 12 s\n");
                        TimeController.ResetAll(); TimeController.SetSpeed(1);
                    }
                }
            }
            Projectile.Impact -= onImpact;
            foreach (var g in grabs) { File.WriteAllBytes(Path.Combine(Dir, g.name), g.tex.EncodeToPNG()); Destroy(g.tex); }
            sb.Append("wave 1 at x1: state " + s.Game.State + ", combat " + combatTime.ToString("F1") + " s, an enemy inside Range for " + inRangeTime.ToString("F1") + " s (" + (combatTime > 0 ? inRangeTime / combatTime * 100f : 0f).ToString("F0") + " %)\n");
            sb.Append("SEISMIC SHOTS " + fires + " (first at t+" + firstFire.ToString("F2") + " s), impacts " + impacts + ", enemies inside splash at impact " + hitsTotal + "; expected shots ~ inRange x rate = " + (inRangeTime * seis.AttackRate).ToString("F1") + "\n");
            sb.Append("shot times: "); foreach (var f in fireTimes) sb.Append(f.ToString("F2") + " "); sb.Append("\n"); sb.Append(impactLog);
            File.WriteAllText(Path.Combine(Dir, "seismic_" + Res + ".txt"), sb.ToString()); Debug.Log(sb.ToString());
        }

        /// Seismic impact ring state if the v18.6b driver exists (looked up by type name so this file also compiles on v18.6).
        static string ImpactFxState()
        {
            var t = System.Type.GetType("StoneSignal.VFX.SeismicImpactFx"); if (t == null) return "(no v18.6b ring driver)";
            var last = t.GetProperty("Last")?.GetValue(null) as Component; if (last == null) return "(no ring yet)";
            string P(string n) { var v = t.GetProperty(n)?.GetValue(last); return v is float f ? f.ToString("F3") : "-"; }
            return "ring r " + P("RingRadius") + " m width " + P("RingWidth") + " m alpha " + P("RingA") + " | wave r " + P("WaveR") + " alpha " + P("WaveA") + " | splash " + P("Splash");
        }
        // ---- draw calls: Build and Combat (wave 2), full frame vs every canvas off (= HUD DC incl. screen-space HP bars / numbers) ---------
        IEnumerator DcMeasure(StringBuilder sb, string label)
        {
            yield return Clean();
            var ld = new List<long>(); var lb = new List<long>(); var ls = new List<long>(); var lt = new List<long>();
            IEnumerator Sample() { ld.Clear(); lb.Clear(); ls.Clear(); lt.Clear(); for (int f = 0; f < 4; f++) yield return null; for (int f = 0; f < 21; f++) { yield return null; ld.Add(draws.LastValue); lb.Add(batches.LastValue); ls.Add(setPass.LastValue); lt.Add(tris.LastValue); } ld.Sort(); lb.Sort(); ls.Sort(); lt.Sort(); }
            yield return Sample(); long dAll = ld[10], bAll = lb[10], sAll = ls[10], tAll = lt[10]; string rAll = ld[0] + ".." + ld[20], rtAll = lt[0] + ".." + lt[20];
            var off = new List<Canvas>(); foreach (var cv in FindObjectsOfType<Canvas>()) if (cv.enabled && cv.isRootCanvas) { cv.enabled = false; off.Add(cv); }
            yield return Sample(); long dScene = ld[10], bScene = lb[10], tScene = lt[10]; string rScene = ld[0] + ".." + ld[20];
            foreach (var cv in off) if (cv != null) cv.enabled = true;
            yield return Sample(); long dAll2 = ld[10];
            sb.Append(label + ": full frame DC " + dAll + " (range " + rAll + ", re-measured " + dAll2 + "), batches " + bAll + ", setpass " + sAll + ", tris " + tAll + " (range " + rtAll + ") | canvases off DC " + dScene + " (range " + rScene + "), batches " + bScene + ", tris " + tScene +
                      " | HUD DC (full - canvases off) " + (dAll - dScene) + " | root canvases " + off.Count + ", live enemies " + s.Enemies.Active.Count + ", towers " + s.Towers.Towers.Count + "\n");
        }
        IEnumerator Dc186()
        {
            var sb = new StringBuilder("draw calls " + Res + " - ProfilerRecorder Draw Calls Count, median of 21 frames, development player, seed 37 battle board\n");
            yield return BattleSetup(sb, ArgInt("-seed", 37));
            yield return DcMeasure(sb, "BUILD (before wave 1)");
            yield return BattleWave1(sb);
            yield return DcMeasure(sb, "BUILD (before wave 2)");
            TimeController.ResetAll(); TimeController.SetSpeed(1); s.Waves.StartWave(); float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f) yield return null;
            yield return DcMeasure(sb, "COMBAT (wave 2, t+4 s)");
            File.WriteAllText(Path.Combine(Dir, "dc186_" + Res + ".txt"), sb.ToString()); Debug.Log(sb.ToString());
        }
    }
}
#endif
