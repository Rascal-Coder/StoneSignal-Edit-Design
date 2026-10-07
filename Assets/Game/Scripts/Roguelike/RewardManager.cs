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
        // GDD v0.2: each option independently has rewardRuneChance (30%) to be a rune instead: a wall block card carrying it.
        private readonly List<int> runeChoices = new List<int>();
        public IReadOnlyList<int> RuneChoices => runeChoices;
        public RuneConfig Runes { get; set; }
        public event Action Offered;
        public void Initialize(GameManager state, GameConfig data, WaveManager rounds, BlockPlacementManager blockTool, TowerManager towerTool, RunModifiers upgrades, RunEconomy resources)
        {
            game = state; config = data; waves = rounds; blocks = blockTool; towers = towerTool; modifiers = upgrades; economy = resources;
            waves.Completed += Offer;
        }
        private void Offer()
        {
            choices.Clear(); runeChoices.Clear();
            var pool = new List<RewardData>(config.rewards);
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int index = GameRng.Rewards.Range(0,pool.Count);
                choices.Add(pool[index]); pool.RemoveAt(index);
                int rune = RuneRules.NoRune;
                if (Runes != null && GameRng.Rewards.Permille(Runes.rewardRuneChance)) rune = RuneRules.RollType(Runes, GameRng.Rewards);
                runeChoices.Add(rune);
            }
            Offered?.Invoke();
        }
        public bool Choose(int index)
        {
            if (game.State != GameState.Reward || index < 0 || index >= choices.Count) return false;
            RewardData choice = choices[index]; int rune = runeChoices.Count > index ? runeChoices[index] : RuneRules.NoRune; choices.Clear(); runeChoices.Clear();
            if (rune != RuneRules.NoRune) blocks.AddRuneCard(rune); else choice.Apply(modifiers,economy,blocks);
            towers.Select(-1); // blocks come from the DRAW pile (1st draw free incl. ExtraDraw/NextDraw, 2nd via ad)
            waves.PrepareNext(); return true;
        }
        private void OnDestroy() { if (waves != null) waves.Completed -= Offer; }
    }
}
