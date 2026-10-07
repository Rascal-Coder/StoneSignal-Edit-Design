using UnityEngine;

namespace StoneSignal
{
    [CreateAssetMenu(menuName = "StoneSignal/Game Configuration")]
    public sealed class GameConfig : ScriptableObject
    {
        [Min(1)] public int baseHP = 30;
        [Min(0)] public int initialGold = 200;
        [Min(1)] public int blocksPerBuild = 3;
        public bool allowCombatBlocks = true;
        public BlockShapeData[] blocks;
        public TowerData[] towers;
        public WaveData[] waves;
        public RewardData[] rewards;
        [Tooltip("Rune tuning + drawn-block rune table (null = RuneConfig defaults).")]
        public RuneConfig runes;
        public VisualPalette palette;
        [Tooltip("Board size, spawn points and core footprint for the game scene (null = legacy grid fields).")]
        public BoardLayoutData layout;
        [Tooltip("v18.4 block-card row fan (玩法策划 option A). Visual values are neutral placeholders until the art fan spec (美术策划) arrives.")]
        public HandFanStyle handFan = new HandFanStyle();
    }

    /// v18.4 (玩法策划 option A): after identical block cards are merged into xN stacks, more than collapseAbove slots fan the row into
    /// overlapping cards (fixed width = rowSlots card slots); tap / press-and-hold expands, drag start / empty tap / idle collapses.
    /// Gameplay rules: collapseAbove, rowSlots, minExposedPx, idle / hold times. Everything else is visual and neutral by default
    /// (linear easing, no shadow, no tint) - to be replaced by the art (美术策划) fan spec values.
    [System.Serializable]
    public sealed class HandFanStyle
    {
        [Tooltip("Fan the block row when more than this many card slots remain after stacking (N).")] [Min(1)] public int collapseAbove = 4;
        [Tooltip("Collapsed row width, in card slots (card width + gap).")] [Min(1)] public float rowSlots = 4f;
        [Tooltip("Minimum exposed strip of a covered card, reference px at 1080p (scales with the HUD).")] [Min(0)] public float minExposedPx = 44f;
        [Tooltip("Expanded card spacing, reference px (0 = the normal unfanned spacing). Clamped so the row stays left of the draw pile.")] [Min(0)] public float expandedStepPx = 0f;
        [Tooltip("Expanded row lift, reference px (0 = same height as collapsed).")] public float expandedLiftPx = 0f;
        [Tooltip("Expand animation, seconds.")] [Min(0)] public float expandSeconds = .15f;
        [Tooltip("Collapse animation, seconds.")] [Min(0)] public float collapseSeconds = .15f;
        [Tooltip("Expand easing (0..1 -> 0..1).")] public AnimationCurve expandEase = AnimationCurve.Linear(0, 0, 1, 1);
        [Tooltip("Collapse easing (0..1 -> 0..1, 1 = fully collapsed).")] public AnimationCurve collapseEase = AnimationCurve.Linear(0, 0, 1, 1);
        [Tooltip("Collapse after this long without any input while expanded, seconds.")] [Min(0)] public float idleCollapseSeconds = 2f;
        [Tooltip("Press-and-hold on the collapsed fan this long (finger still) expands it, seconds.")] [Min(0)] public float holdExpandSeconds = .35f;
        [Tooltip("Shadow each card casts on the card it covers (alpha 0 = none).")] public Color shadowColor = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Shadow offset, card units.")] public Vector2 shadowOffset = new Vector2(-6f, -4f);
        [Tooltip("Brightness of covered cards in the collapsed fan (1 = unchanged).")] [Range(0, 1)] public float coveredBrightness = 1f;
        [Tooltip("xN count badge position while the row is fanned, card units from the card's top-left (128 x 128 card, y down): on the exposed left strip.")]
        public Vector2 fanCountBadgePos = new Vector2(-8f, 42f);
    }
}
