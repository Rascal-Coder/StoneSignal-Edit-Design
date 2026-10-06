using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class PathfindingManager : MonoBehaviour
    {
        public GridManager Grid { get; private set; }
        public List<Vector2Int> CurrentPath { get; private set; } = new List<Vector2Int>();
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
            CurrentPath = FindPath(Grid.spawn, Grid.goal);
            PathChanged?.Invoke();
        }

        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int target, HashSet<Vector2Int> extraBlocked = null)
        {
            var empty = new List<Vector2Int>();
            if (!Grid.Walkable(start) || !Grid.Walkable(target) || (extraBlocked != null && (extraBlocked.Contains(start) || extraBlocked.Contains(target)))) return empty;
            var open = new List<Vector2Int> { start };
            var closed = new HashSet<Vector2Int>();
            var parents = new Dictionary<Vector2Int, Vector2Int>();
            var costs = new Dictionary<Vector2Int, int> { [start] = 0 };
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (costs[open[i]] + Distance(open[i], target) < costs[open[best]] + Distance(open[best], target)) best = i;
                Vector2Int current = open[best];
                open.RemoveAt(best);
                if (current == target)
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
        private static int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
