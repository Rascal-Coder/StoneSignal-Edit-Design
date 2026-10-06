using UnityEngine;

namespace StoneSignal
{
    [System.Serializable]
    public sealed class EnemyGroup
    {
        public EnemyData enemy;
        [Min(1)] public int count = 5;
    }
    [CreateAssetMenu(menuName = "StoneSignal/Wave")]
    public sealed class WaveData : ScriptableObject
    {
        public EnemyGroup[] groups;
        [Min(.05f)] public float spawnInterval = .7f;
        [Min(.1f)] public float hpScale = 1;
        [Min(.1f)] public float speedScale = 1;
        public int Total
        {
            get { int count = 0; foreach (var group in groups) count += group.count; return count; }
        }
    }
}
