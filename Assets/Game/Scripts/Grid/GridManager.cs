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
            if (layout != null) { width = layout.width; height = layout.height; cellSize = layout.cellSize; }
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

        public void CommitTower(Vector2Int cell)
        {
            if (!InBounds(cell) || (Get(cell) != CellState.Empty && Get(cell) != CellState.Blocked))
                throw new InvalidOperationException("Tower requires a validated empty cell or wall.");
            cells[cell.x, cell.y] = CellState.TowerSlot;
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
