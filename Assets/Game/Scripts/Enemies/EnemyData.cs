using UnityEngine;

namespace StoneSignal
{
    public enum EnemyKind { Normal, Fast, Tank, Splitter }
    [CreateAssetMenu(menuName = "StoneSignal/Enemy")]
    public sealed class EnemyData : ScriptableObject
    {
        public GameObject visualPrefab;
        public string displayName = "Drifter";
        [Min(1)] public float hp = 35;
        [Min(.1f)] public float moveSpeed = 1.4f;
        [Min(0)] public int reward = 12;
        [Min(1)] public int damageToBase = 2;
        public bool fast;
        public EnemyKind kind;
        public EnemyData splitChild;
        [Min(0)] public int splitCount;
    }
}
