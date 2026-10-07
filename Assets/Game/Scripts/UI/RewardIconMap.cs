namespace StoneSignal.UI
{
    /// v17.4 reward-card icons (art). Sprite names in Assets/Game/Art/Stylized/UI (packed into HUD.spriteatlas, resolved at
    /// runtime with ArtCatalog.UiSprite(name) - BatchWire fills ArtCatalog.uiSprites from that folder).
    /// 256 px sources, chunky toon HUD style with a BAKED hard shadow (RewardPickUI hides its soft IconShadow for these).
    /// Painter: ArtSource/Stylized/UI/reward_icons_v17_4.py; JSON copy: ArtSource/Stylized/UI/reward_icons_v17_4.json.
    public static class RewardIconMap
    {
        public const string Prefix = "ui_reward_";

        /// RewardEffect -> sprite name. Every value of StoneSignal.RewardEffect has its own icon.
        public static string For(RewardEffect e)
        {
            switch (e)
            {
                case RewardEffect.AllDamage: return "ui_reward_all_damage";             // 全塔伤害: sword + burst
                case RewardEffect.AllAttackSpeed: return "ui_reward_all_attack_speed";  // 全塔攻速: bolt + arrows
                case RewardEffect.AllRange: return "ui_reward_all_range";               // 全塔射程: needle tower + gold ring, arrows out
                case RewardEffect.ArrowRange: return "ui_reward_arrow_range";           // 针弩射程: needle tower + orange ring + needle bolt
                case RewardEffect.CannonRadius: return "ui_reward_cannon_radius";       // 震岩半径: seismic shell blast + red ring
                case RewardEffect.AddBlock: return "ui_reward_add_block";               // 石牌补给: O tetromino card + plus
                case RewardEffect.NextDraw: return "ui_reward_next_draw";               // 下次抽牌: card deck + up arrow + plus
                case RewardEffect.PathSlow: return "ui_reward_path_slow";               // 路径减速: snail with an ice shell (cyan = slow)
                case RewardEffect.BonusSlot: return "ui_reward_bonus_slot";             // 扶壁: L piece + gold extra tower slot
                case RewardEffect.KillGold: return "ui_reward_kill_gold";               // 击杀金币: coin stack + skull
                case RewardEffect.WaveGold: return "ui_reward_wave_gold";               // 每波金币: coin stack + red wave pennant
                case RewardEffect.NextWaveGold: return "ui_reward_next_wave_gold";      // 下波金币: coin stack + coin + >> next
                case RewardEffect.TowerDiscount: return "ui_reward_tower_discount";     // 塔价折扣: coin with a % price tag
                case RewardEffect.BaseHP: return "ui_reward_base_hp";                   // 核心加固: shield over the core crystal (+ green repair)
                case RewardEffect.WaveHeal: return "ui_reward_wave_heal";               // 每波回血: core crystal + green cross + loop arrow
                default: return null;
            }
        }

        /// Rune options keep the existing rune sprites (StoneSignal.VFX.RuneArt.Icon): blade, swift, sight, frost, bounty, resonance.
        public static string ForRune(int rune) => StoneSignal.VFX.RuneArt.Icon[UnityEngine.Mathf.Clamp(rune, 0, StoneSignal.VFX.RuneArt.Icon.Length - 1)];

        /// v17.4 icons carry their own hard shadow -> no extra soft IconShadow under them.
        public static bool HasBakedShadow(UnityEngine.Sprite s) => s != null && s.name.StartsWith(Prefix) && s.name != "ui_reward_gem" && s.name != "ui_reward_shard"
            && s.name != "ui_reward_ember" && s.name != "ui_reward_burst";
    }
}
