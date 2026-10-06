using UnityEngine;

namespace StoneSignal
{
    public enum TowerKind { Arrow, Rapid, Cannon, Chill }
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
    }
}
