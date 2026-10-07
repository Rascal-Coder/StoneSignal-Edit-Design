using UnityEngine;
namespace StoneSignal.VFX
{
    /// v16.2: tiny water ripple at a world point (e.g. spawn islands / enemy spawn). 4 global slots, pure shader (SS_Water), 0 extra DC.
    public static class SpawnRipple
    {
        static readonly int Id = Shader.PropertyToID("_SS_SpawnRipples");
        static readonly Vector4[] slots = { new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1) };
        static int next;
        public static void Play(Vector3 worldPos)
        {
            slots[next] = new Vector4(worldPos.x, worldPos.y, worldPos.z, Time.timeSinceLevelLoad); next = (next + 1) % slots.Length;
            Shader.SetGlobalVectorArray(Id, slots);
        }
        [RuntimeInitializeOnLoadMethod] static void Reset() { for (int i = 0; i < 4; i++) slots[i].w = -1; Shader.SetGlobalVectorArray(Id, slots); }
    }
}
