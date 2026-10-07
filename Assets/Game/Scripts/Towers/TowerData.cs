using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    public enum TowerKind { Arrow, Rapid, Cannon, Chill }
    public enum FeedbackShake { None, Light, Medium, Heavy, Boss }
    [CreateAssetMenu(menuName = "StoneSignal/Tower")]
    public sealed class TowerData : ScriptableObject
    {
        public GameObject visualPrefab;
        public Sprite icon;
        public string displayName;
        [TextArea] public string role;
        [Range(0,1)] public float slowFraction;
        [Min(0)] public float slowDuration;
        public TowerKind kind;
        [Min(1)] public int cost = 50;
        [Min(.1f)] public float range = 4;
        [Min(.01f)] public float attacksPerSecond = 1;
        [Min(.1f)] public float damage = 12;
        [Min(.1f)] public float projectileSpeed = 12;
        [Min(0)] public float splashRadius;
        [Header("Critical hits")]
        [Range(0,1)] public float critChance;
        [Min(1)] public float critMultiplier = 1.5f;
        [Header("Presentation (visual only)")]
        [Tooltip("Rotatable child; empty = first child whose name ends with _Head. None found = no head rotation (Tesla).")]
        public string headName;
        [Tooltip("Muzzle offset from the tower's standing surface, along the aim direction.")]
        public float muzzleHeight = .65f;
        public float muzzleForward = .45f;
        [Tooltip("Mortar-style shot: muzzle at the barrel tip aimed upward, projectile follows an arc.")]
        public bool lobbedShot;
        [Min(0)] public float lobHeight = 1.5f;
        public DamageKind damageKind = DamageKind.Physical;
        public GameObject muzzleVfx;
        [Tooltip("Travelling projectile prefab. A prefab with TeslaArc draws an instant arc instead.")]
        public GameObject projectileVfx;
        public GameObject hitVfx;
        public GameObject explosionVfx;
        public bool explosionOnEveryHit, explosionOnCrit, explosionOnKill;
        [Tooltip("Applied only when the explosion plays or on a crit, never on every shot.")]
        public FeedbackShake impactShake;
        [Min(0)] public float impactHitStop;
    }
}
