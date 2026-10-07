using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StoneSignal
{
    /// v18.2 aspect-adaptive game camera (玩法策划: whole board + all entry bridges visible at 16:9 / 19.5:9 / 20:9 / 4:3, notch too).
    /// Keeps the art framing's rotation; solves for the smallest orthographic size (never smaller than the art size 6.6) and then the
    /// smallest pan (camera right/up) so every fit point projects inside the safe area (minus marginPx) and outside every HUD rect
    /// (GameUI.HudObstacles: top bar, hand bottom-left, draw pile + BATTLE bottom-right; padded hudPadPx).
    /// Fit points: the board's top-surface outline (every 0.5 cell) and each entry bridge from the board edge to 0.5 cell onto its
    /// island shore (both sides). fitPortals also requires the spawn portal pad (island centre, radius 1 cell).
    /// Runs when the HUD is ready and again on any resolution / safe-area change; applied as a position delta so CameraShake's additive
    /// offset is untouched.
    public sealed class CameraFit : MonoBehaviour
    {
        public bool fitPortals = false;
        public float marginPx = 12f, hudPadPx = 8f, maxZoomOut = 2.2f, sizeStep = .05f;
        public static CameraFit Instance { get; private set; }
        public float BaseSize { get; private set; }
        public float Size { get; private set; }
        public Vector2 Pan { get; private set; }
        public string Report { get; private set; } = "";
        public int Fits { get; private set; }
        Camera cam; GridManager grid; GameUI ui; Vector3 basePos, applied; Quaternion rot;
        bool has; Vector2Int lastSize; Rect lastSafe;
        readonly List<Vector3> world = new List<Vector3>(256); readonly List<int> kind = new List<int>(256); // 0 board, 1 bridge, 2 portal
        readonly List<Rect> obstacles = new List<Rect>(24);

        public void Initialize(Camera camera, GridManager board, GameUI hud)
        {
            cam = camera; grid = board; ui = hud; Instance = this;
            basePos = applied = cam.transform.position; rot = cam.transform.rotation; BaseSize = Size = cam.orthographicSize;
        }
        /// Forces a re-fit on the next LateUpdate (diagnostics).
        public void Refit() { has = false; }

        void LateUpdate()
        {
            if (cam == null || grid == null || ui == null || !cam.orthographic || !ui.HudReady) return;
            var safe = HudScaler.SafeArea;
            if (has && Screen.width == lastSize.x && Screen.height == lastSize.y && safe == lastSafe) return;
            has = true; lastSize = new Vector2Int(Screen.width, Screen.height); lastSafe = safe;
            Canvas.ForceUpdateCanvases();
            Fit(safe);
        }

        void Points()
        {
            world.Clear(); kind.Clear();
            float cs = grid.cellSize, top = grid.transform.position.y + (grid.tileTop > 0 ? grid.tileTop : .25f);
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

        void Fit(Rect safe)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Points();
            float W = Screen.width, H = Screen.height, asp = W / Mathf.Max(1f, H), k = HudScaler.ScaleFor(Screen.width, Screen.height);
            float margin = marginPx * k; ui.HudObstacles(obstacles, hudPadPx * k);
            Rect area = Rect.MinMaxRect(safe.xMin + margin, safe.yMin + margin, safe.xMax - margin, safe.yMax - margin);
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
            int n = world.Count; var vx = new float[n]; var vy = new float[n];
            for (int i = 0; i < n; i++) { var d = world[i] - basePos; vx[i] = Vector3.Dot(d, right); vy[i] = Vector3.Dot(d, up); }
            float bestS = -1; Vector2 bestPan = Vector2.zero; bool fallback = false; int tested = 0, hot = 0;
            bool TryAt(float s, out Vector2 pan)
            {
                pan = Vector2.zero;
                float ux = 2f * s * asp / W, uy = 2f * s / H;
                float pxMin = float.MinValue, pxMax = float.MaxValue, pyMin = float.MinValue, pyMax = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    pxMin = Mathf.Max(pxMin, vx[i] - (area.xMax - W * .5f) * ux); pxMax = Mathf.Min(pxMax, vx[i] - (area.xMin - W * .5f) * ux);
                    pyMin = Mathf.Max(pyMin, vy[i] - (area.yMax - H * .5f) * uy); pyMax = Mathf.Min(pyMax, vy[i] - (area.yMin - H * .5f) * uy);
                }
                if (pxMin > pxMax || pyMin > pyMax) return false;
                // candidate pans inside the box, nearest to the art framing (pan 0) first
                float step = Mathf.Max(.04f, Mathf.Max(pxMax - pxMin, pyMax - pyMin) / 48f); var cands = new List<Vector2>();
                for (float px = pxMin; px <= pxMax + 1e-4f; px += step) for (float py = pyMin; py <= pyMax + 1e-4f; py += step) cands.Add(new Vector2(px, py));
                cands.Add(new Vector2(Mathf.Clamp(0, pxMin, pxMax), Mathf.Clamp(0, pyMin, pyMax)));
                cands.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
                bool Blocked(int i, Vector2 c)
                {
                    float sx = (vx[i] - c.x) / ux + W * .5f, sy = (vy[i] - c.y) / uy + H * .5f;
                    foreach (var r in obstacles) if (sx > r.xMin && sx < r.xMax && sy > r.yMin && sy < r.yMax) return true;
                    return false;
                }
                foreach (var c in cands)
                {
                    tested++;
                    if (Blocked(hot, c)) continue;   // the point that failed last time usually fails again
                    bool ok = true; for (int i = 0; i < n; i++) if (Blocked(i, c)) { hot = i; ok = false; break; }
                    if (ok) { pan = c; return true; }
                }
                return false;
            }
            // coarse pass (4 steps) to bracket, then fine steps from the last failing coarse size
            float coarse = -1;
            for (float s = BaseSize; s <= BaseSize * maxZoomOut + 1e-4f; s += sizeStep * 4) if (TryAt(s, out _)) { coarse = s; break; }
            if (coarse >= 0)
                for (float s = Mathf.Max(BaseSize, coarse - sizeStep * 3); s <= coarse + 1e-4f; s += sizeStep) if (TryAt(s, out var pan)) { bestS = s; bestPan = pan; break; }
            if (bestS < 0)
            {   // no HUD-clear framing within maxZoomOut: fit the safe area only (logged)
                fallback = true;
                for (float s = BaseSize; s <= BaseSize * 4f && bestS < 0; s += sizeStep)
                {
                    float ux = 2f * s * asp / W, uy = 2f * s / H; float pxMin = float.MinValue, pxMax = float.MaxValue, pyMin = float.MinValue, pyMax = float.MaxValue;
                    for (int i = 0; i < n; i++) { pxMin = Mathf.Max(pxMin, vx[i] - (area.xMax - W * .5f) * ux); pxMax = Mathf.Min(pxMax, vx[i] - (area.xMin - W * .5f) * ux); pyMin = Mathf.Max(pyMin, vy[i] - (area.yMax - H * .5f) * uy); pyMax = Mathf.Min(pyMax, vy[i] - (area.yMin - H * .5f) * uy); }
                    if (pxMin <= pxMax && pyMin <= pyMax) { bestS = s; bestPan = new Vector2(Mathf.Clamp(0, pxMin, pxMax), Mathf.Clamp(0, pyMin, pyMax)); }
                }
                if (bestS < 0) { bestS = BaseSize; bestPan = Vector2.zero; }
            }
            Size = bestS; Pan = bestPan; Fits++;
            var target = basePos + right * bestPan.x + up * bestPan.y;
            cam.transform.position += target - applied; applied = target; cam.orthographicSize = bestS;
            Report = Describe(W, H, area, safe, sw.ElapsedMilliseconds, tested, fallback);
            Debug.Log("CAMERA FIT: " + Report.Replace("\n", " | "));
        }

        string Describe(float W, float H, Rect area, Rect safe, long ms, int tested, bool fallback)
        {
            var sb = new StringBuilder();
            sb.Append("screen ").Append((int)W).Append('x').Append((int)H).Append(" aspect ").Append((W / H).ToString("F3")).Append(" safe ").Append(safe)
              .Append(" ortho ").Append(Size.ToString("F2")).Append(" (art ").Append(BaseSize.ToString("F2")).Append(", x").Append((Size / BaseSize).ToString("F3"))
              .Append(") pan right/up ").Append(Pan.x.ToString("F2")).Append('/').Append(Pan.y.ToString("F2")).Append(" m, ")
              .Append((H / (2f * Size)).ToString("F1")).Append(" px per m (art at this height ").Append((H / (2f * BaseSize)).ToString("F1")).Append(")")
              .Append(fallback ? " FALLBACK(safe area only)" : "").Append(" solve ").Append(ms).Append(" ms, ").Append(tested).Append(" pans tested, ")
              .Append(world.Count).Append(" points, ").Append(obstacles.Count).Append(" HUD rects, portals required=").Append(fitPortals).Append('\n');
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
                sb.Append("  entry ").Append(s).Append(" bridge landing (").Append(bridgeEnd.x.ToString("F0")).Append(',').Append(bridgeEnd.y.ToString("F0")).Append(") portal centre ").Append(portal).Append('\n');
            }
            return sb.ToString();
        }
    }
}
