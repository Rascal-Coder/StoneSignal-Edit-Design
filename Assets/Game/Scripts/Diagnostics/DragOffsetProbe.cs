#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StoneSignal
{
    /// -stonesignal-drag <dir>: touch drag-offset report for the current screen resolution (one player run per resolution).
    /// Configs, scene reloaded per config: notch off/on (simulated safe area L/R 132 px, bottom 63 px) x
    /// {Build x1, Build x2, Build x3, Combat x2 (walls only), Combat x3 (walls only)}.
    /// Targets: 4 board corners, centre, 4 edge midpoints (nearest free cell). Pieces per target: L wall (rotated, drag-hold-swipe
    /// or drag-release + tap arrow), 1x1 tower, 2x2 tower, synthetic rotating 1x2 tower (Needle clone, test only).
    /// Finger: searched below the visible top-face centre of the target cell (columns x = aim, +-.2 / +-.35 cell) for the
    /// longest run of finger positions whose controller aim (PlacementInputController.AimFor: adaptive offset, lifted over
    /// the hand cards) raycasts onto the target and is not in the cancel zone; the run middle is used ("NA-unreachable" if none).
    /// Pass: snapped cell == visible cell under the aim point (and covered by the ghost), and highlighted cells == placed cells
    /// (or nothing placed when the highlight is invalid). Rows: drag_<W>x<H>.tsv, layout: drag_<W>x<H>_layout.txt.
    public sealed class DragOffsetProbe : MonoBehaviour
    {
        struct Cfg { public bool notch, combat; public float ts; public string Name => (notch ? "notch" : "nonotch") + "_" + (combat ? "combat" : "build") + "_x" + ts; }
        static readonly Cfg[] Cfgs =
        {
            new Cfg { ts = 1 }, new Cfg { ts = 2 }, new Cfg { ts = 3 }, new Cfg { ts = 2, combat = true }, new Cfg { ts = 3, combat = true },
            new Cfg { notch = true, ts = 1 }, new Cfg { notch = true, ts = 2 }, new Cfg { notch = true, ts = 3 }, new Cfg { notch = true, ts = 2, combat = true }, new Cfg { notch = true, ts = 3, combat = true },
        };
        static int cfgIndex;
        enum Piece { Wall, T1x1, T2x2, T1x2 }
        GameBootstrap s; string dir; PlacementInputController pic; Camera cam; ArtCatalog art; GridManager g; Cfg cfg;
        string Res => Screen.width + "x" + Screen.height;
        string Tsv => Path.Combine(dir, "drag_" + Res + ".tsv");
        string LayoutFile => Path.Combine(dir, "drag_" + Res + "_layout.txt");

        public void Initialize(GameBootstrap game, string output) { s = game; dir = output; Application.runInBackground = true; StartCoroutine(Run()); }
        static Rect Notch(int w, int h) => new Rect(132, 63, w - 264, h - 63);

        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(.8f);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 240;
            Directory.CreateDirectory(dir);
            cfg = Cfgs[cfgIndex];
            HudScaler.SimulatedSafeArea = cfg.notch ? Notch(Screen.width, Screen.height) : (Rect?)null;
            PointerInput.ForceTouch = true;
            pic = PlacementInputController.Instance; cam = Camera.main; art = s.config.palette != null ? s.config.palette.art : null; g = s.grid;
            if (cfgIndex == 0)
            {
                File.WriteAllText(Tsv, "res\tconfig\tstate\ttarget\tcell\tpiece\tflow\tdir\taim\tfinger\tfingerOnScreen\tfingerInSafe\tfingerOverHand\tsnapAnchor\texpectAnchor\tsnapOk\tshown\tvalid\tshownCells\tplacedCells\texpectCells\tghostFingerDy\toffsetPx\twindowPx\tresult\tnote\n");
                File.WriteAllText(LayoutFile, "");
            }
            s.Economy.AddGold(50000);
            Ensure1x2();
            s.Blocks.NotifyChanged(); // hand rebuilt in touch mode (no 1-4 badges), with the simulated safe area and the 1x2 test card
            for (int f = 0; f < 6; f++) yield return null;
            s.Blocks.NotifyChanged(); yield return null; yield return null;
            if (!cfg.combat && cfg.ts == 1) { LogLayout(); Capture(Path.Combine(dir, "drag_bg_" + Res + (cfg.notch ? "_notch" : "") + ".png")); yield return CornerShot(); }
            if (!cfg.combat && cfg.ts == 1 && cfg.notch && Screen.width == 2400) yield return MobileShots();
            TimeController.SetSpeed(cfg.ts);
            if (cfg.combat) { s.Waves.StartWave(); float w0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - w0 < 1.5f) yield return null; }
            var targets = Targets();
            for (int i = 0; i < targets.Count; i++)
            {
                int d = i % 4; bool tap = i % 2 == 1;
                yield return Case(targets[i].Item1, targets[i].Item2, Piece.Wall, d, tap);
                if (cfg.combat) continue;
                yield return Case(targets[i].Item1, targets[i].Item2, Piece.T1x1, 0, false);
                yield return Case(targets[i].Item1, targets[i].Item2, Piece.T2x2, 0, false);
                yield return Case(targets[i].Item1, targets[i].Item2, Piece.T1x2, (d + 1) % 4, !tap);
            }
            TimeController.ResetAll(); PointerInput.ClearInjection();
            cfgIndex++;
            if (cfgIndex < Cfgs.Length) { SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); yield break; }
            HudScaler.SimulatedSafeArea = null;
            Debug.Log("DRAG PROBE DONE " + Res + " -> " + Tsv);
            Application.Quit(0);
        }

        // ---------- setup ----------
        void Ensure1x2()
        {
            var t = s.config.towers; if (t.Length < 2 || t[1].name.StartsWith("Needle1x2")) return;
            var d = Instantiate(t[0]); d.name = "Needle1x2 (test)"; d.displayName = "Needle 1x2 test"; d.footprint = new Vector2Int(1, 2); d.footprintRotates = true; t[1] = d;
        }
        int Index2x2() { for (int i = 0; i < s.config.towers.Length; i++) { var f = s.config.towers[i].footprint; if (f.x == 2 && f.y == 2) return i; } return -1; }
        BlockShapeData Shape(string n) { foreach (var b in s.config.blocks) if (b != null && (b.displayName == n || b.name == n)) return b; return null; }
        List<(string, Vector2Int)> Targets()
        {
            int w = g.width, h = g.height; var list = new List<(string, Vector2Int)>();
            Vector2Int Free(Vector2Int c, Vector2Int step) { for (int k = 0; k < 6; k++) { var q = c + step * k; if (g.InBounds(q) && g.Get(q) == CellState.Empty) return q; } return c; }
            Vector2Int Centre() { var m = new Vector2Int(w / 2, h / 2); for (int r = 0; r < 6; r++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) { var q = m + new Vector2Int(dx, dy); if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r && g.InBounds(q) && g.Get(q) == CellState.Empty) return q; } return m; }
            list.Add(("corner BL", Free(new Vector2Int(0, 0), Vector2Int.right))); list.Add(("corner BR", Free(new Vector2Int(w - 1, 0), Vector2Int.left)));
            list.Add(("corner TL", Free(new Vector2Int(0, h - 1), Vector2Int.right))); list.Add(("corner TR", Free(new Vector2Int(w - 1, h - 1), Vector2Int.left)));
            list.Add(("centre", Centre()));
            list.Add(("edge B", Free(new Vector2Int(w / 2, 0), Vector2Int.right))); list.Add(("edge T", Free(new Vector2Int(w / 2, h - 1), Vector2Int.right)));
            list.Add(("edge L", Free(new Vector2Int(0, h / 2), Vector2Int.up))); list.Add(("edge R", Free(new Vector2Int(w - 1, h / 2), Vector2Int.up)));
            return list;
        }
        bool Raised(Vector2Int c) { var st = g.Get(c); return g.InBounds(c) && (st == CellState.Blocked || st == CellState.TowerSlot); }
        float TopOf(Vector2Int c) => art == null ? 0 : Raised(c) ? art.blockTop : art.tileTop;
        Vector3 VisualTop(Vector2Int c) => g.ToWorld(c) + Vector3.up * TopOf(c);
        bool PrepWall(Vector2Int c)
        {
            if (!g.InBounds(c)) return false; if (g.Get(c) == CellState.Blocked) return true;
            var one = new List<Vector2Int> { c };
            if (!g.CanPlace(c) || s.Validator.ValidatePlacement(one) != null) return false;
            g.Commit(one, CellState.Blocked); s.Blocks.SpawnWall(c); return true;
        }
        bool CanPrep(Vector2Int c) => g.InBounds(c) && (g.Get(c) == CellState.Blocked || (g.CanPlace(c) && s.Validator.ValidatePlacement(new List<Vector2Int> { c }) == null));
        static Vector2Int TowerOrigin(Vector2Int anchor, Vector2Int size, int d) => d == 2 ? anchor - new Vector2Int(0, size.y - 1) : d == 3 ? anchor - new Vector2Int(size.x - 1, 0) : anchor;
        static Vector2 DirV(int d) => d == 0 ? Vector2.up : d == 1 ? Vector2.right : d == 2 ? Vector2.down : Vector2.left;
        Vector2 CardCentre(string prefix, Vector2 fallback)
        {
            foreach (var rt in FindObjectsOfType<RectTransform>())
                if (rt.name.StartsWith(prefix) && rt.gameObject.activeInHierarchy) { var k = new Vector3[4]; rt.GetWorldCorners(k); return (k[0] + k[2]) * .5f; }
            return fallback;
        }

        // ---------- finger search (adaptive offset) ----------
        struct Reach { public bool ok; public Vector2 finger, aim; public float window; }
        float CellPx(Vector2Int c) => Mathf.Abs(cam.WorldToScreenPoint(g.ToWorld(c) + Vector3.right * g.cellSize).x - cam.WorldToScreenPoint(g.ToWorld(c)).x);
        System.Func<Vector2, bool> HitsCell(Vector2Int target) => a => g.RaycastCell(cam.ScreenPointToRay(a), out var c, out _) && c == target;
        Reach FindFinger(Vector2 aim0, float cellPx, System.Func<Vector2, bool> hits, float step = 1f)
        {
            float k1 = pic.CanvasScale, span = (pic.DragOffset + 420f) * k1; float[] fx = { 0f, .2f, -.2f, .35f, -.35f };
            Reach best = default, centre = default;
            foreach (var f in fx)
            {
                float x = aim0.x + f * cellPx, runStart = -1, runLen = 0, bestStart = -1, bestLen = 0;
                for (float y = Mathf.Min(aim0.y, Screen.height - 1); y >= aim0.y - span && y >= 0; y -= step)
                {
                    var fp = new Vector2(x, y);
                    bool ok = x >= 0 && x < Screen.width && !pic.InCancelZone(fp) && hits(pic.AimFor(fp));
                    if (ok) { if (runStart < 0) { runStart = y; runLen = 0; } runLen += step; if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; } } else runStart = -1;
                }
                if (bestLen <= 0) continue;
                var fp2 = new Vector2(x, bestStart - (bestLen - step) * .5f); var r = new Reach { ok = true, finger = fp2, aim = pic.AimFor(fp2), window = bestLen };
                if (f == 0f) centre = r; if (bestLen > best.window) best = r;
            }
            return centre.ok && centre.window >= Mathf.Min(6f * k1, best.window * .5f) ? centre : best; // prefer the finger straight below the cell
        }

        // ---------- snapshot of what the player sees ----------
        struct Snap { public bool shown, valid; public List<Vector2Int> cells; public Vector2Int anchor; public Vector2 ghost; public int rot; }
        Snap Take(bool tower, int ti)
        {
            var sn = new Snap { cells = new List<Vector2Int>() };
            if (tower)
            {
                sn.shown = s.Towers.PreviewShown; sn.anchor = s.Towers.PreviewCell; sn.rot = s.Towers.Rotation; sn.valid = s.Towers.Status == "Build";
                var size = TowerManager.SizeOf(s.config.towers[ti], sn.rot); sn.cells = g.Footprint(sn.anchor, size);
                sn.ghost = cam.WorldToScreenPoint(g.FootprintCenter(sn.anchor, size) + Vector3.up * (art ? art.blockTop : 0));
            }
            else
            {
                sn.shown = s.Blocks.PreviewShown; sn.anchor = s.Blocks.PreviewAnchor; sn.rot = s.Blocks.Rotation; sn.valid = s.Blocks.PreviewValid;
                sn.cells = s.Blocks.CellsAt(sn.anchor); Vector3 sum = Vector3.zero; foreach (var c in sn.cells) sum += g.ToWorld(c);
                sn.ghost = sn.cells.Count > 0 ? (Vector2)cam.WorldToScreenPoint(sum / sn.cells.Count + Vector3.up * (art ? art.tileTop : 0)) : Vector2.zero;
            }
            return sn;
        }
        CellState[,] Grab() { var a = new CellState[g.width, g.height]; for (int x = 0; x < g.width; x++) for (int y = 0; y < g.height; y++) a[x, y] = g.Get(new Vector2Int(x, y)); return a; }
        List<Vector2Int> Diff(CellState[,] before)
        {
            var l = new List<Vector2Int>();
            for (int x = 0; x < g.width; x++) for (int y = 0; y < g.height; y++) { var st = g.Get(new Vector2Int(x, y)); if (st != before[x, y] && (st == CellState.Blocked || st == CellState.TowerSlot)) l.Add(new Vector2Int(x, y)); }
            return l;
        }
        static bool SetEq(List<Vector2Int> a, List<Vector2Int> b) { if (a.Count != b.Count) return false; foreach (var x in a) if (!b.Contains(x)) return false; return true; }
        static string Cells(List<Vector2Int> l) { if (l == null || l.Count == 0) return "-"; var sb = new StringBuilder(); foreach (var c in l) sb.Append(c.x).Append(',').Append(c.y).Append(' '); return sb.ToString().TrimEnd(); }
        static string V(Vector2 v) => v.x.ToString("0") + "," + v.y.ToString("0");
        static string C(Vector2Int c) => c.x + "," + c.y;
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        // ---------- one case ----------
        IEnumerator Case(string label, Vector2Int target, Piece piece, int prefDir, bool tap)
        {
            string note = ""; bool tower = piece != Piece.Wall; int ti = piece == Piece.T1x1 ? 0 : piece == Piece.T2x2 ? Index2x2() : piece == Piece.T1x2 ? 1 : -1;
            if (tower && ti < 0) yield break;
            if (tower && s.Game.State != GameState.Build) yield break;
            bool directional = piece == Piece.Wall || piece == Piece.T1x2;
            int cardIndex = -1, d = prefDir; BlockShapeData L = null;
            Vector2Int expectAnchor = target; Vector3 aimOffset = Vector3.zero;
            // ---- prepare the piece and the board under it
            if (piece == Piece.Wall)
            {
                L = Shape("L"); if (L == null) { note = "no L shape"; yield break; }
                var hand = s.Blocks.Hand; if (hand.IsFull) { hand.Select(0); hand.Consume(); }
                hand.AddCard(L, RuneRules.NoRune); s.Blocks.NotifyChanged();
                for (int i = hand.Cards.Count - 1; i >= 0; i--) if (hand.Cards[i] == L && hand.Runes[i] == RuneRules.NoRune) { cardIndex = i; break; }
                s.Blocks.SelectCard(cardIndex);
                if (s.Blocks.ValidatePlacement(target, d) != null) { for (int k = 0; k < 4; k++) if (s.Blocks.ValidatePlacement(target, k) == null) { note += "dir " + d + " invalid->" + k + "; "; d = k; break; } }
            }
            else if (piece == Piece.T1x1) { if (!PrepWall(target)) note += "no wall under target; "; cardIndex = ti; }
            else if (piece == Piece.T2x2)
            {
                var size = new Vector2Int(2, 2); aimOffset = new Vector3(.2f, 0, .2f) * g.cellSize; // off the exact cell centre (no .5 rounding tie)
                var p = VisualTop(target) + aimOffset - g.transform.position;
                var o = new Vector2Int(Mathf.RoundToInt(p.x / g.cellSize - 1f), Mathf.RoundToInt(p.z / g.cellSize - 1f));
                o = new Vector2Int(Mathf.Clamp(o.x, 0, g.width - 2), Mathf.Clamp(o.y, 0, g.height - 2)); expectAnchor = o; // centred on the aim point, kept on the board
                foreach (var c in g.Footprint(o, size)) if (!PrepWall(c)) note += "pad " + C(c) + " not wallable; ";
                cardIndex = ti;
            }
            else
            {
                var fp = new Vector2Int(1, 2); int pick = -1;
                for (int k = 0; k < 4 && pick < 0; k++) { int r = (prefDir + k) % 4; var size = GridManager.RotatedSize(fp, r); if (g.Footprint(TowerOrigin(target, size, r), size).TrueForAll(CanPrep)) pick = r; }
                if (pick < 0) { note += "no wallable 1x2 footprint; "; pick = prefDir; } else if (pick != prefDir) note += "dir " + prefDir + "->" + pick + "; ";
                d = pick; var sz = GridManager.RotatedSize(fp, d); foreach (var c in g.Footprint(TowerOrigin(target, sz, d), sz)) PrepWall(c);
                cardIndex = ti;
            }
            s.Blocks.NotifyChanged(); yield return null;
            if (tower) { s.Towers.Select(ti); yield return null; yield return null; } // selected card sits 12 px higher: search with the layout the drag will see
            // ---- aim / finger
            Vector2 aim = cam.WorldToScreenPoint(VisualTop(target) + aimOffset);
            float k1 = pic.CanvasScale;
            var hitTest = HitsCell(target);
            if (piece == Piece.T2x2) { var sz2 = new Vector2Int(2, 2); var ea = expectAnchor; hitTest = a => g.RaycastCell(cam.ScreenPointToRay(a), out var c, out var hp) && c == target && g.ClampOrigin(g.FootprintOrigin(hp, sz2), sz2) == ea; }
            var reach = FindFinger(aim, CellPx(target), hitTest);
            Vector2 finger = reach.ok ? reach.finger : aim - Vector2.up * pic.DragOffset * k1;
            if (reach.ok) aim = reach.aim;
            bool onScreen = finger.x >= 0 && finger.y >= 0 && finger.x < Screen.width && finger.y < Screen.height;
            var safe = HudScaler.SafeArea; bool inSafe = safe.Contains(finger);
            bool overHand = pic.IsOverHand(finger);
            if (!reach.ok)
            {
                pic.CancelPlacement();
                File.AppendAllText(Tsv, Res + "\t" + cfg.Name + "\t" + s.Game.State + "\t" + label + "\t" + C(target) + "\t" + piece + "\t-\t" + (directional ? d.ToString() : "-") + "\t" + V(aim) + "\t" + V(finger) + "\t" + onScreen + "\t" + inSafe + "\t" + overHand +
                    "\t-\t" + C(expectAnchor) + "\tFalse\tFalse\tFalse\t-\t-\t-\tNaN\tNaN\t0\tNA-unreachable\t" + note + "no finger position reaches the cell\n");
                yield break;
            }
            Vector2 start = tower ? CardCentre("Tower card " + s.config.towers[ti].displayName, new Vector2(finger.x, 10)) : CardCentre("Block card " + L.displayName, new Vector2(finger.x, 10));
            var before = Grab(); int towersBefore = s.Towers.Towers.Count;
            // ---- gesture
            PointerInput.Inject(start, true, true, false); pic.CardDown(tower, cardIndex, start); yield return null;
            for (int k = 1; k <= 8; k++) { PointerInput.Inject(Vector2.Lerp(start, finger, k / 8f), false, true, false); yield return null; }
            PointerInput.Inject(finger, false, true, false); yield return null; yield return null;
            var drag = Take(tower, ti); var snap = drag; var expect = new List<Vector2Int>(); bool expectValid = false, noDir = false; string flow;
            if (!directional)
            {
                flow = "drag-release";
                PointerInput.Inject(finger, false, false, true); yield return Frames(3);
            }
            else if (!tap)
            {
                flow = "drag-hold-swipe";
                float h0 = Time.realtimeSinceStartup;
                while (pic.Machine.State != PlacementState.Direction && Time.realtimeSinceStartup - h0 < .6f) { PointerInput.Inject(finger, false, true, false); yield return null; }
                if (pic.Machine.State != PlacementState.Direction)
                {   // no valid direction at this anchor: the hold never opens the diamond; release cancels
                    note += "hold did not open direction (" + pic.Machine.State + "); "; noDir = true;
                    PointerInput.Inject(finger, false, false, true); yield return Frames(3);
                }
                else
                {
                    Vector2 end = finger + DirV(d) * pic.Machine.SwipeThreshold * k1 * 2f;
                    for (int k = 1; k <= 3; k++) { PointerInput.Inject(Vector2.Lerp(finger, end, k / 3f), false, true, false); yield return null; }
                    yield return null; snap = Take(tower, ti);
                    PointerInput.Inject(end, false, false, true); yield return Frames(3);
                }
            }
            else
            {
                flow = "drag-release-tap";
                PointerInput.Inject(finger, false, false, true); yield return Frames(2);
                if (pic.Machine.State != PlacementState.Direction) { note += "release did not open direction (" + pic.Machine.State + "); "; noDir = true; }
                else
                {
                    snap = Take(tower, ti);
                    var anchor = pic.Machine.Cell;
                    if (tower) { var size = TowerManager.SizeOf(s.config.towers[ti], d); expect = g.Footprint(TowerOrigin(anchor, size, d), size); expectValid = s.Towers.ReasonFor(anchor, d) == null; }
                    else { expect = s.Blocks.CellsAt(anchor, d); expectValid = s.Blocks.ValidatePlacement(anchor, d) == null; }
                    Vector2 tapAt = pic.Machine.Centre + DirV(d) * pic.Machine.OuterRadius * .62f * k1;
                    PointerInput.Inject(tapAt, true, true, false); yield return null;
                    PointerInput.Inject(tapAt, false, false, true); yield return Frames(3);
                    if (pic.Machine.State == PlacementState.Direction)
                    {   // why did the arrow tap not commit? top UI raycast hit under the tap + machine direction / validity
                        var es = UnityEngine.EventSystems.EventSystem.current; var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                        if (es != null) es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = tapAt }, hits);
                        note += "tap kept Direction (tapAt=" + V(tapAt) + " centre=" + V(pic.Machine.Centre) + " machineDir=" + pic.Machine.Dir + " uiHit=" + (hits.Count > 0 ? hits[0].gameObject.name + "<" + (hits[0].gameObject.transform.parent ? hits[0].gameObject.transform.parent.name : "") : "none") + "); ";
                    }
                }
            }
            Vector2Int machineCell = pic.Machine.Cell;
            var placed = Diff(before);
            if (s.Towers.Towers.Count > towersBefore) s.Towers.Remove(s.Towers.Towers[s.Towers.Towers.Count - 1]);
            if (pic.Machine.State != PlacementState.Idle) { note += "left in " + pic.Machine.State + " -> cancelled; "; pic.CancelPlacement(); }
            PointerInput.ClearInjection(); yield return null;
            // ---- evaluate
            bool snapOk; Vector2Int snapAnchor;
            if (piece == Piece.T2x2) { snapAnchor = drag.anchor; snapOk = drag.shown && drag.anchor == expectAnchor && drag.cells.Contains(target); }
            else if (piece == Piece.T1x1) { snapAnchor = drag.anchor; snapOk = drag.shown && drag.cells.Count == 1 && drag.cells[0] == target; }
            else
            {   // directional: the drag ghost must sit on the aimed cell, the direction preview / arrow footprint must cover it
                snapAnchor = drag.anchor;
                bool dragOk = drag.shown && (piece == Piece.Wall ? drag.anchor == target : true) && drag.cells.Contains(target);
                snapOk = dragOk && (noDir || (snap.cells.Contains(target) && (!tap || expect.Contains(target)) && (tap || machineCell == target)));
            }
            if (!tap || noDir) { expect = snap.cells; expectValid = !noDir && snap.valid; }
            bool placedAny = placed.Count > 0; string result;
            if (!onScreen) result = "NA-finger-offscreen";
            else if (!drag.shown && !snap.shown) result = "FAIL-no-ghost";
            else if (!snapOk) result = "FAIL-snap";
            else if (expectValid && placedAny && SetEq(placed, expect)) result = "PASS";
            else if (!expectValid && !placedAny) result = "PASS-invalid";
            else result = "FAIL-mismatch";
            float dy = drag.shown ? drag.ghost.y - finger.y : float.NaN;
            string row = Res + "\t" + cfg.Name + "\t" + s.Game.State + "\t" + label + "\t" + C(target) + "\t" + piece + "\t" + flow + "\t" + (directional ? d.ToString() : "-") + "\t" + V(aim) + "\t" + V(finger) + "\t" + onScreen + "\t" + inSafe + "\t" + overHand +
                         "\t" + C(snapAnchor) + "\t" + C(expectAnchor) + "\t" + snapOk + "\t" + (snap.shown || drag.shown) + "\t" + expectValid + "\t" + Cells(snap.cells) + "\t" + Cells(placed) + "\t" + Cells(expect) + "\t" + dy.ToString("0.0") + "\t" + (aim.y - finger.y).ToString("0.0") + "\t" + reach.window.ToString("0") + "\t" + result + "\t" + note + "\n";
            File.AppendAllText(Tsv, row);
        }

        // ---------- layout + mobile proof shots ----------
        void LogLayout()
        {
            var sb = new StringBuilder("# " + Res + " " + (cfg.notch ? "notch" : "nonotch") + " safe=" + HudScaler.SafeArea + " canvasScale=" + pic.CanvasScale + " dragOffsetPx=" + (pic.DragOffset * pic.CanvasScale) + " handTop=" + pic.HandTop + "\n");
            foreach (var rt in FindObjectsOfType<RectTransform>())
            {
                if (!rt.gameObject.activeInHierarchy) continue;
                string n = rt.name; if (!(n.StartsWith("Tower card") || n.StartsWith("Block card") || n == "Hand count" || n == "BATTLE" || n == "DrawPileRoot")) continue;
                var k = new Vector3[4]; rt.GetWorldCorners(k); float x0 = Mathf.Min(k[0].x, k[1].x, k[2].x, k[3].x), x1 = Mathf.Max(k[0].x, k[1].x, k[2].x, k[3].x), y0 = Mathf.Min(k[0].y, k[1].y, k[2].y, k[3].y), y1 = Mathf.Max(k[0].y, k[1].y, k[2].y, k[3].y);
                sb.Append("rect\t" + n + "\t" + x0.ToString("0") + "\t" + y0.ToString("0") + "\t" + x1.ToString("0") + "\t" + y1.ToString("0") + "\n");
            }
            int hot = 0; foreach (var rt in FindObjectsOfType<RectTransform>()) if (rt.name == "Hotkey" && rt.gameObject.activeInHierarchy) hot++;
            sb.Append("hotkeyBadges\t" + hot + "\n");
            { var bl = Targets()[0].Item2; float bx = cam.WorldToScreenPoint(VisualTop(bl)).x; for (float y = 0; y <= Screen.height * .6f; y += 4) { var fp = new Vector2(bx, y); sb.Append("profile\t" + bx.ToString("0") + "\t" + y.ToString("0") + "\t" + pic.OffsetFor(fp).ToString("0.0") + "\t" + (pic.InCancelZone(fp) ? 1 : 0) + "\n"); } }
            sb.Append("liftMargin\t" + pic.LiftMargin + "\tliftFeather\t" + pic.LiftFeather + "\n");
            for (int y = 0; y < g.height; y++) for (int x = 0; x < g.width; x++) { var c = new Vector2Int(x, y); Vector2 sp = cam.WorldToScreenPoint(VisualTop(c)); sb.Append("cell\t" + x + "\t" + y + "\t" + sp.x.ToString("0") + "\t" + sp.y.ToString("0") + "\t" + g.Get(c) + "\n"); }
            // per-cell drag reachability with the adaptive offset (finger search as in Case, 2 px steps)
            for (int y = 0; y < g.height; y++) for (int x = 0; x < g.width; x++)
            {
                var c = new Vector2Int(x, y); Vector2 sp = cam.WorldToScreenPoint(VisualTop(c)); var r = FindFinger(sp, CellPx(c), HitsCell(c), 2f);
                sb.Append("reach\t" + x + "\t" + y + "\t" + (r.ok ? 1 : 0) + "\t" + V(r.finger) + "\t" + V(r.aim) + "\t" + r.window.ToString("0") + "\t" + (r.ok ? (r.aim.y - r.finger.y).ToString("0") : "-") + "\t" + pic.IsOverHand(r.finger) + "\n");
            }
            File.AppendAllText(LayoutFile, sb.ToString());
        }
        IEnumerator MobileShots()
        {
            var info = new StringBuilder("mobile proof shots " + Res + " notch safe=" + HudScaler.SafeArea + " touchMode=" + PointerInput.TouchMode + "\n");
            int hot = 0; foreach (var rt in FindObjectsOfType<RectTransform>()) if (rt.name == "Hotkey" && rt.gameObject.activeInHierarchy) hot++;
            info.Append("1-4 hotkey badges visible: " + hot + "\n");
            float k1 = pic.CanvasScale;
            Vector2Int Near(float fx, float fy) { var m = new Vector2Int(Mathf.RoundToInt(g.width * fx), Mathf.RoundToInt(g.height * fy)); for (int r = 0; r < 6; r++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) { var q = m + new Vector2Int(dx, dy); if (g.InBounds(q) && g.Get(q) == CellState.Empty && CanPrep(q)) return q; } return m; }
            // 1) tower drag: ghost DragOffset px above the finger
            var t1 = Near(.35f, .6f); PrepWall(t1); s.Blocks.NotifyChanged(); s.Towers.Select(0); yield return null; yield return null;
            Vector2 aim = cam.WorldToScreenPoint(VisualTop(t1)), finger = aim - Vector2.up * pic.DragOffset * k1;
            { var r = FindFinger(aim, CellPx(t1), HitsCell(t1)); if (r.ok) { finger = r.finger; aim = r.aim; } }
            Vector2 start = CardCentre("Tower card " + s.config.towers[0].displayName, new Vector2(finger.x, 10));
            PointerInput.Inject(start, true, true, false); pic.CardDown(true, 0, start); yield return null;
            for (int k = 1; k <= 8; k++) { PointerInput.Inject(Vector2.Lerp(start, finger, k / 8f), false, true, false); yield return null; }
            for (int k = 0; k < 20; k++) { PointerInput.Inject(finger, false, true, false); yield return null; }
            var sn = Take(true, 0); yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "mobile_drag_tower.png"));
            info.Append("tower drag: cell " + C(t1) + " finger " + V(finger) + " aim " + V(aim) + " ghost centre " + V(sn.ghost) + " ghost-finger dy " + (sn.ghost.y - finger.y).ToString("0") + " px (offset " + pic.OffsetFor(finger).ToString("0") + " px; base DragOffset " + pic.DragOffset + " x scale " + k1 + ") shown=" + sn.shown + " valid=" + sn.valid + "\n");
            info.Append("FINGER\tmobile_drag_tower\t" + V(finger) + "\t" + V(aim) + "\n");
            pic.CancelPlacement(); PointerInput.ClearInjection(); yield return Frames(3);
            // 2) wall: drag, hold -> direction diamond, swipe right (rotated L previewed)
            var L = Shape("L"); var hand = s.Blocks.Hand; if (hand.IsFull) { hand.Select(0); hand.Consume(); } hand.AddCard(L, RuneRules.NoRune); s.Blocks.NotifyChanged(); yield return null;
            int li = -1; for (int i = hand.Cards.Count - 1; i >= 0; i--) if (hand.Cards[i] == L) { li = i; break; }
            s.Towers.Select(-1); s.Blocks.SelectCard(li); yield return null; yield return null;
            var t2 = Near(.55f, .45f); aim = cam.WorldToScreenPoint(VisualTop(t2)); finger = aim - Vector2.up * pic.DragOffset * k1;
            { var r = FindFinger(aim, CellPx(t2), HitsCell(t2)); if (r.ok) { finger = r.finger; aim = r.aim; } }
            start = CardCentre("Block card " + L.displayName, new Vector2(finger.x, 10));
            yield return DiamondShot(false, li, start, finger, 1, "mobile_wall_diamond", info, t2, aim);
            // 3) synthetic 1x2 tower: walls under it, hold -> diamond, swipe up
            var t3 = Near(.7f, .6f); PrepWall(t3); PrepWall(t3 + Vector2Int.up); s.Blocks.NotifyChanged(); s.Towers.Select(1); yield return null; yield return null;
            aim = cam.WorldToScreenPoint(VisualTop(t3)); finger = aim - Vector2.up * pic.DragOffset * k1;
            { var r = FindFinger(aim, CellPx(t3), HitsCell(t3)); if (r.ok) { finger = r.finger; aim = r.aim; } }
            start = CardCentre("Tower card " + s.config.towers[1].displayName, new Vector2(finger.x, 10));
            yield return DiamondShot(true, 1, start, finger, 0, "mobile_1x2_diamond", info, t3, aim);
            File.WriteAllText(Path.Combine(dir, "mobile_shots.txt"), info.ToString()); Debug.Log(info.ToString());
        }
        /// Acceptance shots: L wall dragged onto the bottom-left corner target (drag ghost, then released -> direction arrows).
        IEnumerator CornerShot()
        {
            var bl = Targets()[0].Item2; var L = Shape("L"); if (L == null) yield break;
            var hand = s.Blocks.Hand; if (hand.IsFull) { hand.Select(0); hand.Consume(); } hand.AddCard(L, RuneRules.NoRune); s.Blocks.NotifyChanged(); yield return null; yield return null;
            int li = -1; for (int i = hand.Cards.Count - 1; i >= 0; i--) if (hand.Cards[i] == L && hand.Runes[i] == RuneRules.NoRune) { li = i; break; }
            s.Towers.Select(-1); s.Blocks.SelectCard(li); yield return null; yield return null;
            Vector2 aim0 = cam.WorldToScreenPoint(VisualTop(bl)); var r = FindFinger(aim0, CellPx(bl), HitsCell(bl));
            string tag = Res + (cfg.notch ? "_notch" : "");
            var info = new StringBuilder("CORNER\t" + tag + "\tcell " + C(bl) + "\tcellTop " + V(aim0) + "\treach " + r.ok + "\tfinger " + V(r.finger) + "\taim " + V(r.aim) + "\toffset " + (r.aim.y - r.finger.y).ToString("0") + "\twindow " + r.window.ToString("0") + "\tfingerOverHand " + pic.IsOverHand(r.finger));
            if (r.ok && li >= 0)
            {
                Vector2 start = CardCentre("Block card " + L.displayName, new Vector2(r.finger.x, 10));
                PointerInput.Inject(start, true, true, false); pic.CardDown(false, li, start); yield return null;
                for (int k = 1; k <= 8; k++) { PointerInput.Inject(Vector2.Lerp(start, r.finger, k / 8f), false, true, false); yield return null; }
                PointerInput.Inject(r.finger, false, true, false); yield return null;
                var sn = Take(false, -1); yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "drag_corner_" + tag + "_drag.png"));
                info.Append("\tdragGhost " + sn.shown + " anchor " + C(sn.anchor) + " valid " + sn.valid + " state " + pic.Machine.State);
                PointerInput.Inject(r.finger, false, false, true); yield return Frames(3);
                sn = Take(false, -1); yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, "drag_corner_" + tag + "_released.png"));
                info.Append("\treleased " + pic.Machine.State + " cell " + C(pic.Machine.Cell) + " centre " + V(pic.Machine.Centre) + " ghost " + Cells(sn.cells));
            }
            pic.CancelPlacement(); PointerInput.ClearInjection(); yield return Frames(3);
            File.AppendAllText(Path.Combine(dir, "corner_shots.txt"), info + "\n");
        }
        IEnumerator DiamondShot(bool tower, int index, Vector2 start, Vector2 finger, int d, string name, StringBuilder info, Vector2Int cell, Vector2 aim)
        {
            float k1 = pic.CanvasScale;
            PointerInput.Inject(start, true, true, false); pic.CardDown(tower, index, start); yield return null;
            for (int k = 1; k <= 8; k++) { PointerInput.Inject(Vector2.Lerp(start, finger, k / 8f), false, true, false); yield return null; }
            float h0 = Time.realtimeSinceStartup; while (pic.Machine.State != PlacementState.Direction && Time.realtimeSinceStartup - h0 < .8f) { PointerInput.Inject(finger, false, true, false); yield return null; }
            Vector2 end = finger + DirV(d) * pic.Machine.SwipeThreshold * k1 * 2.2f;
            for (int k = 1; k <= 4; k++) { PointerInput.Inject(Vector2.Lerp(finger, end, k / 4f), false, true, false); yield return null; }
            for (int k = 0; k < 6; k++) { PointerInput.Inject(end, false, true, false); yield return null; }
            var sn = Take(tower, index); yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, name + ".png"));
            info.Append(name + ": cell " + C(cell) + " state=" + pic.Machine.State + " dir=" + pic.Machine.Dir + " centre " + V(pic.Machine.Centre) + " finger " + V(end) + " ghost cells " + Cells(sn.cells) + " valid=" + sn.valid + "\n");
            info.Append("FINGER\t" + name + "\t" + V(end) + "\t" + V(aim) + "\n");
            pic.CancelPlacement(); PointerInput.ClearInjection(); yield return Frames(3);
        }
        void Capture(string path)
        {
            var c = cam; int W = Screen.width, H = Screen.height; var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            var canvases = FindObjectsOfType<Canvas>(); var modes = new RenderMode[canvases.Length]; var cams = new Camera[canvases.Length]; var dist = new float[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { modes[i] = canvases[i].renderMode; cams[i] = canvases[i].worldCamera; dist[i] = canvases[i].planeDistance; if (!canvases[i].isRootCanvas) continue; canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = c; canvases[i].planeDistance = c.nearClipPlane + .3f - Mathf.Clamp(canvases[i].sortingOrder, 0, 25) * .01f; }
            c.targetTexture = rt; Canvas.ForceUpdateCanvases(); c.Render(); Canvas.ForceUpdateCanvases(); c.Render();
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            c.targetTexture = null; RenderTexture.active = null; rt.Release(); Destroy(rt); Destroy(tex);
            for (int i = 0; i < canvases.Length; i++) { if (!canvases[i].isRootCanvas) continue; canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cams[i]; canvases[i].planeDistance = dist[i]; }
        }
    }
}
#endif
