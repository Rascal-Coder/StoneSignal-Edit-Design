using UnityEngine;

namespace StoneSignal
{
    [CreateAssetMenu(menuName = "StoneSignal/Art Catalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        public GameObject tileA, tileB, wall, cliff, foliage, ruinPillar, brazier, spawnPortal, signalCore, boardBase, adventurer;
        public Material backdrop;
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
            if (palette.art != null && palette.art.wall != null)
                Create(palette.art.wall, parent, position, cellSize);
            else PrimitiveVisual.Create("Wall", PrimitiveType.Cube, parent, position + Vector3.up * .3f, new Vector3(.9f, .6f, .9f) * cellSize, palette.wall);
        }
    }
}
