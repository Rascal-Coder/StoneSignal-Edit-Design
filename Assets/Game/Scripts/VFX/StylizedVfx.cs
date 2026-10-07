using UnityEngine;

namespace StoneSignal.VFX
{
    /// Fire-and-forget spawner for FX_* prefabs in Assets/Game/VFX/Stylized (they self-destroy via ParticleSystem stopAction).
    public static class StylizedVfx
    {
        public static GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null)
        {
            if (!prefab) return null;
            var go = Object.Instantiate(prefab, position, rotation ?? prefab.transform.rotation, parent);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) if (ps.transform == go.transform || ps.transform.parent == go.transform) { ps.Play(true); break; }
            return go;
        }
    }
}
