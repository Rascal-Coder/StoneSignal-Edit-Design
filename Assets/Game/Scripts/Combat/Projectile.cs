using System;
using UnityEngine;

namespace StoneSignal
{
    public sealed class Projectile : MonoBehaviour
    {
        private Enemy target;
        private EnemyManager enemies;
        private float speed, damage, radius, lifetime, slowFraction, slowDuration;
        private Func<bool> canMove;
        private bool resolved;
        public static event Action<Vector3, float> Impact;
        public void Initialize(Enemy destination, EnemyManager registry, float moveSpeed, float hitDamage, float splash, Func<bool> allowed, float slow = 0, float duration = 0)
        {
            slowFraction=slow; slowDuration=duration; target = destination; enemies = registry; speed = moveSpeed; damage = hitDamage; radius = splash; canMove = allowed;
        }
        private void Update() { if (canMove != null && canMove()) Advance(Time.deltaTime); }
        public void Advance(float deltaTime)
        {
            if (resolved) return;
            if (target == null || !target.Alive) { Dispose(); return; }
            lifetime += deltaTime;
            Vector3 destination = target.transform.position;
            float distance = Vector3.Distance(transform.position, destination);
            if (distance <= speed * deltaTime + .1f)
            {
                target.ApplySlow(slowFraction,slowDuration);
                if (radius > 0) enemies.DamageArea(destination, radius, damage); else target.TakeDamage(damage);
                Impact?.Invoke(destination, radius);
                Dispose();
            }
            else if (lifetime > 6) Dispose();
            else transform.position = Vector3.MoveTowards(transform.position, destination, speed * deltaTime);
        }
        private void Dispose() { resolved = true; PrimitiveVisual.DestroyObject(gameObject); }
    }
}
