using System;
using UnityEngine;

namespace StoneSignal
{
    public sealed class WaveManager : MonoBehaviour
    {
        private GameManager game;
        private GameConfig config;
        private EnemyManager enemies;
        private EnemySpawner spawner;
        private RunModifiers modifiers;
        private bool allSpawned;
        public int WaveIndex { get; private set; }
        /// Enemies of this wave still alive or not yet spawned (split children included: each split adds its children).
        public int Remaining { get; private set; }
        /// v18.6: this wave's enemy total - wave.Total plus every split child spawned so far (grows with Remaining on a split).
        public int Total { get; private set; }
        public event Action Changed;
        public event Action Completed;
        public void Initialize(GameManager state, GameConfig settings, EnemyManager registry, EnemySpawner source, RunModifiers upgrades)
        {
            game = state; config = settings; enemies = registry; spawner = source; modifiers = upgrades;
            enemies.Resolved += OnResolved; enemies.ChildrenAdded += OnChildren; game.StateChanged += OnStateChanged;
        }
        public bool StartWave()
        {
            if (game.State != GameState.Build) return false;
            WaveData wave = config.waves[Mathf.Min(WaveIndex,config.waves.Length-1)];
            Remaining = Total = wave.Total; allSpawned = false; modifiers.BeginWave(); game.SetState(GameState.Combat); Changed?.Invoke();
            float extraScale = 1 + Mathf.Max(0,WaveIndex-config.waves.Length+1)*.2f;
            spawner.Begin(wave,extraScale,() => { allSpawned = true; TryComplete(); });
            return true;
        }
        private void Update() { if (Input.GetKeyDown(KeyCode.Space)) StartWave(); }
        private void OnChildren(int count) { if(game.State==GameState.Combat) { Remaining+=count; Total+=count; Changed?.Invoke(); } }
        private void OnResolved(Enemy enemy, EnemyResolution reason)
        {
            if (game.State != GameState.Combat || Remaining <= 0) return;
            Remaining--; Changed?.Invoke(); TryComplete();
        }
        private void TryComplete()
        {
            if (game.State != GameState.Combat || !allSpawned || Remaining != 0) return;
            game.SetState(GameState.Reward); Completed?.Invoke();
        }
        public void PrepareNext()
        {
            if (game.State != GameState.Reward) return;
            WaveIndex++; game.SetState(GameState.Build); Changed?.Invoke();
        }
        private void OnStateChanged(GameState state) { if (state == GameState.GameOver) spawner.Stop(); }
        private void OnDestroy()
        {
            if (enemies != null) { enemies.Resolved -= OnResolved; enemies.ChildrenAdded -= OnChildren; }
            if (game != null) game.StateChanged -= OnStateChanged;
        }
    }
}
