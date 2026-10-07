using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.UI
{
    /// Chinese UI strings (v18, user: all in-game UI text Chinese; boot/splash stays English + digits).
    /// Rendered with LiberationSans SDF + StoneSignalRoundedCN-Heavy SDF as its first TMP fallback (Editor/CjkFontSetup).
    /// Names follow the GDD (StoneSignal_玩法设计文档.md); terms marked INVENTED are not in the GDD yet and need 玩法策划 confirmation.
    /// Game logic keeps its English reason/status strings (tests compare them); they are translated only for display (Notice).
    public static class Loc
    {
        // ---- HUD
        public static string Wave(int i, int n) => "第 " + i + " / " + n + " 波";   // INVENTED wording (GDD: "WAVE x/y")
        public const string Battle = "开战";                                          // INVENTED (GDD: "BATTLE")
        public const string RotateHint = "R 旋转 · 右键取消";
        public const string ChooseUpgrade = "选择强化", Choose = "选择";             // GDD 8.1 "波次强化"
        public const string CoreDark = "核心熄灭了", Restart = "重新开始";             // INVENTED (game over)
        public static string DrawStatus(StoneSignal.VFX.DrawPileState s) =>
            s == StoneSignal.VFX.DrawPileState.Free ? "免费" : s == StoneSignal.VFX.DrawPileState.Ad ? "抽牌" : s == StoneSignal.VFX.DrawPileState.Full ? "已满" : "0 / 2";

        // ---- towers / enemies / runes (GDD 3.4, 6, 8.3.1)
        static readonly Dictionary<string, string> Towers = new Dictionary<string, string>
        { { "Needle", "针弩" }, { "Pulse", "脉冲炮" }, { "Seismic", "震岩炮" }, { "Chill", "寒晶" } };
        static readonly Dictionary<string, string> Enemies = new Dictionary<string, string>
        { { "Drifter", "漂石史莱姆" }, { "Skimmer", "掠影蝠" }, { "Bulwark", "骨垒卫" }, { "Splitter", "裂鳞龙" }, { "Shard", "碎晶" } };
        public static string Tower(TowerData d) { if (d == null) return ""; foreach (var kv in Towers) if (d.name.StartsWith(kv.Key) || (d.displayName ?? "").StartsWith(kv.Key)) return kv.Value; return d.displayName; }
        public static string Enemy(string assetName) => Enemies.TryGetValue(assetName ?? "", out var s) ? s : assetName;
        public static readonly string[] RuneNames = { "锋·赤", "疾·苍", "望·金", "霜·紫", "丰·绿", "共鸣·白" };
        public static readonly string[] RuneEffects = { "塔伤害 +25%", "塔攻速 +20%", "塔射程 +1 格", "命中减速 15%，持续 1.5 秒", "该塔每次击杀 +1 金币", "周围 8 格内其它塔的符文效果 +50%" };
        public static string RuneTitle(int rune) => RuneNames[Mathf.Clamp(rune, 0, 5)] + "符文";
        public static string RuneDesc(int rune) => "镶嵌到墙牌\n" + RuneEffects[Mathf.Clamp(rune, 0, 5)];

        // ---- wave rewards (GDD 8.1 names; numbers come from the asset so balance changes stay in sync)
        public static string RewardTitle(RewardData r)
        {
            switch (r.effect)
            {
                case RewardEffect.AllDamage: return "全塔伤害";
                case RewardEffect.AllAttackSpeed: return "全塔攻速";
                case RewardEffect.AllRange: return "全塔射程";
                case RewardEffect.CannonRadius: return "震岩半径";
                case RewardEffect.ArrowRange: return "针弩射程";        // INVENTED (not in GDD 8.1)
                case RewardEffect.AddBlock: return "石牌补给";          // INVENTED (GDD: "加入 O 牌 + 抽牌 +1")
                case RewardEffect.NextDraw: return "下次抽牌 +" + N(r);
                case RewardEffect.PathSlow: return "路径减速";
                case RewardEffect.BonusSlot: return "扶壁";              // GDD 8.1: BonusSlot renamed 扶壁
                case RewardEffect.KillGold: return "击杀金币";
                case RewardEffect.WaveGold: return "每波金币";
                case RewardEffect.TowerDiscount: return "塔价折扣";      // GDD "塔价" (+"折扣" INVENTED)
                case RewardEffect.BaseHP: return "核心加固";            // INVENTED (GDD: "核心 HP")
                case RewardEffect.WaveHeal: return "每波回血";
                case RewardEffect.NextWaveGold: return "下波金币";       // INVENTED (not in GDD 8.1)
            }
            return r.displayName;
        }
        public static string RewardDesc(RewardData r)
        {
            switch (r.effect)
            {
                case RewardEffect.AllDamage: return "所有塔伤害 +" + P(r) + "%";
                case RewardEffect.AllAttackSpeed: return "所有塔攻速 +" + P(r) + "%";
                case RewardEffect.AllRange: return "所有塔射程 +" + P(r) + "%";
                case RewardEffect.CannonRadius: return "震岩炮爆炸半径 +" + P(r) + "%";
                case RewardEffect.ArrowRange: return "针弩射程 +" + P(r) + "%";
                case RewardEffect.AddBlock: return "牌组加入 1 张 " + Shape(r) + " 牌\n每次抽牌 +" + N(r);
                case RewardEffect.NextDraw: return "下一个建造阶段多抽 " + N(r) + " 张";
                case RewardEffect.PathSlow: return "路径上的敌人移速 -" + P(r) + "%";
                case RewardEffect.BonusSlot: return Shape(r) + " 形墙额外提供 1 个相邻塔位";
                case RewardEffect.KillGold: return "本局击杀金币 +" + P(r) + "%";
                case RewardEffect.WaveGold: return "每波开始获得 " + N(r) + " 金币";
                case RewardEffect.TowerDiscount: return "建塔费用 -" + P(r) + "%";
                case RewardEffect.BaseHP: return "核心生命上限 +" + N(r) + " 并回复";
                case RewardEffect.WaveHeal: return "每波结束核心回复 " + N(r) + " 生命";
                case RewardEffect.NextWaveGold: return "下一波击杀金币 +" + P(r) + "%";
            }
            return r.effectText;
        }
        static int P(RewardData r) => Mathf.RoundToInt(r.amount * 100);
        static int N(RewardData r) => Mathf.Max(1, Mathf.RoundToInt(r.amount));
        static string Shape(RewardData r) { var n = r.blockShape != null ? r.blockShape.displayName : ""; return string.IsNullOrEmpty(n) ? (r.effect == RewardEffect.BonusSlot ? "L" : "O") : n; }

        // ---- placement / build notices (logic keeps English; display only)
        static readonly Dictionary<string, string> Notices = new Dictionary<string, string>
        {
            { "Not enough gold", "金币不足" }, { "Tower ready", "建造完成" }, { "Placed. Route recalculated.", "已放置，路线已更新" },
            { "Enemy is crossing this cell", "敌人正在经过该格" }, { "An enemy would be trapped", "会困住敌人" },
            { "No blocks left. Start the next wave.", "没有墙牌了，开始下一波吧" }, { "Outside the board", "超出棋盘" }, { "Off board", "超出棋盘" },
            { "Cell occupied / protected", "该格已被占用或受保护" }, { "Duplicate block cell", "墙块格重复" },
            { "Blocked: the core needs an open route", "不能堵死通往核心的路线" }, { "Occupied by a tower", "已有塔" }, { "No tower selected", "未选择塔" },
        };
        /// Every static UI string (for the font atlas check; rewards are added by the caller from the reward pool).
        public static IEnumerable<string> All()
        {
            yield return Wave(10, 12); yield return Battle; yield return RotateHint; yield return ChooseUpgrade; yield return Choose; yield return CoreDark; yield return Restart;
            foreach (StoneSignal.VFX.DrawPileState s in System.Enum.GetValues(typeof(StoneSignal.VFX.DrawPileState))) yield return DrawStatus(s);
            foreach (var v in Towers.Values) yield return v; foreach (var v in Enemies.Values) yield return v;
            for (int i = 0; i < RuneNames.Length; i++) { yield return RuneTitle(i); yield return RuneDesc(i); }
            foreach (var v in Notices.Values) yield return v;
            yield return "普通精良稀有传说"; yield return "镶嵌此符文的墙块，其上的塔获得符文效果";
        }
        static readonly HashSet<string> reported = new HashSet<string>();
        public static string Notice(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            if (Notices.TryGetValue(en, out var cn)) return cn;
            foreach (char c in en) if (c >= 0x4E00 && c <= 0x9FFF) return en; // already Chinese
            if (reported.Add(en)) Debug.LogWarning("LOC MISSING: " + en);
            return en;
        }
    }
}
