using System.Collections.Generic;
using UnityEngine;
using StoneSignal.VFX;

namespace StoneSignal
{
    /// Rune tuning (all values per-mille integers unless noted). A null GameConfig.runes uses CreateDefault().
    [CreateAssetMenu(menuName = "StoneSignal/Rune Rules")]
    public sealed class RuneConfig : ScriptableObject
    {
        [Header("Effects (per-mille)")]
        public int bladeDamage = 250;        // 锋 damage +25%
        public int swiftAttackSpeed = 200;   // 疾 attack speed +20%
        public int sightRangeCells = 1000;   // 望 range +1 cell (per-mille of a cell)
        public int frostSlow = 150;          // 霜 slow 15%
        public int frostDurationMs = 1500;   //    for 1.5 s
        public int bountyGold = 1000;        // 丰 +1 gold per kill (per-mille of a gold, rounded per kill)
        public int resonanceBoost = 500;     // 共鸣 other towers' rune effects +50%
        [Tooltip("Same rune on one tower: 1st, 2nd, 3rd, 4th copy weight (per-mille). Copies beyond the table have no effect (GDD v0.2: 4th = 0).")]
        public int[] stacking = { 1000, 500, 250, 0 };
        [Tooltip("Per-mille chance that each 3-choose-1 reward option is a rune (GDD v0.2: 30%).")]
        public int rewardRuneChance = 300;
        [Header("Resonance area (AMBIGUOUS in design: '8 cells around')")]
        [Tooltip("Chebyshev distance in cells from the resonance tower's footprint. 1 = the 8 neighbouring cells (default); 8 = radius 8 (art's RuneArt.ResonanceRadiusCells).")]
        public int resonanceRadiusCells = 1;
        [Header("Drawn block rune roll (PLACEHOLDER table for the gameplay designer)")]
        [Tooltip("Per-mille chance a drawn wall block carries a rune.")]
        public int runeChance = 250;      // designers v12 review: none 750 / rune 250
        [Tooltip("Relative weights Blade, Swift, Sight, Frost, Bounty, Resonance.")]
        public int[] runeWeights = { 180, 180, 180, 180, 180, 100 };
        public static RuneConfig CreateDefault() { var c = CreateInstance<RuneConfig>(); c.hideFlags = HideFlags.DontSave; return c; }
    }

    public struct RuneStats
    {
        public int damage, attackSpeed, rangeCells, slow, slowMs, bounty; // per-mille
        public bool resonated;
        public float DamageMul => 1 + damage / 1000f;
        public float AttackSpeedMul => 1 + attackSpeed / 1000f;
        public float RangeCells => rangeCells / 1000f;
        public float SlowFraction => slow / 1000f;
        public float SlowSeconds => slowMs / 1000f;
    }

    /// Pure rune rules (unit-tested in Editor/RuneDrawTests).
    public static class RuneRules
    {
        public static readonly string[] Names = { "Blade", "Swift", "Sight", "Frost", "Bounty", "Resonance" };
        public static readonly string[] Effects = { "Damage +25%", "Attack speed +20%", "Range +1 cell", "Slow 15% for 1.5s", "+1 gold per kill", "Nearby towers' runes +50%" };
        public const int NoRune = -1;
        /// runes = runes inlaid under the tower's footprint; resonated = another tower with Resonance covers it.
        public static RuneStats Compute(RuneConfig c, IList<int> runes, bool resonated)
        {
            var s = new RuneStats { resonated = resonated };
            var counts = new int[6];
            if (runes != null) foreach (int r in runes) if (r >= 0 && r < 6) counts[r]++;
            int boost = resonated ? 1000 + c.resonanceBoost : 1000;
            int Stack(int value, int n) { long sum = 0; for (int k = 0; k < n && k < c.stacking.Length; k++) sum += (long)value * c.stacking[k] / 1000; return (int)(sum * boost / 1000); }
            s.damage = Stack(c.bladeDamage, counts[(int)RuneId.Blade]);
            s.attackSpeed = Stack(c.swiftAttackSpeed, counts[(int)RuneId.Swift]);
            s.rangeCells = Stack(c.sightRangeCells, counts[(int)RuneId.Sight]);
            s.slow = Stack(c.frostSlow, counts[(int)RuneId.Frost]);
            s.slowMs = counts[(int)RuneId.Frost] > 0 ? c.frostDurationMs : 0;
            s.bounty = Stack(c.bountyGold, counts[(int)RuneId.Bounty]);
            return s;
        }
        /// Chebyshev gap between two footprints (0 = overlapping/touching cells share an edge or corner -> 1).
        public static int Gap(Vector2Int aMin, Vector2Int aSize, Vector2Int bMin, Vector2Int bSize)
        {
            int dx = Mathf.Max(0, Mathf.Max(bMin.x - (aMin.x + aSize.x - 1), aMin.x - (bMin.x + bSize.x - 1)));
            int dy = Mathf.Max(0, Mathf.Max(bMin.y - (aMin.y + aSize.y - 1), aMin.y - (bMin.y + bSize.y - 1)));
            return Mathf.Max(dx, dy);
        }
        /// True when the target footprint lies within radius cells of a resonance source footprint (only one resonance counts).
        public static bool InResonance(RuneConfig c, Vector2Int srcMin, Vector2Int srcSize, Vector2Int tgtMin, Vector2Int tgtSize)
            => Gap(srcMin, srcSize, tgtMin, tgtSize) <= c.resonanceRadiusCells;
        /// Roll a rune for a drawn wall block from the rewards stream: NoRune or a RuneId index.
        public static int Roll(RuneConfig c, RngStream rng) => rng.Permille(c.runeChance) ? RollType(c, rng) : NoRune;
        /// Rune type only (weights), used when a rune is guaranteed (NextDraw reward, rune reward option).
        public static int RollType(RuneConfig c, RngStream rng)
        {
            int total = 0; foreach (int w in c.runeWeights) total += Mathf.Max(0, w);
            if (total <= 0) return NoRune;
            int pick = rng.Range(0, total);
            for (int i = 0; i < c.runeWeights.Length && i < 6; i++) { pick -= Mathf.Max(0, c.runeWeights[i]); if (pick < 0) return i; }
            return NoRune;
        }
    }

    /// Per-intermission DRAW allowance: 1st draw free, 2nd after a rewarded ad, no 3rd, never for gold.
    public sealed class DrawRules
    {
        public enum Offer { Free, Ad, None }
        public int Used { get; private set; }
        /// ExtraDraw reward: this intermission's 2nd draw needs no ad.
        public bool FreeSecond { get; private set; }
        public Offer Next => Used == 0 ? Offer.Free : Used == 1 ? (FreeSecond ? Offer.Free : Offer.Ad) : Offer.None;
        public void BeginIntermission(bool freeSecond = false) { Used = 0; FreeSecond = freeSecond; }
        /// Consumes the next offer; ad offers only after the ad reported success.
        public bool Consume(bool adWatched)
        {
            if (Next == Offer.None || (Next == Offer.Ad && !adWatched)) return false;
            Used++; return true;
        }
    }
}
