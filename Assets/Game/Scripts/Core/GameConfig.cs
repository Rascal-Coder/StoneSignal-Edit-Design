using UnityEngine;

namespace StoneSignal
{
    [CreateAssetMenu(menuName = "StoneSignal/Game Configuration")]
    public sealed class GameConfig : ScriptableObject
    {
        [Min(1)] public int baseHP = 30;
        [Min(0)] public int initialGold = 200;
        [Min(1)] public int blocksPerBuild = 3;
        [Tooltip("v18.6 (玩法策划): walls can be placed during combat. false = Build phase only (combat: the block row is disabled, drag -> toast).")]
        public bool allowCombatBlocks = false;
        [Tooltip("v18.6 (玩法策划): towers can be built for gold during combat.")]
        public bool allowCombatTowers = true;
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
        [Tooltip("v18.6 combat-phase HUD (美术策划 + 玩法策划): remaining plate, disabled styles, draw pile dim, HP bars / floating numbers, gold ring.")]
        public CombatHudStyle combatHud = new CombatHudStyle();
    }

    /// v18.6 combat-phase HUD. Sizes are 1080p reference px (x HudScaler). Colours / timings per the art spec; one disabled style everywhere.
    [System.Serializable]
    public sealed class CombatHudStyle
    {
        [Header("Remaining plate (replaces BATTLE in combat)")]
        [Tooltip("Plate height as a fraction of the BATTLE button height (same anchor / width, vertically centred).")] [Range(.3f, 1f)] public float plateHeightFrac = .8f;
        public Color plateColor = new Color(1f, 1f, 1f, .85f);
        [Min(.1f)] public float platePpuMultiplier = 2.4f;
        public float labelPx = 22f; public Color labelColor = new Color32(0xAE, 0xB6, 0xC8, 0xFF);
        public float numberPx = 36f; public Color numberColor = Color.white; public Color numberOutline = new Color32(0x1E, 0x2A, 0x4A, 0xFF); [Range(0, 1)] public float numberOutlineWidth = .30f;
        [Tooltip("Gap between the parts of '剩余 N 只' (one space), ref px.")] public float wordGapPx = 6f;
        [Tooltip("Progress bar height / inset from the plate bottom and sides, ref px.")] public float barHeightPx = 5f, barInsetPx = 12f;
        public Color barTrack = new Color32(0x1A, 0x22, 0x38, 0xFF), barFill = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
        [Tooltip("N pop on a kill / leak (not on a split): scale from -> 1 over seconds.")] public float numberPopScale = 1.15f; [Min(0)] public float numberPopSeconds = .12f;
        [Tooltip("N turns lowColor at <= lowCount remaining.")] public int lowCount = 3; public Color lowColor = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
        [Tooltip("Ember breathing dot left of '剩余': size / gap ref px, alpha min..max, period seconds.")] public float dotPx = 12f, dotGapPx = 8f; public Color dotColor = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
        [Range(0, 1)] public float dotAlphaMin = .4f, dotAlphaMax = 1f; [Min(.05f)] public float dotPeriod = 1.2f;
        [Tooltip("Visible fraction of the dot sprite (ui_reward_ember: alpha >= 50 % out to 0.65 of its size) - the rect is dotPx / this.")] [Range(.1f, 1f)] public float dotSpriteVisibleFrac = .65f;
        [Header("BATTLE <-> plate transitions")]
        public float buttonOutScale = .92f; [Min(0)] public float buttonOutSeconds = .12f;
        [Min(0)] public float plateInSeconds = .15f; public float plateInScaleFrom = .96f;
        [Min(0)] public float plateOutSeconds = .12f;
        [Tooltip("BATTLE bounce back in Build: seconds and scale curve (0.9 -> 1.08 -> 1.0, ease-out-back shape with an exact 1.08 peak).")]
        [Min(0)] public float buttonBounceSeconds = .25f;
        public AnimationCurve buttonBounce = new AnimationCurve(new Keyframe(0f, .9f, 0f, 0.6f), new Keyframe(.6f, 1.08f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));
        [Header("Draw pile in combat")]
        public Color pileMultiply = new Color(.55f, .55f, .6f, 1f); [Min(0)] public float pileSeconds = .15f;
        [Header("Disabled style (combat block row + unaffordable tower cards)")]
        [Range(0, 1)] public float disabledGray = .7f; [Range(0, 1)] public float disabledBrightness = .8f;
        [Tooltip("xN / rune badges on disabled block cards: brightness only (no grey).")] [Range(0, 1)] public float badgeBrightness = .85f;
        [Min(0)] public float disabledSeconds = .15f;
        public Color unaffordablePrice = new Color32(0xFF, 0x6B, 0x6B, 0xFF);
        [Header("Enemy HP bars (screen-space, above every floating number)")]
        public bool screenSpaceHpBars = true;
        [Tooltip("Bar size in world units (the 3D bar it replaces: 0.65 wide; visible thickness ~0.095 at the game camera pitch).")] public float hpBarWorldWidth = .65f, hpBarWorldHeight = .095f;
        [Header("Floating damage numbers (one layer below the HP bars)")]
        public float numberSpawnAbovePx = 14f, numberDriftXPx = 18f, numberDriftYPx = 26f; [Min(.05f)] public float numberSeconds = .6f;
        [Range(0, 1)] public float numberFadeTail = .4f;
        public float numberNormalPx = 24f, numberCritPx = 30f, numberPopFrom = 1.2f; [Min(0)] public float numberPopFromSeconds = .08f;
        [Tooltip("Same-target hits within this window add into the live number instead of a new one (0 = every hit its own number, alternating left / right).")]
        [Min(0)] public float numberStackSeconds = 0f;
        [Header("Gold pickup ring")]
        [Tooltip("If the ring would reach the HP orb's outer ring, its max radius is capped at this x the gold pill height.")] public float goldRingCapPillFrac = .9f;
        [Header("Hand count pill")]
        [Tooltip("During a (re)deal the 5/7 pill counts up as each entering card settles.")] public bool handCountCountsUp = true;
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

