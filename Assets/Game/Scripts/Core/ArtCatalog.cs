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
        public static void Wall(VisualPalette palette, Transform parent, Vector3 position, float cellSize)
        {
            if (palette.art != null && palette.art.block != null)
                Create(palette.art.block, parent, position, cellSize);
            else PrimitiveVisual.Create("Wall", PrimitiveType.Cube, parent, position + Vector3.up * .3f, new Vector3(.9f, .6f, .9f) * cellSize, palette.wall);
        }
    }
}
