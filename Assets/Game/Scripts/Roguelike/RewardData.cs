using UnityEngine;

namespace StoneSignal
{
    public enum RewardEffect { AllDamage, AllAttackSpeed, ArrowRange, BaseHP, NextWaveGold, CannonRadius, AllRange, AddBlock, NextDraw, PathSlow, BonusSlot, KillGold, WaveGold, TowerDiscount, WaveHeal }
    [CreateAssetMenu(menuName = "StoneSignal/Reward")]
    public sealed class RewardData : ScriptableObject
    {
        public string displayName;
        [TextArea] public string description;
        public string effectText;
        public RewardEffect effect;
        public float amount = .1f;
        public BlockShapeData blockShape;
        public void Apply(RunModifiers modifiers, RunEconomy economy, BlockPlacementManager blocks = null)
        {
            switch (effect)
            {
                case RewardEffect.AllRange: modifiers.AllRange *= 1+amount; break;
                case RewardEffect.AddBlock: if(blocks!=null) blocks.Deck.Add(blockShape); modifiers.ExtraDraw += Mathf.RoundToInt(amount); break;
                case RewardEffect.NextDraw: modifiers.NextDraw += Mathf.RoundToInt(amount); break;
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
