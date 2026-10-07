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
        // v18.2 (玩法策划): per option, 30% rune option (RuneConfig.rewardRuneChance) else a tier roll 600/300/90/10 permille, equal odds
        // within the tier, no duplicates within one pick-of-three. Rewards RNG stream only (RewardRoll). Rune options carry choices[i] = null.
        private readonly List<int> tierScratch = new List<int>(); private readonly List<int> pickScratch = new List<int>();
        private void Offer()
        {
            choices.Clear(); runeChoices.Clear(); tierScratch.Clear();
            foreach (var r in config.rewards) tierScratch.Add(r != null ? (int)r.tier : -1);
            int runeChance = Runes != null ? Runes.rewardRuneChance : 0; var runes = Runes;
            RewardRoll.Offer(tierScratch, runeChance, rng => runes != null ? RuneRules.RollType(runes, rng) : RuneRules.NoRune, GameRng.Rewards, pickScratch, runeChoices, 3);
            foreach (int i in pickScratch) choices.Add(i >= 0 ? config.rewards[i] : null);
            Offered?.Invoke();
        }
        /// Diagnostics (GameplayShot reward screen): roll a real offer now (caller sets the Reward state).
        public void DebugOffer() => Offer();
        public bool Choose(int index)
        {
            if (game.State != GameState.Reward || index < 0 || index >= choices.Count) return false;
            RewardData choice = choices[index]; int rune = runeChoices.Count > index ? runeChoices[index] : RuneRules.NoRune; choices.Clear(); runeChoices.Clear();
            if (rune != RuneRules.NoRune) blocks.AddRuneCard(rune); else if (choice != null) choice.Apply(modifiers,economy,blocks);
            towers.Select(-1); // blocks come from the DRAW pile (1st draw free incl. ExtraDraw/NextDraw, 2nd via ad)
            waves.PrepareNext(); return true;
        }
        private void OnDestroy() { if (waves != null) waves.Completed -= Offer; }
    }

    /// v18.2 rarity-weighted reward offer (pure, deterministic per stream; EditMode-tested in RuneDrawTests).
    /// Per option: rng.Permille(runeChance) -> rune option (type from rollRune; a type already offered in this pick is re-rolled up to
    /// 8 times, then the option falls through to a reward). Otherwise a tier roll over TierPermille. Empty tiers (no eligible reward:
    /// none in the pool, or all already offered in this pick) hand their weight DOWN to the next lower non-empty tier
    /// (Legendary -> Epic -> Rare -> Common); if no lower tier is left, UP to the next higher non-empty one. Equal odds within a tier.
    /// If no reward is eligible at all, the option is skipped (fewer than 3 options).
    public static class RewardRoll
    {
        public static readonly int[] TierPermille = { 600, 300, 90, 10 };
        public const int Tiers = 4, RuneRerolls = 8;
        /// Effective tier weights for the current eligibility counts (exposed for tests / logs).
        public static void Weights(int[] count, int[] w)
        {
            for (int t = 0; t < Tiers; t++) w[t] = TierPermille[t];
            for (int t = Tiers - 1; t > 0; t--) if (count[t] == 0) { int lower = -1; for (int k = t - 1; k >= 0; k--) if (count[k] > 0) { lower = k; break; } if (lower >= 0) { w[lower] += w[t]; w[t] = 0; } }
            for (int t = 0; t < Tiers; t++) if (count[t] == 0 && w[t] > 0) { int up = -1; for (int k = t + 1; k < Tiers; k++) if (count[k] > 0) { up = k; break; } if (up >= 0) w[up] += w[t]; w[t] = 0; }
        }
        /// tiers[i] = tier of pool entry i (-1 = not offerable). Fills poolIndex (-1 for rune options) and runes (NoRune for rewards).
        public static void Offer(IReadOnlyList<int> tiers, int runeChance, Func<RngStream, int> rollRune, RngStream rng, List<int> poolIndex, List<int> runes, int options = 3)
        {
            poolIndex.Clear(); runes.Clear();
            var count = new int[Tiers]; var w = new int[Tiers];
            for (int o = 0; o < options; o++)
            {
                if (runeChance > 0 && rng.Permille(runeChance))
                {
                    int rune = RuneRules.NoRune;
                    for (int k = 0; k <= RuneRerolls; k++) { int r = rollRune(rng); if (r != RuneRules.NoRune && !runes.Contains(r)) { rune = r; break; } }
                    if (rune != RuneRules.NoRune) { poolIndex.Add(-1); runes.Add(rune); continue; }
                }
                for (int t = 0; t < Tiers; t++) count[t] = 0;
                for (int i = 0; i < tiers.Count; i++) if (tiers[i] >= 0 && tiers[i] < Tiers && !poolIndex.Contains(i)) count[tiers[i]]++;
                Weights(count, w); int total = 0; for (int t = 0; t < Tiers; t++) total += w[t];
                if (total <= 0) continue; // nothing left to offer
                int pick = rng.Range(0, total), tier = 0;
                for (int t = 0; t < Tiers; t++) { if (pick < w[t]) { tier = t; break; } pick -= w[t]; }
                int nth = rng.Range(0, count[tier]);
                for (int i = 0; i < tiers.Count; i++) if (tiers[i] == tier && !poolIndex.Contains(i) && nth-- == 0) { poolIndex.Add(i); runes.Add(RuneRules.NoRune); break; }
            }
        }
    }
}
