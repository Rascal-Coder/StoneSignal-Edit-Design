using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    /// v17.3 visual walk-surface heights (footprints / ground decals only; gameplay heights never read this).
    /// Filled once by GridView while the board is built:
    ///  - board cells: placed tile top = tile pivot y (grid plane + per-cell visual undulation BoardArt.HeightOffset) + ArtCatalog.tileTop
    ///    + TileRoughness (rough_tile top relief, build_stylized_batch1.py: within +-2 cm)
    ///  - bridges + shore islands: a 1D height profile per entry, sampled from the level-dressing meshes (same temporary MeshColliders
    ///    GridView.ProbeIslands already uses): every 0.1 m from the board edge out past the island centre, 5 lateral rays (+-0.36 m),
    ///    topmost hit below the handrails, max over a +-0.1 m window so the 3 cm gaps between planks never read as a hole.
    /// TryGet: board cell -> tile top; else nearest bridge/island profile within ProfileHalfWidth -> deck / sand top; else false
    /// (EnemyGroundFx then falls back to the walker's feet height).
    public static class WalkSurface
    {
        public const float TileRoughness = .015f, ProfileStep = .1f, ProfileHalfWidth = .75f;
        struct Profile { public Vector3 start, dir; public float[] h; }
        static readonly Dictionary<Vector2Int, float> tiles = new Dictionary<Vector2Int, float>();
        static readonly List<Profile> profiles = new List<Profile>();
        static GridManager grid;
        public static int TileCount => tiles.Count;
        public static int ProfileCount => profiles.Count;

        public static void Begin(GridManager g) { grid = g; tiles.Clear(); profiles.Clear(); }
        public static void SetTile(Vector2Int cell, float topY) { tiles[cell] = topY; }
        /// start = world point on the board edge (y ignored), dir = outward (xz), h[i] = surface top at start + dir * i * ProfileStep (NaN = no ground)
        public static void AddProfile(Vector3 start, Vector3 dir, float[] h) { dir.y = 0; profiles.Add(new Profile { start = start, dir = dir.normalized, h = h }); }

        public static bool TryGet(Vector3 p, out float y)
        {
            y = 0;
            if (grid != null)
            {
                var c = grid.ToCell(p);
                if (grid.InBounds(c)) return tiles.TryGetValue(c, out y);
            }
            float best = float.MaxValue; bool ok = false;
            foreach (var pr in profiles)
            {
                var d = p - pr.start; d.y = 0;
                float along = Vector3.Dot(d, pr.dir), lat = Mathf.Abs(d.x * pr.dir.z - d.z * pr.dir.x);
                if (lat > ProfileHalfWidth || along < -ProfileStep) continue;
                float f = Mathf.Max(0, along) / ProfileStep; int i = Mathf.FloorToInt(f); if (i >= pr.h.Length) continue;
                float a = pr.h[i], b = i + 1 < pr.h.Length ? pr.h[i + 1] : a;
                if (float.IsNaN(a)) a = b; if (float.IsNaN(b)) b = a; if (float.IsNaN(a)) continue;
                if (lat < best) { best = lat; y = Mathf.Lerp(a, b, f - i); ok = true; }
            }
            return ok;
        }

        /// Samples one entry's bridge + island top. cols = readable dressing MeshColliders; railCutoff = ignore hits above this y (handrails, posts, lanterns).
        public static float[] SampleProfile(IList<MeshCollider> cols, Vector3 start, Vector3 dir, float length, float railCutoff)
        {
            dir.y = 0; dir.Normalize(); var side = new Vector3(dir.z, 0, -dir.x);
            int n = Mathf.Max(2, Mathf.CeilToInt(length / ProfileStep) + 1); var raw = new float[n];
            for (int i = 0; i < n; i++)
            {
                float top = float.NaN;
                for (int k = -2; k <= 2; k++)
                {
                    var o = start + dir * (i * ProfileStep) + side * (k * .18f);
                    var ray = new Ray(new Vector3(o.x, 30, o.z), Vector3.down);
                    foreach (var c in cols)
                    {
                        if (!c || !c.Raycast(ray, out var hit, 60)) continue;
                        float hy = hit.point.y;
                        if (hy > railCutoff) { if (c.Raycast(new Ray(new Vector3(o.x, railCutoff, o.z), Vector3.down), out var h2, 60)) hy = h2.point.y; else continue; }
                        if (hy > .15f && (float.IsNaN(top) || hy > top)) top = hy;   // .15: above the water plane
                    }
                }
                raw[i] = top;
            }
            var h = new float[n];
            for (int i = 0; i < n; i++)
            {
                float m = float.NaN;
                for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(n - 1, i + 1); j++) if (!float.IsNaN(raw[j]) && (float.IsNaN(m) || raw[j] > m)) m = raw[j];
                h[i] = m;
            }
            return h;
        }
    }
}
