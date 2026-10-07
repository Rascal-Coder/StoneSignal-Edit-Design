using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Per-enemy: blob contact shadow (shared instanced material, child quad) + reports position/radius to the global dust system.
    /// No particle system per enemy.
    public class EnemyGroundFx : MonoBehaviour
    {
        public Transform blob;
        public float radius = .45f;
        [Tooltip("Ground height relative to the enemy root (root sits on tile top 0.80).")] public float groundY = 0f;
        [Tooltip("Lift above groundY to clear tile-top bumps (tile jitter max 0.06).")] public float lift = .07f;
        public bool flying;
        [Tooltip("Metres walked between footprints")] public float stepDistance = .45f;

        Vector3 last; bool hasLast;
        void OnEnable() { hasLast = false; EnemyGroundFxSystem.Register(this); Apply(); }
        void OnDisable() { EnemyGroundFxSystem.Unregister(this); }
        public void SetRadius(float r) { radius = r; Apply(); }
        public void SetFlying(bool f) { flying = f; Apply(); }
        void Apply()
        {
            if (!blob) return;
            blob.localScale = Vector3.one * radius * 2 * (flying ? .7f : 1);
        }
        void LateUpdate()
        {
            if (blob) { blob.position = new Vector3(transform.position.x, transform.position.y + groundY + lift, transform.position.z); blob.rotation = Quaternion.Euler(90, 0, 0); }
            if (flying) return;
            var p = transform.position;
            if (!hasLast) { last = p; hasLast = true; return; }
            if ((p - last).sqrMagnitude >= stepDistance * stepDistance)
            {
                EnemyGroundFxSystem.Footprint(new Vector3(p.x, p.y + groundY + lift - .02f, p.z), radius);
                last = p;
            }
        }
    }

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
