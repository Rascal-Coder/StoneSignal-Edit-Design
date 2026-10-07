using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Global pooled dust emitter (one ParticleSystem, ParticleSystem.Emit, hard cap 64 footprints, 2 s fade). Lives in PF_VFX_EnemyGroundSystem.
    public class EnemyGroundFxSystem : MonoBehaviour
    {
        public static EnemyGroundFxSystem Instance { get; private set; }
        public ParticleSystem dust;
        public const int MaxFootprints = 64;
        static readonly HashSet<EnemyGroundFx> active = new HashSet<EnemyGroundFx>();
        public static int ActiveCount => active.Count;

        void Awake() { Instance = this; if (dust) { var m = dust.main; m.maxParticles = MaxFootprints; } }
        void OnDestroy() { if (Instance == this) Instance = null; }
        public static void Register(EnemyGroundFx e) => active.Add(e);
        public static void Unregister(EnemyGroundFx e) => active.Remove(e);
        /// Emit one footprint/dust puff. Silently dropped when no system exists or the cap is reached.
        public static void Footprint(Vector3 pos, float radius)
        {
            var s = Instance; if (s == null || s.dust == null || s.dust.particleCount >= MaxFootprints) return;
            s.dust.Emit(new ParticleSystem.EmitParams { position = pos, startSize = Mathf.Lerp(.16f, .3f, Mathf.InverseLerp(.2f, 1f, radius)), rotation = Random.Range(0, 360f), applyShapeToPosition = false }, 1);
        }
        public void Clear() { if (dust) dust.Clear(); }
    }
}
