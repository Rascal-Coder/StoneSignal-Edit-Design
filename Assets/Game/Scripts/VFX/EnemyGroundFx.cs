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

}
