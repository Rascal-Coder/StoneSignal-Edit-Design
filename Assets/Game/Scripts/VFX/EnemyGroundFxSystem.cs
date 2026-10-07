using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Global pooled footprint emitter (one ParticleSystem, ParticleSystem.Emit, hard cap 64). Lives in PF_VFX_EnemyGroundSystem.
    /// v17.2: painted paw prints (T_FX_Footprint, M_VFX_Footprint), dark warm brown, alpha ~0.55, 1.5 s life (hold then fade), larger,
    /// rotated to the walk heading (horizontal billboard).
    /// v17.3: darker + more opaque (#3A2414 via M_VFX_Footprint _BaseColor, alpha 0.7), slightly larger, SS_GroundPrint shader
    /// (queue 2995 after opaque, ZTest LEqual, small depth offset); heights resolved by EnemyGroundFx from StoneSignal.WalkSurface.
    /// v17.4: colour + opacity come ONLY from M_VFX_Footprint _BaseColor, written by StylizedFxV14.FootprintColor (#24160C, a 0.62);
    /// the particle start colour is white. Change the value in the builder (BatchImport overwrites the material).
    public class EnemyGroundFxSystem : MonoBehaviour
    {
        public static EnemyGroundFxSystem Instance { get; private set; }
        public ParticleSystem dust;
        [Tooltip("v17.2: added to the heading yaw (set 180 if prints point backwards)")] public float footprintYawOffset = 0f;
        [Tooltip("v17.2: set -1 if prints turn the wrong way on diagonal paths (billboard rotation handedness)")] public float footprintYawSign = 1f;
        [Tooltip("v17.2: print size range by enemy radius (0.2..1)")] public Vector2 footprintSize = new Vector2(.40f, .64f);   // v17.3 (was .34/.56)
        public const int MaxFootprints = 64;
        static readonly HashSet<EnemyGroundFx> active = new HashSet<EnemyGroundFx>();
        public static int ActiveCount => active.Count;

        void Awake() { Instance = this; if (dust) { var m = dust.main; m.maxParticles = MaxFootprints; } }
        void OnDestroy() { if (Instance == this) Instance = null; }
        public static void Register(EnemyGroundFx e) => active.Add(e);
        public static void Unregister(EnemyGroundFx e) => active.Remove(e);
        /// Emit one footprint (random rotation). Silently dropped when no system exists or the cap is reached.
        public static void Footprint(Vector3 pos, float radius) => Footprint(pos, radius, Random.Range(0, 360f));
        /// v17.2: footprint aligned to the walk heading (yaw degrees, world).
        public static void Footprint(Vector3 pos, float radius, float yaw)
        {
            var s = Instance; if (s == null || s.dust == null || s.dust.particleCount >= MaxFootprints) return;
            float size = Mathf.Lerp(s.footprintSize.x, s.footprintSize.y, Mathf.InverseLerp(.2f, 1f, radius));
            s.dust.Emit(new ParticleSystem.EmitParams { position = pos, startSize = size, rotation = yaw * s.footprintYawSign + s.footprintYawOffset, applyShapeToPosition = false }, 1);
        }
        public void Clear() { if (dust) dust.Clear(); }
    }
}
