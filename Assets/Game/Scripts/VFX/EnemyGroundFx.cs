using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Per-enemy: blob contact shadow (shared instanced material, child quad) + reports position/radius to the global dust system.
    /// No particle system per enemy. v17: flyers get a smaller, softer shadow (flyingBlobMaterial, shared) and no footprints,
    /// and SetFlying(true) makes sure the sibling visual has a FlyingMotion (height/bob/bank).
    /// v17.2: exactly ONE EnemyGroundFx per enemy - the runtime one claims the enemy (ClaimEnemy) and disables any copy baked into the
    /// visual prefab (that copy rode the flyer's lifted model: 2nd shadow at ~2 m and a 2nd FlyingMotion = double lift ~1.8 m).
    /// Footprints: alternating L/R prints aligned to the heading, sampled per step at the walker's actual feet height
    /// (visual root y; optional downward raycast if the scene has ground colliders).
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
        [Tooltip("v17.2: optional ground colliders for per-step height (0 = none; art has no colliders, the walker's feet height is used)")] public LayerMask groundMask = 0;
        [Tooltip("v17.2: footprint lift above the sampled ground (planks/tiles)")] public float footprintLift = .045f;

        Vector3 last; bool hasLast; Material groundMat; Renderer blobR; int side; Transform visual;
        void OnEnable() { hasLast = false; EnemyGroundFxSystem.Register(this); Apply(); }
        void OnDisable() { EnemyGroundFxSystem.Unregister(this); }
        public void SetRadius(float r) { radius = r; Apply(); }
        public void SetFlying(bool f) { flying = f; Apply(); if (f) EnsureFlyingMotion(); }

        /// v17.2: make `owner` the only active EnemyGroundFx under enemyRoot (disables prefab-baked copies, removes stray FlyingMotions
        /// they added to inner model children). Call once after creating the runtime ground fx.
        public static void ClaimEnemy(Transform enemyRoot, EnemyGroundFx owner)
        {
            if (!enemyRoot || !owner) return;
            foreach (var g in enemyRoot.GetComponentsInChildren<EnemyGroundFx>(true))
            {
                if (g == owner) continue;
                if (g.blob) g.blob.gameObject.SetActive(false);
                g.enabled = false; g.gameObject.SetActive(false);
            }
            foreach (var fm in enemyRoot.GetComponentsInChildren<FlyingMotion>(true))
                if (fm.transform.parent != enemyRoot) { fm.enabled = false; Destroy(fm); }   // only a direct child (the visual root) may lift
        }

        void Apply()
        {
            if (!blob) return;
            blob.localScale = Vector3.one * radius * 2 * (flying ? flyingBlobScale : 1);
            if (!blobR) { blobR = blob.GetComponent<Renderer>(); if (blobR) groundMat = blobR.sharedMaterial; }
            if (blobR && flyingBlobMaterial) blobR.sharedMaterial = flying ? flyingBlobMaterial : groundMat;
        }
        Transform Visual()
        {
            if (visual) return visual;
            var root = transform.parent; if (!root) return null;
            foreach (Transform c in root)
            {
                if (c == transform || c.GetComponentInChildren<EnemyGroundFx>(true) == this) continue;
                if (!c.GetComponentInChildren<Renderer>(true)) continue;
                return visual = c;
            }
            return null;
        }
        void EnsureFlyingMotion()
        {
            var v = Visual(); if (!v || v.GetComponent<FlyingMotion>()) return;
            if (v.parent && v.parent.GetComponentInParent<FlyingMotion>()) return;   // already inside a lifted visual: never stack
            v.gameObject.AddComponent<FlyingMotion>();
        }
        void LateUpdate()
        {
            if (blob) { blob.position = new Vector3(transform.position.x, transform.position.y + groundY + lift, transform.position.z); blob.rotation = Quaternion.Euler(90, 0, 0); }
            if (flying) return;   // flyers: shadow only, never footprints
            var p = transform.position;
            if (!hasLast) { last = p; hasLast = true; return; }
            var d = p - last; d.y = 0;
            if (d.sqrMagnitude >= stepDistance * stepDistance)
            {
                // per-step ground sample: walker feet = visual root (stands on tiles / bridge planks); colliders refine it when present
                var v = Visual(); float gy = v ? v.position.y : p.y + groundY;
                if (groundMask.value != 0 && Physics.Raycast(new Vector3(p.x, gy + .6f, p.z), Vector3.down, out var hit, 1.4f, groundMask, QueryTriggerInteraction.Ignore)) gy = hit.point.y;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                var right = new Vector3(d.z, 0, -d.x).normalized; side = 1 - side;
                var at = new Vector3(p.x, gy + footprintLift, p.z) + right * ((side == 0 ? -1 : 1) * radius * .32f);
                EnemyGroundFxSystem.Footprint(at, radius, yaw);
                last = p;
            }
        }
    }

}
