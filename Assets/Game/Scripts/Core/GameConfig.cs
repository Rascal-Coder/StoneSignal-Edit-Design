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
        [Tooltip("Expanded row lift, reference px (0 = same height as collapsed).")] public float expandedLiftPx = 20f; // art v18.4
        [Tooltip("Expand animation, seconds.")] [Min(0)] public float expandSeconds = .15f;
        [Tooltip("Collapse animation, seconds.")] [Min(0)] public float collapseSeconds = .15f;
        [Tooltip("Expand easing (0..1 -> 0..1).")] public AnimationCurve expandEase = EaseOutCubic(); // art v18.4: 1-(1-t)^3 (exact with these 2 keys)
        [Tooltip("Collapse easing (0..1 -> 0..1, 1 = fully collapsed).")] public AnimationCurve collapseEase = EaseOutCubic();
        [Tooltip("Collapse after this long without any input while expanded, seconds.")] [Min(0)] public float idleCollapseSeconds = 2f;
        [Tooltip("Press-and-hold on the collapsed fan this long (finger still) expands it, seconds.")] [Min(0)] public float holdExpandSeconds = .35f;
        [Tooltip("Shadow each card casts on the card it covers (alpha 0 = none).")] public Color shadowColor = new Color(.05f, .07f, .16f, .5f); // art v18.4: #0D1229 a.50
        [Tooltip("Shadow offset, card units.")] public Vector2 shadowOffset = new Vector2(-5f, -3f);
        [Tooltip("Brightness of covered cards in the collapsed fan (1 = unchanged).")] [Range(0, 1)] public float coveredBrightness = .88f;
        [Tooltip("xN count badge position while the row is fanned, card units from the card's top-left (128 x 128 card, y down): on the exposed left strip.")]
        public Vector2 fanCountBadgePos = new Vector2(-12f, 100f); // art v18.4 final: bottom-left corner, clear of every block icon (icons end at y 99)

        // ---- art v18.4 additions (美术策划 扇形手牌规范 v18.4). All visual; 0 / false = the 7c977d5 behaviour.
        [Header("Art v18.4: fan shape")]
        [Tooltip("Collapsed fan: tilt of the outermost cards, degrees (left +, right -, linear in between).")] [Range(0, 6)] public float tiltDeg = 3f;
        [Tooltip("Collapsed fan: centre cards sit this much higher than the outer ones, reference px (parabola).")] [Min(0)] public float arcSagPx = 6f;
        [Tooltip("Expanded row: tilt of the outermost cards, degrees.")] [Range(0, 6)] public float expandedTiltDeg = 2f;
        [Tooltip("Expanded row: centre arc, reference px (on top of expandedLiftPx).")] [Min(0)] public float expandedArcSagPx = 8f;
        [Tooltip("Fanned row keeps the plain spacing (card + 12) even when a xN stack exists, so the collapsed row is always rowSlots wide (636 at card 150).")]
        public bool fixedRowIgnoresStackSpacing = true;
        [Header("Art v18.4: motion")]
        [Tooltip("Expand / collapse stagger per card, seconds (rightmost card moves first; expandSeconds / collapseSeconds stay the total).")] [Min(0)] public float staggerSeconds = .008f;
        [Tooltip("Cards that change slot after a rebuild slide from their old pose, seconds (EaseOutCubic).")] [Min(0)] public float relayoutSeconds = .15f;
        [Tooltip("A new group (drawn card) enters from enterOffsetPx at enterScaleFrom, seconds (EaseOutBack).")] [Min(0)] public float enterSeconds = .2f;
        public Vector2 enterOffsetPx = new Vector2(48f, -24f);
        [Range(.5f, 1f)] public float enterScaleFrom = .85f;
        [Tooltip("xN badge pop when a stack grows: start scale and seconds (EaseOutCubic back to 1).")] public float countBumpScale = 1.25f;
        [Min(0)] public float countBumpSeconds = .18f;
        [Header("Art v18.4: press / layout")]
        [Tooltip("Press / hover on a fanned card: scale and lift, reference px (unfanned rows keep CardHover 1.06 / 12). Press also lifts the covered-card dimming.")]
        public float pressScale = 1.04f;
        public float pressLiftPx = 8f;
        [Tooltip("Hand-count pill (7/7) bottom = row y + card + this, reference px (was 8: overlapped a selected left card with a rune badge). Rises with expandedLiftPx while expanded.")]
        public float handCountGapPx = 28f;

        public static AnimationCurve EaseOutCubic() => new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f));
    }
}
