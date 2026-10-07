using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StoneSignal
{
    /// v18.3 camera fit mode. Screen = the accepted v18.2 fit (default). HudFree = art's experiment: the board must sit in the area the HUD leaves free.
    public enum CameraFitMode { Screen, HudFree }

    /// v18.2 aspect-adaptive game camera (玩法策划: whole board + all entry bridges visible at 16:9 / 19.5:9 / 20:9 / 4:3, notch too).
    /// Keeps the art framing's rotation; solves for the smallest orthographic size (never smaller than the art size 6.6) and then the
    /// smallest pan (camera right/up).
    /// Screen mode: every fit point projects inside the safe area (minus marginPx) and outside every HUD rect (GameUI.HudObstacles with the
    /// block row reserved at FitBlockCards = 3 cards, padded hudPadPx). Fit points: the board's top-surface outline (every 0.5 cell) and each
    /// entry bridge from the board edge to 0.5 cell onto its island shore (both sides). fitPortals also requires the spawn portal pad.
    /// v18.3 pan fix (Screen, aspect < 2, i.e. 16:9 and 4:3): with a FULL 7-card block hand the hand may only cover board EDGE cells - never an
    /// interior cell, an entry landing cell or a flow-arrow path cell (current routes at fit time). If it would, the camera is panned (same size,
    /// never zoomed out further). 19.5:9 / 20:9 are left exactly as accepted.
    /// HudFree mode (art experiment, Mode = HudFree / -camfit hudfree): the board's top-surface parallelogram must not touch any HUD block
    /// (GameUI.HudFreeObstacles: top banner band, bottom-left hand group with 7 block cards + tower cards, bottom-right draw pile + BATTLE,
    /// plus each element) and must lie inside the safe area; each entry's landing cell and the first arrow segment (bridge head, half a cell
    /// beyond the board edge) must be visible too. Islands, bridge tails and portals may be covered or off-screen. Readability floor at
    /// 1080p-equivalent: cell >= minCellPx1080, smallest enemy >= minEnemyPx1080 (reported; if the fit needs more zoom-out it keeps the floor).
    /// Runs when the HUD is ready and again on any resolution / safe-area / mode change; applied as a position delta so CameraShake's additive
    /// offset is untouched.
    public sealed class CameraFit : MonoBehaviour
    {
        public static CameraFitMode Mode = CameraFitMode.Screen;
        public bool fitPortals = false, panFixFullHand = true;
        public float marginPx = 12f, hudPadPx = 8f, maxZoomOut = 2.2f, sizeStep = .05f;
        public float topBandPx = 120f, minCellPx1080 = 52f, minEnemyPx1080 = 44f, bridgeHeadCells = .5f;
        public static CameraFit Instance { get; private set; }
        public float BaseSize { get; private set; }
        public float Size { get; private set; }
        public Vector2 Pan { get; private set; }
        public string Report { get; private set; } = "";
        public string Coverage { get; private set; } = "";
        public int Fits { get; private set; }
        public CameraFitMode FittedMode { get; private set; }
        /// Last analysis (full 7-card hand): board cells covered by the HUD, of which interior / landing / path ("bad"), landing + first arrow
        /// segment visibility, readability (1080p-equivalent px).
        public int CoveredCells { get; private set; }
        public int CoveredBad { get; private set; }
        public bool LandingsVisible { get; private set; }
        public bool ArrowsVisible { get; private set; }
        public float CellPx1080 { get; private set; }
        public float EnemyPx1080 { get; private set; }
        public bool PanFixed { get; private set; }
        Camera cam; GridManager grid; GameUI ui; GameBootstrap session; Vector3 basePos, applied; Quaternion rot;
        bool has; Vector2Int lastSize; Rect lastSafe;
        readonly List<Vector3> world = new List<Vector3>(256); readonly List<int> kind = new List<int>(256); // 0 board, 1 bridge, 2 portal
        readonly List<Rect> obstacles = new List<Rect>(24);
        float enemyUnits = -1; string enemyName = "?";

        public void Initialize(Camera camera, GridManager board, GameUI hud, GameBootstrap s = null)
        {
            cam = camera; grid = board; ui = hud; session = s; Instance = this;
            basePos = applied = cam.transform.position; rot = cam.transform.rotation; BaseSize = Size = cam.orthographicSize;
        }
        /// Forces a re-fit on the next LateUpdate (diagnostics).
        public void Refit() { has = false; }

        void LateUpdate()
        {
            if (cam == null || grid == null || ui == null || !cam.orthographic || !ui.HudReady) return;
            var safe = HudScaler.SafeArea;
            if (has && Screen.width == lastSize.x && Screen.height == lastSize.y && safe == lastSafe && FittedMode == Mode) return;
            has = true; lastSize = new Vector2Int(Screen.width, Screen.height); lastSafe = safe;
            Canvas.ForceUpdateCanvases();
            Fit(safe);
        }

        void Points()
        {
            world.Clear(); kind.Clear();
            float cs = grid.cellSize, top = Top;
            Vector3 o = grid.transform.position; o.y = top; float w = grid.width * cs, h = grid.height * cs;
            Vector3[] c = { o, o + new Vector3(w, 0, 0), o + new Vector3(w, 0, h), o + new Vector3(0, 0, h) };
            for (int e = 0; e < 4; e++)
            {
                var a = c[e]; var b = c[(e + 1) % 4]; int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / (cs * .5f)));
                for (int i = 0; i < n; i++) { world.Add(Vector3.Lerp(a, b, i / (float)n)); kind.Add(0); }
            }
            foreach (var s in grid.Spawns)
            {
                var dir = GridManager.OutwardOf(s, grid.width, grid.height); if (dir == Vector3.zero) continue;
                var side = new Vector3(-dir.z, 0, dir.x); var p = grid.ToWorld(s); p.y = top - .2f;
                float end = Mathf.Max(1f, grid.islandDistance - 2f + .5f);
                for (float t = .5f; t <= end + 1e-3f; t += .5f) for (int k = -1; k <= 1; k += 2) { world.Add(p + dir * t * cs + side * .6f * k * cs); kind.Add(1); }
                if (fitPortals && grid.TryPortalPoint(s, out var pp))
                    for (int k = 0; k < 12; k++) { float an = k * Mathf.PI / 6f; world.Add(pp + new Vector3(Mathf.Cos(an), 0, Mathf.Sin(an)) * cs); kind.Add(2); }
            }
        }
        float Top => grid.transform.position.y + (grid.tileTop > 0 ? grid.tileTop : .25f);
        Vector3 CellCorner(int x, int y) { var o = grid.transform.position; return new Vector3(o.x + x * grid.cellSize, Top, o.z + y * grid.cellSize); }
        public static string Side(Vector3 dir) => dir.x > .5f ? "E" : dir.x < -.5f ? "W" : dir.z < -.5f ? "S" : dir.z > .5f ? "N" : "?";

        /// Separating-axis test: convex quad (screen) vs axis-aligned rect (any overlap of positive area).
        public static bool QuadHitsRect(Vector2[] q, Rect r)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 4; i++) { x0 = Mathf.Min(x0, q[i].x); y0 = Mathf.Min(y0, q[i].y); x1 = Mathf.Max(x1, q[i].x); y1 = Mathf.Max(y1, q[i].y); }
            if (x1 <= r.xMin || x0 >= r.xMax || y1 <= r.yMin || y0 >= r.yMax) return false;
            for (int i = 0; i < 4; i++)
            {
                var e = q[(i + 1) % 4] - q[i]; var n = new Vector2(-e.y, e.x); if (n.sqrMagnitude < 1e-8f) continue;
                float qa = float.MaxValue, qb = float.MinValue; for (int j = 0; j < 4; j++) { float d = Vector2.Dot(n, q[j]); qa = Mathf.Min(qa, d); qb = Mathf.Max(qb, d); }
                float d0 = Vector2.Dot(n, new Vector2(r.xMin, r.yMin)), d1 = Vector2.Dot(n, new Vector2(r.xMax, r.yMin)), d2 = Vector2.Dot(n, new Vector2(r.xMin, r.yMax)), d3 = Vector2.Dot(n, new Vector2(r.xMax, r.yMax));
                float ra = Mathf.Min(Mathf.Min(d0, d1), Mathf.Min(d2, d3)), rb = Mathf.Max(Mathf.Max(d0, d1), Mathf.Max(d2, d3));
                if (qb <= ra + 1e-3f || rb <= qa + 1e-3f) return false;
            }
            return true;
        }

        void Fit(Rect safe)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Points(); FittedMode = Mode; PanFixed = false;
            float W = Screen.width, H = Screen.height, asp = W / Mathf.Max(1f, H), k = HudScaler.ScaleFor(Screen.width, Screen.height);
            float margin = marginPx * k;
            Rect area = Rect.MinMaxRect(safe.xMin + margin, safe.yMin + margin, safe.xMax - margin, safe.yMax - margin);
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
            Vector2 V(Vector3 p) { var d = p - basePos; return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)); }
            Vector2 S(Vector2 v, float s, Vector2 c) { float u = 2f * s / H; return new Vector2((v.x - c.x) / u + W * .5f, (v.y - c.y) / u + H * .5f); }
            float bestS = -1; Vector2 bestPan = Vector2.zero; bool fallback = false; int tested = 0; string note = "";
            // pan box (camera right/up) that keeps every view point inside the area at size s
            bool Box(List<Vector2> pts, float s, out float pxMin, out float pxMax, out float pyMin, out float pyMax)
            {
                float u = 2f * s / H; pxMin = pyMin = float.MinValue; pxMax = pyMax = float.MaxValue;
                foreach (var v in pts)
                {
                    pxMin = Mathf.Max(pxMin, v.x - (area.xMax - W * .5f) * u); pxMax = Mathf.Min(pxMax, v.x - (area.xMin - W * .5f) * u);
                    pyMin = Mathf.Max(pyMin, v.y - (area.yMax - H * .5f) * u); pyMax = Mathf.Min(pyMax, v.y - (area.yMin - H * .5f) * u);
                }
                return pxMin <= pxMax && pyMin <= pyMax;
            }
            List<Vector2> Cands(float pxMin, float pxMax, float pyMin, float pyMax, Vector2 near)
            {
                float step = Mathf.Max(.04f, Mathf.Max(pxMax - pxMin, pyMax - pyMin) / 48f); var cands = new List<Vector2>();
                for (float px = pxMin; px <= pxMax + 1e-4f; px += step) for (float py = pyMin; py <= pyMax + 1e-4f; py += step) cands.Add(new Vector2(px, py));
                cands.Add(new Vector2(Mathf.Clamp(near.x, pxMin, pxMax), Mathf.Clamp(near.y, pyMin, pyMax)));
                cands.Sort((a, b) => (a - near).sqrMagnitude.CompareTo((b - near).sqrMagnitude));
                return cands;
            }
            int n = world.Count; var pv = new List<Vector2>(n); for (int i = 0; i < n; i++) pv.Add(V(world[i]));
            // board top parallelogram + cells (view space)
            var boardV = new[] { V(CellCorner(0, 0)), V(CellCorner(grid.width, 0)), V(CellCorner(grid.width, grid.height)), V(CellCorner(0, grid.height)) };
            Vector2[] Quad(Vector2[] qv, float s, Vector2 c, Vector2[] buf) { for (int i = 0; i < 4; i++) buf[i] = S(qv[i], s, c); return buf; }
            var qb = new Vector2[4];

            if (Mode == CameraFitMode.Screen)
            {
                ui.HudObstacles(obstacles, hudPadPx * k);
                int hot = 0;
                bool TryAt(float s, out Vector2 pan)
                {
                    pan = Vector2.zero;
                    if (!Box(pv, s, out var pxMin, out var pxMax, out var pyMin, out var pyMax)) return false;
                    bool Blocked(int i, Vector2 c)
                    {
                        var sp = S(pv[i], s, c);
                        foreach (var r in obstacles) if (sp.x > r.xMin && sp.x < r.xMax && sp.y > r.yMin && sp.y < r.yMax) return true;
                        return false;
                    }
                    foreach (var c in Cands(pxMin, pxMax, pyMin, pyMax, Vector2.zero))
                    {
                        tested++;
                        if (Blocked(hot, c)) continue;   // the point that failed last time usually fails again
                        bool ok = true; for (int i = 0; i < n; i++) if (Blocked(i, c)) { hot = i; ok = false; break; }
                        if (ok) { pan = c; return true; }
                    }
                    return false;
                }
                float coarse = -1;
                for (float s = BaseSize; s <= BaseSize * maxZoomOut + 1e-4f; s += sizeStep * 4) if (TryAt(s, out _)) { coarse = s; break; }
                if (coarse >= 0)
                    for (float s = Mathf.Max(BaseSize, coarse - sizeStep * 3); s <= coarse + 1e-4f; s += sizeStep) if (TryAt(s, out var pan)) { bestS = s; bestPan = pan; break; }
                if (bestS >= 0 && panFixFullHand && asp < 2f)
                {   // v18.3: full 7-card hand may cover EDGE cells only -> pan (same size) if an interior / landing / path cell would be covered
                    var full = new List<Rect>(); ui.HudObstacles(full, 0f, BlockHandManager.MaxCards, true);
                    var bad = BadCells(); var cellQ = new List<Vector2[]>();
                    foreach (var c in bad) cellQ.Add(new[] { V(CellCorner(c.x, c.y)), V(CellCorner(c.x + 1, c.y)), V(CellCorner(c.x + 1, c.y + 1)), V(CellCorner(c.x, c.y + 1)) });
                    bool Clear(float s, Vector2 c)
                    {
                        foreach (var q in cellQ) { Quad(q, s, c, qb); foreach (var r in full) if (QuadHitsRect(qb, r)) return false; }
                        return true;
                    }
                    if (!Clear(bestS, bestPan))
                    {
                        Box(pv, bestS, out var pxMin, out var pxMax, out var pyMin, out var pyMax); bool found = false;
                        foreach (var c in Cands(pxMin, pxMax, pyMin, pyMax, bestPan))
                        {
                            tested++; bool ok = true;
                            for (int i = 0; i < n && ok; i++) { var sp = S(pv[i], bestS, c); foreach (var r in obstacles) if (sp.x > r.xMin && sp.x < r.xMax && sp.y > r.yMin && sp.y < r.yMax) { ok = false; break; } }
                            if (ok && Clear(bestS, c)) { note = " PAN FIX (7-card hand): " + bestPan.ToString("F2") + " -> " + c.ToString("F2"); bestPan = c; found = true; PanFixed = true; break; }
                        }
                        if (!found) note = " PAN FIX FAILED: no pan at this size keeps interior / landing / path cells clear of the 7-card hand (not zoomed out)";
                    }
                    else note = " pan fix not needed (7-card hand covers edge cells only)";
                }
            }
            else
            {   // ---- HudFree (art experiment)
                ui.HudFreeObstacles(obstacles, hudPadPx * k, topBandPx);
                var heads = new List<Vector2>(); var must = new List<Vector2>(boardV);
                foreach (var sp in grid.Spawns)
                {
                    var dir = GridManager.OutwardOf(sp, grid.width, grid.height); if (dir == Vector3.zero) continue;
                    var side = new Vector3(-dir.z, 0, dir.x); var p = grid.ToWorld(sp); p.y = Top;
                    foreach (float t in new[] { .5f, .5f + bridgeHeadCells }) for (int s2 = -1; s2 <= 1; s2 += 2) heads.Add(V(p + dir * t * grid.cellSize + side * .45f * s2 * grid.cellSize));
                }
                var all = new List<Vector2>(must); all.AddRange(heads);
                int Hits(float s, Vector2 c, bool withHeads)
                {
                    int h = 0; Quad(boardV, s, c, qb);
                    foreach (var r in obstacles) if (QuadHitsRect(qb, r)) h++;
                    if (withHeads) foreach (var v in heads) { var sp = S(v, s, c); foreach (var r in obstacles) if (r.Contains(sp)) { h++; break; } }
                    return h;
                }
                bool TryFree(float s, bool withHeads, out Vector2 pan, out int hits)
                {
                    pan = Vector2.zero; hits = int.MaxValue;
                    if (!Box(withHeads ? all : must, s, out var pxMin, out var pxMax, out var pyMin, out var pyMax)) return false;
                    foreach (var c in Cands(pxMin, pxMax, pyMin, pyMax, Vector2.zero))
                    {
                        tested++; int h = Hits(s, c, withHeads);
                        if (h < hits) { hits = h; pan = c; }
                        if (h == 0) return true;
                    }
                    return false;
                }
                float coarse = -1;
                for (float s = BaseSize; s <= BaseSize * maxZoomOut + 1e-4f; s += sizeStep * 4) if (TryFree(s, true, out _, out _)) { coarse = s; break; }
                if (coarse >= 0)
                    for (float s = Mathf.Max(BaseSize, coarse - sizeStep * 3); s <= coarse + 1e-4f; s += sizeStep) if (TryFree(s, true, out var pan, out _)) { bestS = s; bestPan = pan; break; }
                float cellU = CellUnits(right, up), sCap = Mathf.Min(540f * cellU / minCellPx1080, EnemyUnits(right, up) > 0 ? 540f * enemyUnits / minEnemyPx1080 : float.MaxValue);
                note = " HudFree: free-area fit " + (bestS >= 0 ? bestS.ToString("F2") : "none <= " + (BaseSize * maxZoomOut).ToString("F2")) + ", readability cap " + sCap.ToString("F2") + " (cell >= " + minCellPx1080 + " px, enemy >= " + minEnemyPx1080 + " px at 1080p)";
                if (bestS < 0 || bestS > sCap + 1e-3f)
                {   // keep the readability floor: board only (bridge heads may go), else the pan with the fewest HUD hits
                    float s = Mathf.Max(BaseSize, Mathf.Min(sCap, BaseSize * maxZoomOut));
                    if (TryFree(s, false, out var p0, out _)) { bestS = s; bestPan = p0; note += " -> CAP: size " + s.ToString("F2") + ", board clear, bridge heads not guaranteed"; }
                    else { TryFree(s, false, out var p1, out int h1); bestS = s; bestPan = p1; fallback = h1 == int.MaxValue; note += " -> CAP: size " + s.ToString("F2") + ", board still touches " + (h1 == int.MaxValue ? "?" : h1.ToString()) + " HUD block(s)"; }
                }
            }
            if (bestS < 0)
            {   // no HUD-clear framing within maxZoomOut: fit the safe area only (logged)
                fallback = true;
                for (float s = BaseSize; s <= BaseSize * 4f && bestS < 0; s += sizeStep)
                    if (Box(pv, s, out var pxMin, out var pxMax, out var pyMin, out var pyMax)) { bestS = s; bestPan = new Vector2(Mathf.Clamp(0, pxMin, pxMax), Mathf.Clamp(0, pyMin, pyMax)); }
                if (bestS < 0) { bestS = BaseSize; bestPan = Vector2.zero; }
            }
            Size = bestS; Pan = bestPan; Fits++;
            var target = basePos + right * bestPan.x + up * bestPan.y;
            cam.transform.position += target - applied; applied = target; cam.orthographicSize = bestS;
            Report = "mode " + Mode + " " + Describe(W, H, area, safe, sw.ElapsedMilliseconds, tested, fallback, note);
            Coverage = Analyze(right, up);
            Debug.Log("CAMERA FIT: " + Report.Replace("\n", " | ") + " || " + Coverage.Replace("\n", " | "));
        }

        /// Cells that the full hand must never cover: interior cells, entry landing cells, current flow-arrow path cells.
        List<Vector2Int> BadCells()
        {
            var set = new HashSet<Vector2Int>();
            for (int x = 1; x < grid.width - 1; x++) for (int y = 1; y < grid.height - 1; y++) set.Add(new Vector2Int(x, y));
            foreach (var s in grid.Spawns) set.Add(s);
            if (session != null && session.Paths != null) foreach (var p in session.Paths.CurrentPaths) if (p != null) foreach (var c in p) set.Add(c);
            return new List<Vector2Int>(set);
        }
        float CellUnits(Vector3 right, Vector3 up) { var x = Vector3.right * grid.cellSize; return new Vector2(Vector3.Dot(x, right), Vector3.Dot(x, up)).magnitude; }
        /// Smallest enemy on screen (view units, max of projected width / height of its mesh bounds; split children at splitChildScale).
        float EnemyUnits(Vector3 right, Vector3 up)
        {
            if (enemyUnits >= 0) return enemyUnits;
            enemyUnits = 0; var cfg = session != null ? session.config : null; if (cfg == null || cfg.waves == null) return 0;
            var list = new List<(EnemyData d, float scale)>();
            foreach (var w in cfg.waves) if (w != null && w.groups != null) foreach (var g in w.groups) if (g != null && g.enemy != null)
            { list.Add((g.enemy, 1f)); if (g.enemy.splitChild != null) list.Add((g.enemy.splitChild, g.enemy.splitChildScale)); }
            float best = float.MaxValue;
            foreach (var (d, scale) in list)
            {
                if (d.visualPrefab == null) continue; var root = d.visualPrefab.transform; var b = new Bounds(); bool any = false;
                void Enc(Mesh m, Transform t) { if (m == null) return; var mb = m.bounds; var M = root.worldToLocalMatrix * t.localToWorldMatrix;
                    for (int i = 0; i < 8; i++) { var p = M.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1))); if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); } }
                foreach (var mf in d.visualPrefab.GetComponentsInChildren<MeshFilter>(true)) Enc(mf.sharedMesh, mf.transform);
                foreach (var sm in d.visualPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Enc(sm.sharedMesh, sm.transform);
                if (!any) continue;
                float wx = 0, wy = 0, x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var p = (b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1))) * scale;
                    float px = Vector3.Dot(p, right), py = Vector3.Dot(p, up); x0 = Mathf.Min(x0, px); x1 = Mathf.Max(x1, px); y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
                }
                wx = x1 - x0; wy = y1 - y0; float u = Mathf.Max(wx, wy);
                if (u > 0 && u < best) { best = u; enemyName = d.name + (scale != 1f ? " x" + scale.ToString("F2") : "") + " (" + wx.ToString("F2") + " x " + wy.ToString("F2") + " u)"; }
            }
            enemyUnits = best == float.MaxValue ? 0 : best; return enemyUnits;
        }

        /// Coverage with the WORST-CASE hand (7 block cards + tower cards, deep rects, unpadded): covered board cells (E edge, L landing,
        /// P path, I interior), landing + first arrow segment visibility, portals on screen, readability.
        string Analyze(Vector3 right, Vector3 up)
        {
            float W = Screen.width, H = Screen.height; var full = new List<Rect>(); ui.HudObstacles(full, 0f, BlockHandManager.MaxCards, true);
            var safe = HudScaler.SafeArea; var path = new HashSet<Vector2Int>(); var first = new HashSet<Vector2Int>();
            if (session != null && session.Paths != null) foreach (var p in session.Paths.CurrentPaths) if (p != null) { for (int i = 0; i < p.Count; i++) { path.Add(p[i]); if (i < 2) first.Add(p[i]); } }
            var spawns = new HashSet<Vector2Int>(grid.Spawns); var q = new Vector2[4]; var sb = new StringBuilder(); int covered = 0, bad = 0;
            Vector2 Sc(Vector3 wp) => cam.WorldToScreenPoint(wp);
            bool Covered(int x, int y) { q[0] = Sc(CellCorner(x, y)); q[1] = Sc(CellCorner(x + 1, y)); q[2] = Sc(CellCorner(x + 1, y + 1)); q[3] = Sc(CellCorner(x, y + 1)); foreach (var r in full) if (QuadHitsRect(q, r)) return true; return false; }
            bool Inside(int x, int y) { for (int i = 0; i < 4; i++) { var p = Sc(CellCorner(x + (i & 1), y + (i >> 1))); if (!safe.Contains(p)) return false; } return true; }
            var list = new StringBuilder();
            for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
            {
                bool off = !Inside(x, y), cov = Covered(x, y); if (!cov && !off) continue;
                var c = new Vector2Int(x, y); bool edge = x == 0 || y == 0 || x == grid.width - 1 || y == grid.height - 1;
                string tag = (spawns.Contains(c) ? "L" : "") + (path.Contains(c) ? "P" : "") + (edge ? "E" : "I") + (off ? "/off" : "");
                covered++; if (!edge || spawns.Contains(c) || path.Contains(c)) bad++;
                list.Append(" (").Append(x).Append(',').Append(y).Append(')').Append(tag);
            }
            CoveredCells = covered; CoveredBad = bad;
            bool land = true, arrows = true; var ends = new StringBuilder(); var portals = new StringBuilder();
            foreach (var s in grid.Spawns)
            {
                var dir = GridManager.OutwardOf(s, grid.width, grid.height); string side = Side(dir);
                bool lv = Inside(s.x, s.y) && !Covered(s.x, s.y); land &= lv;
                bool av = true; var p = grid.ToWorld(s); p.y = Top;
                foreach (float t in new[] { .5f, .5f + bridgeHeadCells }) for (int k = -1; k <= 1; k += 2)
                { Vector2 sp = Sc(p + dir * t * grid.cellSize + new Vector3(-dir.z, 0, dir.x) * .45f * k * grid.cellSize); if (!safe.Contains(sp)) av = false; foreach (var r in full) if (r.Contains(sp)) av = false; }
                if (session != null && session.Paths != null) foreach (var pa in session.Paths.CurrentPaths) if (pa != null && pa.Count > 1 && pa[0] == s) { if (!Inside(pa[1].x, pa[1].y) || Covered(pa[1].x, pa[1].y)) av = false; }
                arrows &= av;
                string po = "-"; if (grid.TryPortalPoint(s, out var pp)) { var ps = Sc(pp); po = ps.x >= 0 && ps.x <= W && ps.y >= 0 && ps.y <= H ? "on" : "off"; }
                ends.Append(' ').Append(side).Append(s).Append(" landing ").Append(lv ? "visible" : "NOT VISIBLE").Append(", first arrow segment ").Append(av ? "visible" : "NOT VISIBLE");
                portals.Append(side).Append('=').Append(po).Append(' ');
            }
            LandingsVisible = land; ArrowsVisible = arrows;
            float cellPx = (Sc(grid.ToWorld(new Vector2Int(grid.width / 2, grid.height / 2)) + Vector3.right * grid.cellSize) - Sc(grid.ToWorld(new Vector2Int(grid.width / 2, grid.height / 2)))).magnitude;
            float depthPx = (Sc(grid.ToWorld(new Vector2Int(grid.width / 2, grid.height / 2)) + Vector3.forward * grid.cellSize) - Sc(grid.ToWorld(new Vector2Int(grid.width / 2, grid.height / 2)))).magnitude;
            CellPx1080 = cellPx * 1080f / H; EnemyUnits(right, up); EnemyPx1080 = enemyUnits * 540f / Size;
            sb.Append("coverage (worst case: 7 block cards + tower cards, deep HUD rects): ").Append(covered).Append(" board cell(s) covered or off-screen, ")
              .Append(bad).Append(" of them interior/landing/path").Append(covered > 0 ? ":" + list : "").Append('\n')
              .Append("  landings / first arrow segment:").Append(ends).Append(" | portals ").Append(portals).Append('\n')
              .Append("  readability: cell ").Append(cellPx.ToString("F1")).Append(" px wide (depth ").Append(depthPx.ToString("F1")).Append(" px), 1080p-equivalent ").Append(CellPx1080.ToString("F1"))
              .Append(" px (floor ").Append(minCellPx1080).Append("); smallest enemy ").Append(enemyName).Append(" -> ").Append((enemyUnits * H / (2f * Size)).ToString("F1")).Append(" px, 1080p-equivalent ")
              .Append(EnemyPx1080.ToString("F1")).Append(" px (floor ").Append(minEnemyPx1080).Append(")");
            return sb.ToString();
        }

        string Describe(float W, float H, Rect area, Rect safe, long ms, int tested, bool fallback, string note)
        {
            var sb = new StringBuilder();
            sb.Append("screen ").Append((int)W).Append('x').Append((int)H).Append(" aspect ").Append((W / H).ToString("F3")).Append(" safe ").Append(safe)
              .Append(" ortho ").Append(Size.ToString("F2")).Append(" (art ").Append(BaseSize.ToString("F2")).Append(", x").Append((Size / BaseSize).ToString("F3"))
              .Append(") pan right/up ").Append(Pan.x.ToString("F2")).Append('/').Append(Pan.y.ToString("F2")).Append(" m, ")
              .Append((H / (2f * Size)).ToString("F1")).Append(" px per m (art at this height ").Append((H / (2f * BaseSize)).ToString("F1")).Append(")")
              .Append(fallback ? " FALLBACK(safe area only)" : "").Append(" solve ").Append(ms).Append(" ms, ").Append(tested).Append(" pans tested, ")
              .Append(world.Count).Append(" points, ").Append(obstacles.Count).Append(" HUD rects, portals required=").Append(fitPortals).Append(note).Append('\n');
            float minClear = float.MaxValue; var bb = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (int i = 0; i < world.Count; i++)
            {
                var sp = cam.WorldToScreenPoint(world[i]);
                float d = Mathf.Min(Mathf.Min(sp.x - area.xMin, area.xMax - sp.x), Mathf.Min(sp.y - area.yMin, area.yMax - sp.y));
                foreach (var r in obstacles) { float dx = Mathf.Max(r.xMin - sp.x, sp.x - r.xMax), dy = Mathf.Max(r.yMin - sp.y, sp.y - r.yMax); d = Mathf.Min(d, Mathf.Max(dx, dy)); }
                minClear = Mathf.Min(minClear, d);
                if (kind[i] == 0) bb = Rect.MinMaxRect(Mathf.Min(bb.xMin, sp.x), Mathf.Min(bb.yMin, sp.y), Mathf.Max(bb.xMax, sp.x), Mathf.Max(bb.yMax, sp.y));
            }
            sb.Append("  min clearance of fit points (to safe-area margin / padded HUD rects) ").Append(minClear.ToString("F0")).Append(" px; board outline screen box ").Append(bb).Append('\n');
            foreach (var s in grid.Spawns)
            {
                var dir = GridManager.OutwardOf(s, grid.width, grid.height); var p = grid.ToWorld(s);
                var bridgeEnd = cam.WorldToScreenPoint(p + dir * Mathf.Max(1f, grid.islandDistance - 2f + .5f) * grid.cellSize);
                string portal = "-"; if (grid.TryPortalPoint(s, out var pp)) { var ps = cam.WorldToScreenPoint(pp); portal = "(" + ps.x.ToString("F0") + "," + ps.y.ToString("F0") + ")" + (ps.x >= 0 && ps.x <= W && ps.y >= 0 && ps.y <= H ? " on-screen" : " OFF-screen"); }
                sb.Append("  entry ").Append(Side(dir)).Append(s).Append(" bridge end (").Append(bridgeEnd.x.ToString("F0")).Append(',').Append(bridgeEnd.y.ToString("F0")).Append(") portal centre ").Append(portal).Append('\n');
            }
            return sb.ToString();
        }
    }
}
