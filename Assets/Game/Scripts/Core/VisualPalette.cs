using UnityEngine;

namespace StoneSignal
{
    [CreateAssetMenu(menuName = "StoneSignal/Visual Palette")]
    public sealed class VisualPalette : ScriptableObject
    {
        public ArtCatalog art;
        public Material tileA, tileB, wall, spawn, goal, valid, invalid, path, projectile, enemy, fastEnemy, towerBase, arrow, rapid, cannon, particle;
    }
    public static class PrimitiveVisual
    {
        public static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj);
        }
        public static GameObject Create(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = obj.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            return obj;
        }
    }
}
