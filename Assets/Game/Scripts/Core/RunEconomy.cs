using System;
using UnityEngine;

namespace StoneSignal
{
    public sealed class RunEconomy : MonoBehaviour
    {
        private EnemyManager enemies;
        private RunModifiers modifiers;
        public int Gold { get; private set; }
        public int HP { get; private set; }
        public int MaxHP { get; private set; }
        public event Action Changed;
        public void Initialize(GameConfig config, EnemyManager registry, RunModifiers upgrades)
        {
            Gold = config.initialGold; HP = MaxHP = config.baseHP;
            enemies = registry; modifiers = upgrades; enemies.Resolved += OnResolved;
        }
        public bool Spend(int cost)
        {
            if (cost < 0 || Gold < cost) return false;
            Gold -= cost; Changed?.Invoke(); return true;
        }
        public void AddBaseHP(int amount) { MaxHP += amount; HP += amount; Changed?.Invoke(); }
        public void AddGold(int amount) { Gold+=amount; Changed?.Invoke(); }
        public void Heal(int amount) { HP=Mathf.Min(MaxHP,HP+amount); Changed?.Invoke(); }
        private void OnResolved(Enemy enemy, EnemyResolution reason)
        {
            if (reason == EnemyResolution.Killed) Gold += Mathf.RoundToInt(enemy.Data.reward * modifiers.CurrentWaveGold * modifiers.KillGold);
            if (reason == EnemyResolution.Escaped) HP = Mathf.Max(0, HP - enemy.Data.damageToBase);
            Changed?.Invoke();
        }
        private void OnDestroy() { if (enemies != null) enemies.Resolved -= OnResolved; }
    }
}
