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
        public Vector2Int spawn = new Vector2Int(0, 4);
        public Vector2Int goal = new Vector2Int(15, 4);
        private CellState[,] cells;
        public event Action Changed;

        public void Initialize()
        {
            cells = new CellState[width, height];
            spawn = new Vector2Int(0, height / 2 - 1);
            goal = new Vector2Int(width - 1, height / 2 - 1);
            cells[spawn.x, spawn.y] = CellState.Spawn;
            cells[goal.x, goal.y] = CellState.Goal;
        }

        public bool InBounds(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;
        public CellState Get(Vector2Int p) => InBounds(p) && cells != null ? cells[p.x, p.y] : CellState.Blocked;
        public bool CanPlace(Vector2Int p) => InBounds(p) && Get(p) == CellState.Empty;
        public bool Walkable(Vector2Int p) => InBounds(p) && Get(p) != CellState.Blocked && Get(p) != CellState.TowerSlot;
        public Vector3 ToWorld(Vector2Int p) => transform.position + new Vector3((p.x + .5f) * cellSize, 0, (p.y + .5f) * cellSize);
        public Vector2Int ToCell(Vector3 world)
        {
            Vector3 p = world - transform.position;
            return new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
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
                Gizmos.color = p == spawn ? Color.cyan : p == goal ? Color.yellow : cells != null && Get(p) == CellState.Blocked ? Color.red : new Color(.3f, .6f, .6f, .3f);
                Gizmos.DrawWireCube(ToWorld(p), new Vector3(cellSize * .96f, .06f, cellSize * .96f));
            }
        }
    }
}
