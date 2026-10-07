using UnityEngine;

namespace StoneSignal
{
    [CreateAssetMenu(menuName = "StoneSignal/Block Shape")]
    public sealed class BlockShapeData : ScriptableObject
    {
        public Sprite icon;
        public string displayName;
        public CellState placedState = CellState.Blocked;
        public Vector2Int[] cells;

        /// Placement cells for a direction (0 up, 1 right, 2 down, 3 left): the authored shape (pointing up) turned clockwise
        /// around its own (0,0) cell, so the aimed / anchor cell is always covered and the piece extends toward the picked arrow.
        public Vector2Int[] TurnedAround(int dir)
        {
            var r = (Vector2Int[])cells.Clone(); int n = (dir % 4 + 4) % 4;
            for (int t = 0; t < n; t++) for (int i = 0; i < r.Length; i++) r[i] = new Vector2Int(r[i].y, -r[i].x);
            return r;
        }
        // Return a fresh normalized shape; rotation never changes asset data.
        public Vector2Int[] Rotated(int quarterTurns)
        {
            Vector2Int[] result = (Vector2Int[])cells.Clone();
            for (int turn = 0; turn < ((quarterTurns % 4 + 4) % 4); turn++)
                for (int i = 0; i < result.Length; i++) result[i] = new Vector2Int(-result[i].y, result[i].x);
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (Vector2Int p in result) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); }
            for (int i = 0; i < result.Length; i++) result[i] -= new Vector2Int(minX, minY);
            return result;
        }
    }
}
