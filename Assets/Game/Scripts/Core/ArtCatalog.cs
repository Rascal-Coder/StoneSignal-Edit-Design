using UnityEngine;

namespace StoneSignal
{
    // Runtime meshes come from two CC0 packs: Kenney Tower Defense Kit (board, blocks,
    // towers) and Quaternius Animated Monsters (enemies). Prefabs keep an identity root
    // and carry FBX axis/scale/ground correction on child nodes, so gameplay transforms
    // never depend on model size. Metrics are written by the art import tool.
    [CreateAssetMenu(menuName = "StoneSignal/Art Catalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [Header("Board")]
        public GameObject tile;
        public GameObject tilePath;
        public GameObject tileSpawn;
        public GameObject tileGoal;
        [Header("Placeable")]
        public GameObject block;
        [Header("Landmarks")]
        public GameObject spawnPortal;
        public GameObject signalCore;
        [Header("Tower parts")]
        public GameObject towerBase, towerBodyA, towerBodyB, towerBodyC, towerCrystals;
        public GameObject weaponBallista, weaponTurret, weaponCannon;
        [Header("Decoration")]
        public GameObject detailTree, detailTreeLarge, detailRocks, detailCrystal, detailDirt;
        [Header("Scene")]
        public Material atlas;
        public Material backdrop;
        [Header("Board dressing (visual only)")]
        [Tooltip("Cliff/island mesh placed under the board, scaled from its native footprint to the grid.")]
        public GameObject boardCliff;
        public Vector2 boardCliffSize = new Vector2(16, 12);
        [Tooltip("Cliff pivot height relative to the grid plane (tile pivots rest on the cliff top).")]
        public float boardCliffOffsetY = -.55f;
        [Tooltip("Plank placed outward from each edge spawn.")]
        public GameObject entryBridge;
        [Min(0)] public int entryBridgePlanks = 4;
        public float entryBridgeOffsetY = -.55f;
        [Header("Art v8 (visual only)")]
        [Tooltip("Ground tile variants picked per cell by BoardArt.CellHash: index (h>>2) % length (art: A,A,B,C,D,E).")]
        public GameObject[] tileVariants;
        [Tooltip("Per-cell yaw (90 deg steps) and +-0.03..0.06 height undulation; core/wall cells stay flat.")]
        public bool tileUndulation = true;
        [Tooltip("Route flow segment (M_Path_Flow) replacing the dirt road; one per path edge, oriented along enemy travel.")]
        public GameObject pathFlowSegment;
        [Tooltip("Flow segment height over the grid plane (demo 0.82 - 0.55).")]
        public float pathFlowY = .27f;
        public GameObject placeGhostBlock, placeGhostTower;
        [Tooltip("Marks wall-top cells that can take a tower while a tower is selected (pooled).")]
        public GameObject slotHighlight;
        [Header("Combat VFX")]
        [Tooltip("Played where an enemy reaches the core.")]
        public GameObject coreHitVfx;
        [Header("Metrics")]
        public float tileTop = .2f;
        public float blockTop = .7f;
    }

    // Naming contract between the art import tool and the runtime enemy animator.
    // The tool copies the picked source clips to these stable names so the runtime never
    // depends on the original Blender action names shipped inside the FBX.
    public static class EnemyVisualContract
    {
        public const string MoveClip = "SS_Move";
        public const string DeathClip = "SS_Death";
        public const string LocomotionState = "Locomotion";
        public const string DeathState = "Death";
        public const string DieTrigger = "Die";
    }

    // Port of cell_hash() in ArtSource/Stylized/build_stylized_batch1.py. Blender cell (bx,by) = (7-gx, 5-gy) on the 16x12 board.
    public static class BoardArt
    {
        public static long CellHash(int x, int y)
        {
            long h = ((long)x * 73856093L) ^ ((long)y * 19349663L) ^ 0x5bd1e995L;
            h = ((h ^ (h >> 13)) * 0x27d4eb2dL) & 0xffffffffL;
            return h ^ (h >> 15);
        }
        public static long GameCellHash(Vector2Int cell, int width, int height) => CellHash(width / 2 - 1 - cell.x, height / 2 - 1 - cell.y);
        public static float HeightOffset(long h) { float mag = .03f + ((h >> 4) & 255) / 255f * .03f; return (h & 1) != 0 ? mag : -mag; }
        public static float Yaw(long h) => -((h >> 8) & 3) * 90f; // Blender +Z rotation -> Unity -Y
        public static int Variant(long h, int count) => count <= 0 ? -1 : (int)((h >> 2) % count);
    }

    public static class ArtVisual
    {
        public static GameObject Create(GameObject prefab, Transform parent, Vector3 position, float scale = 1)
        {
            if (prefab == null) return null;
            var obj = Object.Instantiate(prefab, parent);
            obj.name = prefab.name;
            obj.transform.position = position;
            obj.transform.localScale = Vector3.one * scale;
            return obj;
        }
        public static GameObject Wall(VisualPalette palette, Transform parent, Vector3 position, float cellSize)
        {
            if (palette.art != null && palette.art.block != null)
                return Create(palette.art.block, parent, position, cellSize);
            return PrimitiveVisual.Create("Wall", PrimitiveType.Cube, parent, position + Vector3.up * .3f, new Vector3(.9f, .6f, .9f) * cellSize, palette.wall);
        }
    }
}
