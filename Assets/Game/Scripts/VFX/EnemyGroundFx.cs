using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Per-enemy: blob contact shadow (shared instanced material, child quad) + reports position/radius to the global dust system.
    /// No particle system per enemy. v17: flyers get a smaller, softer shadow (flyingBlobMaterial, shared) and no footprints,
    /// and SetFlying(true) makes sure the sibling visual has a FlyingMotion (height/bob/bank).
    public class EnemyGroundFx : MonoBehaviour
    {
        public Transform blob;
        public float radius = .45f;
        [Tooltip("Ground height relative to the enemy root (root sits on tile top 0.80).")] public float groundY = 0f;
        [Tooltip("Lift above groundY to clear tile-top bumps (tile jitter max 0.06).")] public float lift = .07f;
        public bool flying;
        [Tooltip("Metres walked between footprints")] public float stepDistance = .45f;
        [Tooltip("v17: shadow scale for flyers (relative to radius*2)")] public float flyingBlobScale = .55f;
        [Tooltip("v17: optional softer/lighter shared blob material for flyers (null = keep the ground one)")] public Material flyingBlobMaterial;

        Vector3 last; bool hasLast; Material groundMat; Renderer blobR;
        void OnEnable() { hasLast = false; EnemyGroundFxSystem.Register(this); Apply(); }
        void OnDisable() { EnemyGroundFxSystem.Unregister(this); }
        public void SetRadius(float r) { radius = r; Apply(); }
        public void SetFlying(bool f) { flying = f; Apply(); if (f) EnsureFlyingMotion(); }
        void Apply()
        {
            if (!blob) return;
            blob.localScale = Vector3.one * radius * 2 * (flying ? flyingBlobScale : 1);
            if (!blobR) { blobR = blob.GetComponent<Renderer>(); if (blobR) groundMat = blobR.sharedMaterial; }
            if (blobR && flyingBlobMaterial) blobR.sharedMaterial = flying ? flyingBlobMaterial : groundMat;
        }
        void EnsureFlyingMotion()
        {
            var root = transform.parent; if (!root) return;
            foreach (Transform c in root)
            {
                if (c == transform || c.GetComponentInChildren<EnemyGroundFx>(true) == this) continue;
                if (!c.GetComponentInChildren<Renderer>(true)) continue;
                if (!c.GetComponent<FlyingMotion>()) c.gameObject.AddComponent<FlyingMotion>();
                return;
            }
        }
        void LateUpdate()
        {
            if (blob) { blob.position = new Vector3(transform.position.x, transform.position.y + groundY + lift, transform.position.z); blob.rotation = Quaternion.Euler(90, 0, 0); }
            if (flying) return;   // flyers: shadow only, never footprints
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
