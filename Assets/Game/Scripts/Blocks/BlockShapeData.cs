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
