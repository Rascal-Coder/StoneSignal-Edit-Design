using System;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    public sealed class Projectile : MonoBehaviour
    {
        private Enemy target;
        private EnemyManager enemies;
        private float speed, damage, radius, lifetime, slowFraction, slowDuration;
        private Func<bool> canMove;
        private bool resolved;
        // Presentation only; never changes hit timing or damage.
        private TowerData source;
        private bool crit;
        private float lobHeight, travelled;
        private Vector3 groundPosition;
        public static event Action<Vector3, float> Impact;
        // Pool (play mode): projectiles are reused instead of Instantiate/Destroy per shot.
        private static readonly System.Collections.Generic.Stack<Projectile> pool = new System.Collections.Generic.Stack<Projectile>();
        private bool pooled;
        private GameObject body;
        public static Projectile Get(string name, Transform parent, Vector3 position)
        {
            Projectile p = null;
            while (pool.Count > 0 && p == null) p = pool.Pop();
            if (p == null) { p = new GameObject(name).AddComponent<Projectile>(); p.pooled = Application.isPlaying; }
            p.transform.SetParent(parent, false); p.transform.position = position; p.transform.rotation = Quaternion.identity;
            p.gameObject.SetActive(true);
            p.resolved = false; p.lifetime = 0; p.travelled = 0; p.source = null; p.Owner = null; p.crit = false; p.lobHeight = 0; p.body = null;
            return p;
        }
        /// Firing tower (rune bounty attribution); null for non-tower shots.
        public Tower Owner { get; set; }
        public void AttachBody(GameObject vfx) { body = vfx; }
        public void Initialize(Enemy destination, EnemyManager registry, float moveSpeed, float hitDamage, float splash, Func<bool> allowed, float slow = 0, float duration = 0)
        {
            slowFraction=slow; slowDuration=duration; target = destination; enemies = registry; speed = moveSpeed; damage = hitDamage; radius = splash; canMove = allowed;
            groundPosition = transform.position;
        }
        public void SetPresentation(TowerData data, bool critical, float arcHeight)
        {
            source = data; crit = critical; lobHeight = arcHeight;
        }
        private void Update() { if (canMove != null && canMove()) Advance(Time.deltaTime); }
        public void Advance(float deltaTime)
        {
            if (resolved) return;
            if (target == null || !target.Alive) { Dispose(); return; }
            lifetime += deltaTime;
            Vector3 destination = target.transform.position;
            float distance = Vector3.Distance(groundPosition, destination);
            if (distance <= speed * deltaTime + .1f)
            {
                var kind = source != null ? source.damageKind : DamageKind.Physical;
                target.ApplySlow(slowFraction,slowDuration); target.LastAttacker = Owner;
                if (radius > 0) enemies.DamageArea(destination, radius, damage, kind, crit); else target.TakeDamage(damage, kind, crit);
                Impact?.Invoke(destination, radius);
                PlayImpact(destination, !target.Alive);
                Dispose();
            }
            else if (lifetime > 6) Dispose();
            else
            {
                groundPosition = Vector3.MoveTowards(groundPosition, destination, speed * deltaTime);
                travelled += speed * deltaTime;
                float lift = 0;
                if (lobHeight > 0) { float k = travelled / Mathf.Max(.01f, travelled + distance); lift = 4 * lobHeight * k * (1 - k); }
                Vector3 next = groundPosition + Vector3.up * lift;
                Vector3 step = next - transform.position;
                transform.position = next;
                if (step.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(step);
            }
        }
        private void PlayImpact(Vector3 point, bool killed)
        {
            if (source == null) return;
            Vector3 at = point + Vector3.up * .3f;
            bool big = source.explosionVfx != null && (source.explosionOnEveryHit || (crit && source.explosionOnCrit) || (killed && source.explosionOnKill));
            if (big) StylizedVfx.Play(source.explosionVfx, point);
            else if (source.hitVfx != null) StylizedVfx.Play(source.hitVfx, at);
            if (big || crit)
            {
                if (source.impactShake != FeedbackShake.None) CameraShake.Shake((CameraShake.Preset)((int)source.impactShake - 1));
                if (source.impactHitStop > 0) HitStop.Trigger(source.impactHitStop, .05f);
            }
        }
        private void Dispose()
        {
            resolved = true;
            if (body != null) { StylizedVfx.Release(body); body = null; }
            if (!pooled) { PrimitiveVisual.DestroyObject(gameObject); return; }
            target = null; gameObject.SetActive(false); pool.Push(this);
        }
    }
}
