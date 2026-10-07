using UnityEngine;

namespace StoneSignal
{
    public enum RewardEffect { AllDamage, AllAttackSpeed, ArrowRange, BaseHP, NextWaveGold, CannonRadius, AllRange, AddBlock, NextDraw, PathSlow, BonusSlot, KillGold, WaveGold, TowerDiscount, WaveHeal, ExtraDraw }
    /// v18.2 (玩法策划): reward tier for the weighted offer roll (RewardRoll). Display: 普通 / 精良 / 稀有 / 传说.
    public enum RewardTier { Common, Rare, Epic, Legendary }
    /// v18.3 (玩法策划) tier table for the reward pool. Applied to the reward assets by PrototypeBuild.EnsureRewardPool (the asset field is what
    /// the roll reads). 传说 is empty for now -> its 10 permille merges into 稀有 (RewardRoll.Weights). Effects not listed -> 普通 (Listed = false).
    /// Rune options (display only, the 30% rune gate is unchanged): 锋 疾 望 霜 丰 = 精良, 共鸣·白 = 稀有 (RuneTier).
    public static class RewardTiers
    {
        public static bool Listed(RewardEffect e)
        {
            switch (e)
            {
                case RewardEffect.WaveGold: case RewardEffect.KillGold: case RewardEffect.WaveHeal: case RewardEffect.AddBlock: case RewardEffect.NextDraw: case RewardEffect.CannonRadius:
                case RewardEffect.AllRange: case RewardEffect.PathSlow: case RewardEffect.TowerDiscount: case RewardEffect.ExtraDraw:
                case RewardEffect.AllDamage: case RewardEffect.AllAttackSpeed: case RewardEffect.BonusSlot: return true;
            }
            return false;
        }
        public static RewardTier For(RewardEffect e)
        {
            switch (e)
            {
                case RewardEffect.AllRange: case RewardEffect.PathSlow: case RewardEffect.TowerDiscount: case RewardEffect.ExtraDraw: return RewardTier.Rare;      // 精良
                case RewardEffect.AllDamage: case RewardEffect.AllAttackSpeed: case RewardEffect.BonusSlot: return RewardTier.Epic;                                  // 稀有
                default: return RewardTier.Common;                                                                                                                  // 普通 (+ unlisted)
            }
        }
        /// Display tier of a rune option: 共鸣·白 (Resonance, index 5) = 稀有, the five stat runes = 精良.
        public static RewardTier RuneTier(int rune) => rune == 5 ? RewardTier.Epic : RewardTier.Rare;
    }
    [CreateAssetMenu(menuName = "StoneSignal/Reward")]
    public sealed class RewardData : ScriptableObject
    {
        public string displayName;
        [TextArea] public string description;
        public string effectText;
        public RewardEffect effect;
        public float amount = .1f;
        [Tooltip("Offer roll tier: Common 600 / Rare 300 / Epic 90 / Legendary 10 permille (after the 30% rune roll); equal odds within a tier. v18.3: set from RewardTiers by PrototypeBuild.EnsureRewardPool.")]
        public RewardTier tier = RewardTier.Common;
        public BlockShapeData blockShape;
        public void Apply(RunModifiers modifiers, RunEconomy economy, BlockPlacementManager blocks = null)
        {
            switch (effect)
            {
                case RewardEffect.AllRange: modifiers.AllRange *= 1+amount; break;
                case RewardEffect.AddBlock: if(blocks!=null) blocks.Deck.Add(blockShape); break; // v18: card only (was also ExtraDraw += amount: hidden free 2nd draw; draw stays 3 cards)
                case RewardEffect.NextDraw: modifiers.NextDraw += Mathf.RoundToInt(amount); break;
                case RewardEffect.ExtraDraw: modifiers.ExtraDraw += Mathf.Max(1, Mathf.RoundToInt(amount)); break; // 免广告再抽 (玩法策划 v18, rarity 精良): pending free 2nd draw, taken by the next Build intermission whose 2nd draw is still open
                case RewardEffect.PathSlow: modifiers.EnemySpeed *= 1-amount; break;
                case RewardEffect.BonusSlot: modifiers.BonusSlotShape = blockShape; break;
                case RewardEffect.KillGold: modifiers.KillGold *= 1+amount; break;
                case RewardEffect.WaveGold: modifiers.WaveGold += Mathf.RoundToInt(amount); break;
                case RewardEffect.TowerDiscount: modifiers.TowerCost *= 1-amount; break;
                case RewardEffect.WaveHeal: modifiers.WaveHeal += Mathf.RoundToInt(amount); break;
                case RewardEffect.AllDamage: modifiers.Damage *= 1 + amount; break;
                case RewardEffect.AllAttackSpeed: modifiers.AttackSpeed *= 1 + amount; break;
                case RewardEffect.ArrowRange: modifiers.ArrowRange *= 1 + amount; break;
                case RewardEffect.BaseHP: economy.AddBaseHP(Mathf.RoundToInt(amount)); break;
                case RewardEffect.NextWaveGold: modifiers.NextWaveGold *= 1 + amount; break;
                case RewardEffect.CannonRadius: modifiers.CannonRadius *= 1 + amount; break;
            }
        }
    }
}
