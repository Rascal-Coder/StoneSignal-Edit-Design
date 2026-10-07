using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal
{
    public sealed class RewardManager : MonoBehaviour
    {
        private GameManager game;
        private GameConfig config;
        private WaveManager waves;
        private BlockPlacementManager blocks;
        private TowerManager towers;
        private RunModifiers modifiers;
        private RunEconomy economy;
        private readonly List<RewardData> choices = new List<RewardData>();
        public IReadOnlyList<RewardData> Choices => choices;
        public event Action Offered;
        public void Initialize(GameManager state, GameConfig data, WaveManager rounds, BlockPlacementManager blockTool, TowerManager towerTool, RunModifiers upgrades, RunEconomy resources)
        {
            game = state; config = data; waves = rounds; blocks = blockTool; towers = towerTool; modifiers = upgrades; economy = resources;
            waves.Completed += Offer;
        }
        private void Offer()
        {
            choices.Clear();
            var pool = new List<RewardData>(config.rewards);
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int index = GameRng.Rewards.Range(0,pool.Count);
                choices.Add(pool[index]); pool.RemoveAt(index);
            }
            Offered?.Invoke();
        }
        public bool Choose(int index)
        {
            if (game.State != GameState.Reward || index < 0 || index >= choices.Count) return false;
            RewardData choice = choices[index]; choices.Clear();
            choice.Apply(modifiers,economy,blocks);
            blocks.Refill(config.blocksPerBuild + modifiers.ExtraDraw + modifiers.NextDraw); modifiers.NextDraw=0; towers.Select(-1);
            waves.PrepareNext(); return true;
        }
        private void OnDestroy() { if (waves != null) waves.Completed -= Offer; }
    }
}
