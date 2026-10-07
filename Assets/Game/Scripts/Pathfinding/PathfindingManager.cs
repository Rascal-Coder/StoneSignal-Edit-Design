using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class PathfindingManager : MonoBehaviour
    {
        public GridManager Grid { get; private set; }
        // CurrentPath = route from the first spawn (compatibility); CurrentPaths holds one route per spawn.
        public List<Vector2Int> CurrentPath { get; private set; } = new List<Vector2Int>();
        public List<List<Vector2Int>> CurrentPaths { get; private set; } = new List<List<Vector2Int>>();
        public event System.Action PathChanged;
        private static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.down, Vector2Int.left };
        public void Initialize(GridManager grid)
        {
            Grid = grid;
            Grid.Changed += Recalculate;
            Recalculate();
        }
        private void OnDestroy() { if (Grid != null) Grid.Changed -= Recalculate; }
        public void Recalculate()
        {
            CurrentPaths = new List<List<Vector2Int>>();
            foreach (var s in Grid.Spawns) CurrentPaths.Add(FindPath(s, Grid.goal));
            CurrentPath = CurrentPaths.Count > 0 ? CurrentPaths[0] : new List<Vector2Int>();
            PathChanged?.Invoke();
        }
        // Placement rule: every spawn must keep a route to the core.
        public bool AllSpawnsReachCore(HashSet<Vector2Int> extraBlocked = null)
        {
            foreach (var s in Grid.Spawns) if (FindPath(s, Grid.goal, extraBlocked).Count == 0) return false;
            return true;
        }

        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int target, HashSet<Vector2Int> extraBlocked = null)
        {
            var empty = new List<Vector2Int>();
            if (!Grid.Walkable(start) || !Grid.Walkable(target) || (extraBlocked != null && (extraBlocked.Contains(start) || extraBlocked.Contains(target)))) return empty;
            // Targeting any core cell reaches the whole (possibly multi-cell) core footprint.
            bool toCore = Grid.IsCore(target);
            var open = new List<Vector2Int> { start };
            var closed = new HashSet<Vector2Int>();
            var parents = new Dictionary<Vector2Int, Vector2Int>();
            var costs = new Dictionary<Vector2Int, int> { [start] = 0 };
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (costs[open[i]] + Heuristic(open[i], target, toCore) < costs[open[best]] + Heuristic(open[best], target, toCore)) best = i;
                Vector2Int current = open[best];
                open.RemoveAt(best);
                if (toCore ? Grid.IsCore(current) : current == target)
                {
                    var path = new List<Vector2Int> { current };
                    while (current != start) { current = parents[current]; path.Add(current); }
                    path.Reverse();
                    return path;
                }
                closed.Add(current);
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current + direction;
                    if (!Grid.Walkable(next) || closed.Contains(next) || (extraBlocked != null && extraBlocked.Contains(next))) continue;
                    int cost = costs[current] + 1;
                    if (costs.TryGetValue(next, out int previous) && previous <= cost) continue;
                    costs[next] = cost;
                    parents[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            return empty;
        }
        private int Heuristic(Vector2Int p, Vector2Int target, bool toCore)
        {
            if (!toCore) return Distance(p, target);
            int best = int.MaxValue;
            foreach (var c in Grid.CoreCells) best = Mathf.Min(best, Distance(p, c));
            return best;
        }
        private static int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
