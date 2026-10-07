using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public enum CellState { Empty, Blocked, TowerSlot, Spawn, Goal }

    public sealed class GridManager : MonoBehaviour
    {
        [Min(4)] public int width = 16;
        [Min(4)] public int height = 10;
        [Min(.1f)] public float cellSize = 1;
        [Tooltip("Optional. When set, size, spawns and the core footprint come from this asset; otherwise the legacy 1-spawn / 1-cell-goal board is used.")]
        public BoardLayoutData layout;
        // Compatibility: first spawn and first core cell. Any core cell is a valid path target.
        public Vector2Int spawn = new Vector2Int(0, 4);
        public Vector2Int goal = new Vector2Int(15, 4);
        private readonly List<Vector2Int> spawns = new List<Vector2Int>();
        private readonly List<Vector2Int> core = new List<Vector2Int>();
        private CellState[,] cells;
        public event Action Changed;
        public IReadOnlyList<Vector2Int> Spawns => spawns;
        public IReadOnlyList<Vector2Int> CoreCells => core;

        public void Initialize()
        {
            if (layout != null) { width = layout.width; height = layout.height; cellSize = layout.cellSize; islandDistance = layout.entryIslandDistance; islandHeight = layout.entryIslandHeight; }
            cells = new CellState[width, height];
            spawns.Clear(); core.Clear();
            if (layout != null && layout.spawns != null)
                foreach (var s in layout.spawns) if (InBounds(s) && !spawns.Contains(s)) spawns.Add(s);
            if (spawns.Count == 0) spawns.Add(new Vector2Int(0, height / 2 - 1));
            if (layout != null)
            {
                for (int y = 0; y < Mathf.Max(1, layout.coreSize.y); y++) for (int x = 0; x < Mathf.Max(1, layout.coreSize.x); x++)
                {
                    var c = layout.coreOrigin + new Vector2Int(x, y);
                    if (InBounds(c) && !spawns.Contains(c)) core.Add(c);
                }
            }
            if (core.Count == 0) core.Add(new Vector2Int(width - 1, height / 2 - 1));
            foreach (var s in spawns) cells[s.x, s.y] = CellState.Spawn;
            foreach (var c in core) cells[c.x, c.y] = CellState.Goal;
            spawn = spawns[0]; goal = core[0];
        }

        public bool InBounds(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;
        public CellState Get(Vector2Int p) => InBounds(p) && cells != null ? cells[p.x, p.y] : CellState.Blocked;
        public bool IsCore(Vector2Int p) => Get(p) == CellState.Goal;
        public bool CanPlace(Vector2Int p) => InBounds(p) && Get(p) == CellState.Empty;
        public bool Walkable(Vector2Int p) => InBounds(p) && Get(p) != CellState.Blocked && Get(p) != CellState.TowerSlot;
        public float islandDistance = 5.1f, islandHeight = 0f;
        public static Vector3 OutwardOf(Vector2Int c, int w, int h) => c.x == 0 ? Vector3.left : c.x == w - 1 ? Vector3.right : c.y == 0 ? Vector3.back : c.y == h - 1 ? Vector3.forward : Vector3.zero;
        /// <summary>Spawn portal position: centre of the shore island beyond the entry bridge. Enemies start here and walk the bridge to the entry cell.</summary>
        readonly Dictionary<Vector2Int, Vector3> portalPoints = new Dictionary<Vector2Int, Vector3>();
        /// Measured island-top centre (GridView island probe); overrides the layout distance estimate.
        public void SetPortalPoint(Vector2Int entry, Vector3 p) => portalPoints[entry] = p;
        public bool TryPortalPoint(Vector2Int entry, out Vector3 p)
        {
            if (portalPoints.TryGetValue(entry, out p)) return true;
            var o = OutwardOf(entry, width, height); p = ToWorld(entry) + o * islandDistance * cellSize + Vector3.up * islandHeight;
            return o != Vector3.zero && spawns.Contains(entry);
        }
        /// Open water under the middle of an entry's bridge (board edge at .5 cell, 4x4 island edge at islandDistance - 2 cells).
        public Vector3 BridgeWaterPoint(Vector2Int entry) { float mid = (.5f + Mathf.Max(.5f, islandDistance - 2f)) * .5f; return ToWorld(entry) + OutwardOf(entry, width, height) * mid * cellSize; }
        /// Visible surface heights above the grid plane (art tile top / wall top), set by GameBootstrap from the ArtCatalog.
        public float tileTop, wallTop;
        bool Raised(Vector2Int c) { var s = Get(c); return InBounds(c) && (s == CellState.Blocked || s == CellState.TowerSlot); }
        /// The cell the player sees under a screen ray: walking down from the wall top to the tile top, the first raised (wall / tower)
        /// cell the ray enters (its top or its camera-facing side), otherwise the tile cell. Picking on the y = 0 grid plane instead
        /// put the snapped cell up to wallTop / tan(pitch) away from the wall top the finger aims at. False if the ray never goes down.
        public bool RaycastCell(Ray ray, out Vector2Int cell, out Vector3 point)
        {
            cell = default; point = default; float y0 = transform.position.y;
            if (ray.direction.y > -1e-5f) return false;
            Vector3 At(float h) => ray.origin + ray.direction * ((y0 + h - ray.origin.y) / ray.direction.y);
            if (wallTop > tileTop)
            {
                const int Steps = 16;
                for (int i = 0; i <= Steps; i++) { var p = At(Mathf.Lerp(wallTop, tileTop, i / (float)Steps)); var c = ToCell(p); if (Raised(c)) { cell = c; point = p; return true; } }
            }
            point = At(tileTop); cell = ToCell(point); return true;
        }
        /// Keeps a footprint on the board (aiming at an edge cell with a 2x2 shifts it inward instead of hanging off the edge).
        public Vector2Int ClampOrigin(Vector2Int origin, Vector2Int size) => new Vector2Int(Mathf.Clamp(origin.x, 0, Mathf.Max(0, width - Mathf.Max(1, size.x))), Mathf.Clamp(origin.y, 0, Mathf.Max(0, height - Mathf.Max(1, size.y))));
        public Vector3 ToWorld(Vector2Int p) => transform.position + new Vector3((p.x + .5f) * cellSize, 0, (p.y + .5f) * cellSize);
        public Vector3 CoreCenter
        {
            get { Vector3 sum = Vector3.zero; foreach (var c in core) sum += ToWorld(c); return core.Count > 0 ? sum / core.Count : ToWorld(goal); }
        }
        public Vector3 BoardCenter => transform.position + new Vector3(width * cellSize * .5f, 0, height * cellSize * .5f);
        public Vector2Int ToCell(Vector3 world)
        {
            Vector3 p = world - transform.position;
            return new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
        }
        // Outward direction for an edge spawn (zero for interior cells).
        public Vector2Int EdgeNormal(Vector2Int p)
        {
            if (p.x == 0) return Vector2Int.left;
            if (p.x == width - 1) return Vector2Int.right;
            if (p.y == 0) return Vector2Int.down;
            if (p.y == height - 1) return Vector2Int.up;
            return Vector2Int.zero;
        }

        // Only called after validation; no temporary mutation of live cells.
        public void Commit(IReadOnlyList<Vector2Int> positions, CellState state)
        {
            for (int i = 0; i < positions.Count; i++)
                if (!CanPlace(positions[i])) throw new InvalidOperationException("Commit requires validated empty cells.");
            for (int i = 0; i < positions.Count; i++) cells[positions[i].x, positions[i].y] = state;
            Changed?.Invoke();
        }

        // ---- Tower footprints: origin = lower-left cell, size already rotated. ----
        public static Vector2Int RotatedSize(Vector2Int size, int rotation) => (rotation & 1) == 1 ? new Vector2Int(size.y, size.x) : size;
        public List<Vector2Int> Footprint(Vector2Int origin, Vector2Int size)
        {
            var list = new List<Vector2Int>(Mathf.Max(1, size.x * size.y));
            for (int y = 0; y < Mathf.Max(1, size.y); y++) for (int x = 0; x < Mathf.Max(1, size.x); x++) list.Add(origin + new Vector2Int(x, y));
            return list;
        }
        public Vector3 FootprintCenter(Vector2Int origin, Vector2Int size) =>
            transform.position + new Vector3((origin.x + Mathf.Max(1, size.x) * .5f) * cellSize, 0, (origin.y + Mathf.Max(1, size.y) * .5f) * cellSize);
        // Origin whose footprint centre is nearest the world point (ghost snaps to the footprint centre).
        public Vector2Int FootprintOrigin(Vector3 world, Vector2Int size)
        {
            Vector3 p = world - transform.position;
            return new Vector2Int(Mathf.RoundToInt(p.x / cellSize - Mathf.Max(1, size.x) * .5f), Mathf.RoundToInt(p.z / cellSize - Mathf.Max(1, size.y) * .5f));
        }
        // Towers stand on wall blocks: every covered cell must be a wall top (validated by PlacementValidator.ValidateTower).
        public void CommitTower(IReadOnlyList<Vector2Int> footprint)
        {
            for (int i = 0; i < footprint.Count; i++)
                if (Get(footprint[i]) != CellState.Blocked || !InBounds(footprint[i])) throw new InvalidOperationException("Tower requires validated wall cells.");
            for (int i = 0; i < footprint.Count; i++) cells[footprint[i].x, footprint[i].y] = CellState.TowerSlot;
            Changed?.Invoke();
        }
        public void CommitTower(Vector2Int cell) => CommitTower(new[] { cell });
        // Selling/removal: the walls stay, the tower occupancy is freed on every covered cell.
        public void ReleaseTower(IReadOnlyList<Vector2Int> footprint)
        {
            for (int i = 0; i < footprint.Count; i++) if (Get(footprint[i]) == CellState.TowerSlot) cells[footprint[i].x, footprint[i].y] = CellState.Blocked;
            Changed?.Invoke();
        }

        private void OnDrawGizmos()
        {
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                Vector2Int p = new Vector2Int(x, y);
                var s = cells != null ? Get(p) : CellState.Empty;
                Gizmos.color = s == CellState.Spawn ? Color.cyan : s == CellState.Goal ? Color.yellow : s == CellState.Blocked ? Color.red : new Color(.3f, .6f, .6f, .3f);
                Gizmos.DrawWireCube(ToWorld(p), new Vector3(cellSize * .96f, .06f, cellSize * .96f));
            }
        }
    }
}
