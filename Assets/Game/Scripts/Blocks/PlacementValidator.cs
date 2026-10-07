using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class PlacementValidator : MonoBehaviour
    {
        private GridManager grid;
        private PathfindingManager paths;
        public Func<HashSet<Vector2Int>, string> ValidateActors;
        public void Initialize(GridManager map, PathfindingManager pathfinding) { grid = map; paths = pathfinding; }
        public string ValidatePlacement(IReadOnlyList<Vector2Int> cells)
        {
            var simulation = new HashSet<Vector2Int>();
            foreach (Vector2Int cell in cells)
            {
                if (!grid.InBounds(cell)) return "Outside the board";
                if (!grid.CanPlace(cell)) return "Cell occupied / protected";
                if (!simulation.Add(cell)) return "Duplicate block cell";
            }
            if (!paths.AllSpawnsReachCore(simulation)) return "Blocked: the core needs an open route";
            return ValidateActors?.Invoke(simulation);
        }
        public string ValidateTower(Vector2Int cell) => ValidateTower(cell, Vector2Int.one);
        // Every covered cell must be in bounds, a wall block top, and not already carrying a tower.
        // Walls are already impassable, so the route check only guards against stale state.
        public string ValidateTower(Vector2Int origin, Vector2Int size)
        {
            var cells = grid.Footprint(origin, size);
            foreach (var c in cells)
            {
                if (!grid.InBounds(c)) return "Outside the board";
                var s = grid.Get(c);
                if (s == CellState.TowerSlot) return "Occupied by a tower";
                if (s != CellState.Blocked) return size.x * size.y > 1 ? "Tower needs wall blocks under all " + size.x + "x" + size.y + " cells" : "Towers must be built on a wall block";
            }
            if (paths != null && !paths.AllSpawnsReachCore()) return "Blocked: the core needs an open route";
            return null;
        }
    }
}
