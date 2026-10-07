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
        public string ValidateTower(Vector2Int cell)
        {
            // A wall can become a tower foundation; both remain impassable.
            if (grid.InBounds(cell) && grid.Get(cell) == CellState.Blocked) return null;
            return ValidatePlacement(new[] { cell });
        }
    }
}
