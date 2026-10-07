using UnityEngine;

namespace StoneSignal
{
    // Board shape for the real game scene. Gameplay heights are fixed (ground 0.25, wall top 0.6);
    // visual tile undulation never feeds back into logic.
    // One visual-only prop around the board. Position is relative to the board centre with the ground plane at 0
    // (tile pivots); converted from ArtSource/Stylized/level_layout.json by the wiring tool.
    [System.Serializable]
    public struct BoardSurroundItem
    {
        public GameObject prefab;
        public Vector3 position;
        public Vector3 euler;
        public Vector3 scale;
        [Tooltip("Optional material override (foliage tint variants).")]
        public Material material;
    }
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
        [Header("Level dressing (visual only)")]
        [Tooltip("Art-authored environment prefab (water, shore islands, trees) placed at the board centre. Assigned by the wiring tool when PF_Env_LevelDressing_16x12 exists; empty = cliff + bridges only.")]
        public GameObject levelDressing;
        [Tooltip("Distance (cells) from an entry cell centre, outward, to the centre of its shore island (level_layout.json: 5.1).")]
        public float entryIslandDistance = 5.1f;
        [Tooltip("Island top surface height relative to the board walk plane.")]
        public float entryIslandHeight = 0f;
        [Tooltip("Dressing pivot relative to the board centre / ground plane (demo tile top 0.80 -> game 0.25 = -0.55).")]
        public Vector3 levelDressingOffset = new Vector3(0, -.55f, 0);
        [Tooltip("The dressing ships its own board cliff, so the ArtCatalog cliff is skipped.")]
        public bool levelDressingReplacesCliff = true;
        [Header("Surroundings (visual only)")]
        public BoardSurroundItem[] surroundings;
        public Material waterMaterial;
        [Tooltip("Water plane height relative to the ground plane (demo water at y 0 = 0.55 below tile pivots).")]
        public float waterY = -.55f;
        [Min(1)] public float waterSize = 120;
        [TextArea] public string notes;
    }
}
