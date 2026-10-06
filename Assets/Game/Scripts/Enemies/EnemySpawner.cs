using System;
using System.Collections;
using UnityEngine;

namespace StoneSignal
{
    public sealed class EnemySpawner : MonoBehaviour
    {
        private EnemyManager enemies;
        private Coroutine routine;
        public void Initialize(EnemyManager registry) { enemies = registry; }
        public void Begin(WaveData wave, float extraScale, Action completed)
        {
            Stop(); routine = StartCoroutine(Spawn(wave,extraScale,completed));
        }
        private IEnumerator Spawn(WaveData wave, float extraScale, Action completed)
        {
            var delay = new WaitForSeconds(wave.spawnInterval);
            foreach (EnemyGroup group in wave.groups)
                for (int i = 0; i < group.count; i++)
                {
                    enemies.Spawn(group.enemy,wave.hpScale * extraScale,wave.speedScale);
                    yield return delay;
                }
            routine = null; completed();
        }
        public void Stop() { if (routine != null) StopCoroutine(routine); routine = null; }
    }
}
