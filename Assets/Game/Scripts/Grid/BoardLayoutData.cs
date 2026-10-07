using UnityEngine;

namespace StoneSignal
{
    // Board shape for the real game scene. Gameplay heights are fixed (ground 0.25, wall top 0.6);
    // visual tile undulation never feeds back into logic.
    [CreateAssetMenu(menuName = "StoneSignal/Board Layout")]
    public sealed class BoardLayoutData : ScriptableObject
    {
        [Min(4)] public int width = 16;
        [Min(4)] public int height = 12;
        [Min(.1f)] public float cellSize = 1;
        [Tooltip("Enemy entry cells, normally on the board edge. Wave groups pick one by index or rotate.")]
        public Vector2Int[] spawns = { new Vector2Int(15, 3), new Vector2Int(0, 8), new Vector2Int(7, 0) };
        [Tooltip("Lower-left cell of the core footprint.")]
        public Vector2Int coreOrigin = new Vector2Int(7, 5);
        public Vector2Int coreSize = new Vector2Int(2, 2);
        [TextArea] public string notes;
    }
}