        // ---- art v18.5b follow-ups (美术策划 decisions on the v18.5 review points 1-5).
        [Header("Art v18.5b: follow-ups")]
        [Tooltip("1) Selected card +12 lift slides over this many seconds with selectLiftEase (0 = instant; position changes keep relayoutSeconds).")]
        [Min(0)] public float selectLiftSeconds = .08f;
        public AnimationCurve selectLiftEase = EaseOutCubic();
        [Tooltip("2) Expanded row: the stack's lower layers may tuck under the next card, but the top card of a xN stack and its badge are never covered - the expanded step is kept >= card + the next card's rune-badge overhang (10 card units) + this clearance, reference px.")]
        public bool stackTopNeverCovered = true;
        [Min(0)] public float stackTopClearancePx = 0f;
        [Tooltip("3) A merging stack flies above the other cards (last sibling) carrying the fan shadow (shadowColor / shadowOffset) in every fan state; it settles to its resting shadow as it lands.")]
        public bool mergeFlyShadow = true;
        [Tooltip("4) Several groups entering in one rebuild (whole-hand redeal): enter staggered left to right, this interval per card, seconds...")]
        [Min(0)] public float redealStaggerSeconds = .04f;
        [Tooltip("...with the whole hand done within this many seconds: interval = min(redealStaggerSeconds, (redealMaxSeconds - enterSeconds) / (n - 1)). Cards wait hidden until their turn.")]
        [Min(0)] public float redealMaxSeconds = .45f;
        [Tooltip("5) PC mouse hover on a fanned card (no hover state on touch; hover never expands the fan). Press keeps pressScale / pressLiftPx.")]
        public float hoverScale = 1.02f;
        public float hoverLiftPx = 4f;

        public static AnimationCurve EaseOutCubic() => new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f));
    }
}
