using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Loc = StoneSignal.UI.Loc;

namespace StoneSignal
{
    // In-game HUD per Docs/ui_mockup_v3 (art v11). One overlay canvas for static HUD + one for the hand (dirty rebuilds only),
    // TextMeshPro text, sprites from ArtCatalog (Assets/Game/Art/Stylized/UI, packed in one SpriteAtlas). No debug text.
    // Hand card hover: scale 1.06 and +12 px (ui_card_spec_v13).
    public sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        RectTransform card; Vector2 rest; Vector3 restScale; bool lifted, over, down;
        /// art v18.4: fanned rows use HandFanStyle.pressScale / pressLiftPx; Over drives the press un-dim.
        public float Scale = 1.06f, Lift = 12f;
        /// art v18.5b: fanned rows split PC hover (HoverScale / HoverLift, mouse only - no hover state in touch mode) from press (Scale / Lift
        /// while the pointer is down on the card). HoverScale 0 = the v17 behaviour (pointer enter = the raised look; unfanned rows, tower cards).
        public float HoverScale, HoverLift;
        bool Split => HoverScale > 0f;
        /// Press look active (un-dim, scale owned by the hover).
        public bool Over => Split ? down && over : over;
        /// PC hover look active (fanned rows, mouse, pointer over and not pressed).
        public bool Hovered => Split && over && !down && !PointerInput.TouchMode;
        public bool Raised => (Active == null || Active()) && (Over || Hovered);
        /// v18.6: null or true = hover / press looks on; false (block row in combat) = the card stays put (no lift / scale), it stays dimmed.
        public System.Func<bool> Active;
        public void Init(RectTransform target, bool alreadyLifted) { card = target; rest = target.anchoredPosition; restScale = target.localScale; lifted = alreadyLifted; }
        Vector2 Offset => lifted || !Raised ? Vector2.zero : new Vector2(0, Over ? Lift : HoverLift);
        /// v18.4 fan animation moves the rest position.
        public void SetRest(Vector2 p) { rest = p; card.anchoredPosition = rest + Offset; }
        void Apply() { card.localScale = Raised ? restScale * (Over ? Scale : HoverScale) : restScale; card.anchoredPosition = rest + Offset; }
        public void OnPointerEnter(PointerEventData e) { over = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { over = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { if (!Split) return; down = true; Apply(); }
        public void OnPointerUp(PointerEventData e) { if (!Split) return; down = false; Apply(); }
        void OnDisable() { if (Split && card != null && (over || down)) { over = down = false; Apply(); } }
    }
    /// v18.4 block-row fan geometry (ref px). n slots of cardW, normal spacing step; budget = widest expanded row (left of the draw pile).
    public static class HandFanLayout
    {
        public struct Row { public bool fanned; public int slots; public float cardW, collapsedStep, expandedStep, collapsedWidth, expandedWidth, exposed; }
        public static Row Compute(int n, float cardW, float step, float budget, HandFanStyle st, float needExposed, float needExpandedStep = 0f)
        {
            st = st ?? new HandFanStyle(); var r = new Row { slots = n, cardW = cardW, fanned = n > st.collapseAbove };
            if (!r.fanned) r.collapsedStep = r.expandedStep = step;
            else
            {
                float gap = step - cardW, rowW = st.rowSlots * step - gap;
                float f = Mathf.Max((rowW - cardW) / Mathf.Max(1, n - 1), Mathf.Max(st.minExposedPx, needExposed));
                float e = st.expandedStepPx > 0f ? st.expandedStepPx : step; if (n > 1 && budget > cardW) e = Mathf.Min(e, (budget - cardW) / (n - 1));
                r.collapsedStep = f; r.expandedStep = Mathf.Max(Mathf.Max(e, f), needExpandedStep); // v18.5b: needExpandedStep keeps a xN stack's top card uncovered
            }
            r.collapsedWidth = n > 0 ? cardW + r.collapsedStep * (n - 1) : 0f; r.expandedWidth = n > 0 ? cardW + r.expandedStep * (n - 1) : 0f;
            r.exposed = r.fanned ? Mathf.Min(r.collapsedStep, cardW) : cardW; return r;
        }
        /// art v18.5b redeal: n groups entering together start left to right this far apart, seconds:
        /// min(redealStaggerSeconds, (redealMaxSeconds - enterSeconds) / (n - 1)), so the whole hand is done within redealMaxSeconds.
        public static float RedealInterval(int n, HandFanStyle st)
        {
            st = st ?? new HandFanStyle(); if (n < 2 || st.redealStaggerSeconds <= 0f) return 0f;
            return Mathf.Max(0f, Mathf.Min(st.redealStaggerSeconds, (st.redealMaxSeconds - st.enterSeconds) / (n - 1)));
        }
    }
    public sealed class GameUI : MonoBehaviour
    {
        private GameBootstrap session;
        private ArtCatalog art;
        private TextMeshProUGUI hpNumber, hpSmall, gold, wave, hint, targetLabel; private RectTransform hpPlate;
        private RectTransform goldPill, handCount; private StoneSignal.VFX.DrawPileUI drawPile; private StoneSignal.VFX.RewardCounterUI goldCounter;
        /// v18.6 gold ring fix: kill gold flies to the coin icon (pivot = its centre; RewardFlyFx aims at target.position). It was the pill
        /// RectTransform, whose pivot is its top-left corner - every absorb flash landed on the pill's corner, between the core orb and the pill.
        public RectTransform GoldTarget => goldCoin != null ? goldCoin : goldPill; public StoneSignal.VFX.RewardCounterUI GoldCounter => goldCounter;
        private RectTransform goldCoin, goldRing;
        public RectTransform DebugGoldPill => goldPill; public RectTransform DebugGoldRing => goldRing; public RectTransform DebugCoreOrb => hudOrb;
        private Button battle;
        private readonly Button[] speedButtons = new Button[4];
        private RectTransform towerHand, blockHand;
        private readonly List<GameObject> built = new List<GameObject>();
        private GameObject rewardPanel, overPanel;
        private readonly TextMeshProUGUI[] rewardNames = new TextMeshProUGUI[3], rewardDescriptions = new TextMeshProUGUI[3], rewardEffects = new TextMeshProUGUI[3];
        private float noticeUntil; private string notice; private bool handDirty = true; private RectTransform noticePill;
        // notice toast (玩法策划 v18): 1.5 s, one at a time (a new text replaces the old one), the same text is not re-shown within 1 s of
        // its last show; re-placed per notice in the free top slot farthest from the core and the path arrows (routes change with walls).
        private const float NoticeSeconds = 1.5f, NoticeRepeatGap = 1f; // v17.6: ToastFx pop 0.18 + hold 1.12 + fade 0.2 = the planner's 1.5 s total
        private StoneSignal.UI.ToastFx noticeFx;
        private float noticeShownAt = -10f; private bool noticeDirty; private string noticeInfo = "";
        // slot: anchor (0 = top-centre, 1 = top-right, 2 = top-left), x, y from the top edge (reference px, inside the safe area)
        public StoneSignal.UI.ToastFx NoticeFx => noticeFx; public string NoticeDebug => noticeInfo; public RectTransform NoticeRect => noticePill;
        public string NoticeShown => noticePill != null && noticePill.gameObject.activeSelf && hint != null ? hint.text : "";
        // drag hand fade (玩法策划 v18): CanvasGroup alpha per hand card (no extra draw calls), 1 -> .35 over .15 s while the drag finger or
        // the ghost is over the hand; the dragged card stays opaque; restores on leaving / release.
        private const float HandFadeAlpha = .35f, HandFadeSeconds = .15f;
        private bool handPassApplied; public bool HandPassThroughActive => handPassApplied;
        private float handAlpha = 1f, handAlphaApplied = -1f; private int fadeKey = -1, fadeKeyApplied = -2; private bool lastAdAvailable = true;
        private RectTransform hudOrb, hudBanner; private float handCardW = 150, handRowY = 276;
        private readonly List<GameObject> hotkeyBadges = new List<GameObject>(); private bool badgesTouch;
        private readonly List<(CanvasGroup cg, bool tower, int index)> handCards = new List<(CanvasGroup, bool, int)>();
        public float HandAlpha => handAlpha;
        public string HandAlphas { get { var o = ""; foreach (var h in handCards) if (h.cg) o += (h.tower ? "T" : "B") + h.index + "=" + h.cg.alpha.ToString("F2") + " "; return o; } }
        private int speedIndex = 1; private bool paused;
        private const string DefaultHint = ""; // placement hint removed (玩法策划 v18); the label only shows notices

        static readonly Color Ink = Color.white, Navy = Hex("1E2A4A"), Slate = Hex("3B4566"), Blue = Hex("2F5FD0"),
            Red = Hex("D9404A"), Gold = Hex("F7C948"), Orange = Hex("F59A3A"), Blueprint = Hex("2A5DB0"), Shadow = new Color(0, 0, 0, .35f);

        public void Initialize(GameBootstrap game)
        {
            session = game; art = game.config.palette != null ? game.config.palette.art : null;
            if (EventSystem.current == null) { var es = new GameObject("Event system"); es.transform.SetParent(transform); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }
            var root = Canvas("HUD", 0);
            // ---- top-left: core orb (HP number only) + small HP/Max below; gold pill
            var orb = Img(root, "Core orb", art ? art.uiOrbCore : null, Hex("3A8FE0")); TL(orb, 24, 24, 150, 150);
            var glow = Img(orb, "Orb light", Circle, new Color(.75f, .93f, 1f, .42f)); Center(glow, 0, 0, 110, 110); // brighter, lighter core
            hpNumber = Txt(orb, "", 64, Ink); Full(hpNumber.rectTransform); hpNumber.outlineWidth = .2f; hpNumber.outlineColor = Navy;
            // art v18.4 HUD small text: text that sits straight on the 3D scene gets a dark translucent plate (ui9_panel_navy, same atlas as the
            // orb / hand-count pill -> batches with them, +0 DC) and a 3 px outline (outlineWidth = px x 2.2 / font size). Plate starts at y 176,
            // below the orb rect (24..174), so it never overlaps the orb number and stays in the orb's batch.
            hpPlate = Panel(root, "HP plate", "ui9_panel_navy", Navy); Set(hpPlate, new Vector2(0, 1), new Vector2(.5f, 1), 99, -176, 64, 28);
            { var pi = hpPlate.GetComponent<Image>(); pi.pixelsPerUnitMultiplier = 2.4f; pi.color = S("ui9_panel_navy") != null ? new Color(1f, 1f, 1f, .85f) : new Color(Navy.r, Navy.g, Navy.b, .85f); pi.raycastTarget = false; } // v18.5 review: no sprite -> navy, not a white plate
            hpSmall = Txt(root, "", 22, Ink); Set(hpSmall.rectTransform, new Vector2(0, 1), new Vector2(.5f, 1), 99, -176, 150, 28); hpSmall.outlineWidth = .30f; hpSmall.outlineColor = Navy;
            var pill = Panel(root, "Gold", "ui9_pill_gold", Gold); TL(pill, 190, 44, 220, 72);
            RectTransform coin;
            if (S("ui_coin_gold") != null) { coin = Img(pill, "Coin", S("ui_coin_gold"), Color.white); Set(coin, new Vector2(0, 1), new Vector2(.5f, .5f), 6 + 30, -(6 + 30), 60, 60); } // v18.6: pivot = centre (fly target + punch about the coin centre; was TL 6,6)
            else
            {
                var coinRim = Img(pill, "Coin rim", Circle, Navy); Set(coinRim, new Vector2(0, 1), new Vector2(.5f, .5f), 10 + 27, -(9 + 27), 54, 54); coin = coinRim;
                var face = Img(coinRim, "Coin", Circle, Hex("FFC83D")); Center(face, 0, 0, 44, 44);
                var dollar = Txt(face, "$", 28, Hex("8A5A00")); Full(dollar.rectTransform);
            }
            gold = Txt(pill, "", 40, Navy); TL(gold.rectTransform, 66, 4, 146, 64); gold.alignment = TextAlignmentOptions.Center;
            // kill gold flies into this counter (art RewardFlyFx + RewardCounterUI)
            goldCounter = pill.gameObject.AddComponent<StoneSignal.VFX.RewardCounterUI>(); goldCounter.label = gold; goldCounter.punchTarget = pill; goldCounter.icon = coin;
            var ringSprite = S("ui_counter_glow_ring");
            goldCoin = coin;
            if (ringSprite != null)
            {   // v18.6 gold ring fix: centred on the coin centre (pivot .5/.5 - it used to scale about its top-left corner and drift 31 px down-right
                // towards the orb), drawn above the pill; max radius capped when it would reach the HP orb's outer ring.
                var ring = Img(pill, "Glow ring", ringSprite, new Color(1, 1, 1, 0)); Set(ring, new Vector2(0, 1), new Vector2(.5f, .5f), 6 + 30, -(6 + 30), 126, 126);
                ring.GetComponent<Image>().raycastTarget = false; goldCounter.glowRing = ring.GetComponent<Image>(); goldRing = ring;
                goldCounter.ringMaxScale = GoldRingMaxScale(126f, 72f);
            }
            var pop = Txt(pill, "", 28, Gold); TL(pop.rectTransform, 80, 66, 140, 36); pop.outlineWidth = .25f; pop.outlineColor = Navy; goldCounter.incomePop = pop;
            goldCounter.SetValue(session.Economy.Gold, true);
            goldPill = pill;
            // ---- top-centre wave banner
            var banner = Panel(root, "Wave banner", "ui9_banner_wave_red", Red); TC(banner, 0, 24, 440, 92); hudOrb = orb; hudBanner = banner;
            wave = Txt(banner, "", 44, Ink); UseCn(wave); Full(wave.rectTransform); wave.outlineWidth = .2f; wave.outlineColor = Navy;
            // ---- top-right speed + strike target
            string[] speeds = { "II", "x1", "x2", "x3" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i; var b = Btn(root, speeds[i], 34, "ui9_button_navy_normal", Slate, () => SetSpeed(idx)); TR((RectTransform)b.transform, -(24 + (3 - i) * 108), 24, 96, 88); speedButtons[i] = b;
            }
            // TARGET button removed from the HUD (user); towers keep the default First targeting internally.
            // ---- bottom: hint pill, battle, draw deck
            // no hint pill / instruction text this version (user decision; tutorial later). R / RMB input unchanged.
            // DRAW pile v16 (art StoneSignal.VFX.DrawPileUI): 200x268 bottom-right at (-312,+24), 32 px left of BATTLE (256x104 at (-24,+24)).
            // Own nested canvas: the pill bob animates every frame and would otherwise rebuild the whole HUD canvas mesh.
            var pileGo = new GameObject("DrawPileRoot", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)); pileGo.SetActive(false);
            var pileRt = (RectTransform)pileGo.transform; pileRt.SetParent(root, false); BR(pileRt, -312, 24, 200, 268);
            drawPile = pileGo.AddComponent<StoneSignal.VFX.DrawPileUI>();
            drawPile.topCard = S("ui_draw_pile"); drawPile.layerCard = S("ui_draw_pile_layer") ?? S("ui_draw_pile"); drawPile.pillSprite = S("ui9_draw_bubble");
            drawPile.tailSprite = S("ui_draw_bubble_tail"); drawPile.freeIcon = S("ui_icon_free"); drawPile.adIcon = S("ui_badge_video_ad");
            pileGo.SetActive(true); // Awake builds the stack/pill with the sprites above
            drawPile.button.onClick.AddListener(() => { if (session.Draw()) drawPile.PlayDrawPulse(); });
            // hand counter "5/7" (navy pill above the block row, left-aligned)
            handCount = Panel(root, "Hand count", "ui9_panel_navy", Navy); BL(handCount, 24, 0, 108, 52);
            var hc = Txt(handCount, "", 28, Ink); Full(hc.rectTransform); hc.fontStyle = FontStyles.Bold; drawPile.handCountLabel = hc;
            // placement / build notice toast (no standing hint text; v18 had no notice display at all once the hint pill went away):
            // navy pill bottom-centre above the hand row, shown 2.4 s per notice, never takes input
            noticePill = Panel(root, "Notice", "ui9_panel_navy", Navy); BC(noticePill, 0, 524, 560, 68);
            hint = Txt(noticePill, "", 32, Ink); Full(hint.rectTransform); hint.rectTransform.offsetMin = new Vector2(16, 4); hint.rectTransform.offsetMax = new Vector2(-16, -4); UseCn(hint);
            noticeFx = StoneSignal.UI.ToastStyle.Apply(noticePill, hint, S);   // v17.6 toast (art): ui9_toast, 78 px rect, warn/info icon slot, 30 px text, ToastFx
            if (noticeFx != null) { noticeFx.hold = Mathf.Max(0f, NoticeSeconds - noticeFx.popIn - noticeFx.fadeOut); } // keep the accepted 1.5 s total (art default 1.58 s)
            noticePill.gameObject.SetActive(false);
            battle = Btn(root, Loc.Battle, 44, "ui9_button_battle_orange", Orange, () => session.Waves.StartWave(), CnFont); BR((RectTransform)battle.transform, -24, 24, 256, 104);
            battle.name = "BATTLE"; if (drawPile.statusLabel != null) UseCn(drawPile.statusLabel); { var bl = battle.GetComponentInChildren<TextMeshProUGUI>(); bl.rectTransform.offsetMin = new Vector2(22, 8); bl.rectTransform.offsetMax = new Vector2(-22, 0); bl.enableWordWrapping = false; bl.overflowMode = TextOverflowModes.Overflow; bl.enableAutoSizing = true; bl.fontSizeMin = 28; bl.fontSizeMax = 40; } // label kept inside the 9-slice face (it touched the rim)
            BuildRemainingPlate(root);
            var handCanvas = Canvas("Hand", 1);
            { var hcv = handCanvas.GetComponentInParent<Canvas>(); if (hcv != null) hcv.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1; } // v18.6 UiDisable (grey / brightness in uv1)
            // art v18.4: block row first, tower row on top - a xN stack underlay in the rightmost fan slot (and the tilted outer corners) now
            // tucks under the tower cards instead of covering tower card 3's 2x2 badge (7c977d5: 849 px2 of it). Same canvas, same materials: DC +0.
            blockHand = Group(handCanvas, "Block hand"); Full(blockHand);
            towerHand = Group(handCanvas, "Tower hand"); Full(towerHand);
            BuildRewardPanel(Canvas("Overlays", 2)); 
            session.Economy.Changed += Refresh; session.Waves.Changed += Refresh;
            session.Blocks.Changed += Dirty; session.Towers.Changed += Dirty;
            session.Game.StateChanged += OnState; session.Rewards.Offered += ShowRewards;
            session.Blocks.Notice += ShowNotice; session.Towers.Notice += ShowNotice;
            SetSpeed(1); Refresh();
        }

        /// v18.6: ui_counter_glow_ring's visible ring reaches 0.92 of its half size (alpha > 32 of 255, measured on the sprite).
        private const float RingVisibleFrac = .92f;
        /// Ring max scale: the art 1.5 unless its visible radius would reach the core orb's outer ring - then capped at
        /// goldRingCapPillFrac x the pill height (ref px). Layout: orb TL(24,24,150) circle, coin centre = pill (190,44) + (36,36).
        private float GoldRingMaxScale(float ringSize, float pillH)
        {
            float half = ringSize * .5f * RingVisibleFrac, orbR = 75f; var orbC = new Vector2(24 + 75, 24 + 75); var coinC = new Vector2(190 + 36, 44 + 36);
            float gap = Vector2.Distance(orbC, coinC) - orbR; const float artMax = 1.5f;
            if (half * artMax < gap) return artMax;
            return Mathf.Min(artMax, CH.goldRingCapPillFrac * pillH / half);
        }
        // ---------- state ----------
        private void Dirty() { handDirty = true; Refresh(); }
        private void Refresh()
        {
            if (session == null) return;
            hpNumber.text = session.Economy.HP.ToString();
            { var hs = session.Economy.HP + "/" + session.Economy.MaxHP; if (hpSmall.text != hs) { hpSmall.text = hs; if (hpPlate != null) hpPlate.sizeDelta = new Vector2(Mathf.Max(64f, hpSmall.GetPreferredValues(hs).x + 20f), 28f); } }
            wave.text = Loc.Wave(Mathf.Min(session.Waves.WaveIndex + 1, Mathf.Max(1, session.config.waves.Length)), session.config.waves.Length);
            var offer = session.DrawOffer; lastAdAvailable = session.AdAvailable; // ads unavailable: the ad draw is no offer (已用完); ExtraDraw makes the 2nd draw FREE
            var st = session.Blocks.Hand.IsFull ? StoneSignal.VFX.DrawPileState.Full : offer == DrawRules.Offer.Free ? StoneSignal.VFX.DrawPileState.Free : offer == DrawRules.Offer.Ad ? StoneSignal.VFX.DrawPileState.Ad : StoneSignal.VFX.DrawPileState.Used;
            if (drawPile.State != st) { drawPile.SetState(st); CapturePileBase(); }
            { var dl = Loc.DrawStatus(st); if (drawPile.statusLabel != null && drawPile.statusLabel.text != dl) drawPile.statusLabel.text = dl; } // art DrawPileUI writes English; label text is ours
            drawPile.SetStackCount(st == StoneSignal.VFX.DrawPileState.Free || st == StoneSignal.VFX.DrawPileState.Full ? 5 : st == StoneSignal.VFX.DrawPileState.Ad ? 4 : 3);
            if (session.Game.State != GameState.Build && drawPile.button.interactable) drawPile.button.interactable = false;
            SetHandCountShown();
            goldCounter.SetValue(session.Economy.Gold - session.GoldInFlight);
            battle.interactable = session.Game.State == GameState.Build;
            if (handDirty) RebuildHands();
            if (session.Game.State != GameState.Reward) { rewardPanel.SetActive(false); if (pickUi != null && !picking) pickUi.gameObject.SetActive(false); }
            overPanel.SetActive(session.Game.State == GameState.GameOver);
        }
        private void RebuildHands()
        {
            handDirty = false;
            foreach (var g in built) Destroy(g); built.Clear(); handCards.Clear(); dimSets.Clear(); handAlphaApplied = -1f; hotkeyBadges.Clear(); badgesTouch = PointerInput.TouchMode;
            var pic = PlacementInputController.Instance; if (pic != null) { pic.HandRects.Clear(); pic.HandRects.Add(handCount); } // touch "over the hand" = these rects
            bool build = session.Game.State == GameState.Build;
            var towers = session.config.towers;
            // ---- tower hand per ui_card_spec_v13: 200x220 cards, pivot bottom-centre, bottom-left anchor (24,24), spacing 212, fan +-4 deg.
            // Card-local rects below use the spec's convention: origin top-left of the card, y down.
            const float CW = 200, CH = 220;
            for (int i = 0; i < towers.Length; i++)
            {
                int idx = i; var data = towers[i];
                bool selected = session.Towers.SelectedIndex == i;
                var card = new GameObject("Tower card " + data.displayName, typeof(RectTransform)).GetComponent<RectTransform>();
                float u = towers.Length > 1 ? i / (float)(towers.Length - 1) : .5f;
                card.SetParent(towerHand, false);
                card.anchorMin = card.anchorMax = Vector2.zero; card.pivot = new Vector2(.5f, 0); card.sizeDelta = new Vector2(CW, CH);
                float arc = -Mathf.Abs(u - .5f) * 2f * 14f; // staggered fan: outer cards sit 14 px lower than the centre pair (with the +-4 deg tilt)
                card.anchoredPosition = new Vector2(24 + CW * .5f + i * 212, 24 + 14 + arc + (selected ? 12 : 0));
                card.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(4, -4, u));
                var face = Img(card, "Frame", art ? art.uiCardTower : null, art && art.uiCardTower ? Color.white : Red); Full(face);
                face.GetComponent<Image>().type = Image.Type.Sliced; // 9-slice L28 B64 R28 T28 (sprite borders from art .meta)
                face.GetComponent<Image>().raycastTarget = true;
                // v18.6 purple-tint fix: transition None BEFORE interactable. Setting interactable = false while the default ColorTint transition was
                // still on cross-faded the face's CanvasRenderer to the default disabledColor (0.78, 0.78, 0.78, 0.5) and the later "None" never undid
                // it: a 50 % red card over the blue board read purple in combat. The renderer colour is reset to white as well.
                var button = face.gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.None; button.targetGraphic = face.GetComponent<Image>();
                button.interactable = TowersBuildable; face.GetComponent<Image>().canvasRenderer.SetColor(Color.white);
                { var cp = face.gameObject.AddComponent<CardPointer>(); cp.Tower = true; cp.Index = idx; cp.Enabled = () => TowersBuildable; } // tap / drag (PlacementInputController); v18.6: towers also in combat (gold)
                { var th = face.gameObject.AddComponent<CardHover>(); th.Init(card, selected); HoverUnify(th); } // v18.6 hover unify
                if (data.icon != null) { var icon = Img(face, "Icon", data.icon, Color.white); TL(icon, 35, 15, 130, 130); icon.GetComponent<Image>().preserveAspect = true; }
                var price = Txt(face, session.Towers.Cost(data).ToString(), 36, Ink); TL(price.rectTransform, 20, 162, 130, 44);
                price.fontStyle = FontStyles.Bold; price.outlineWidth = .25f; price.outlineColor = new Color32(0x1E, 0x1A, 0x3A, 255); price.alignment = TextAlignmentOptions.Center;
                { var hot = Img(face, "Hotkey", art ? art.uiBadgeHotkey : null, Color.white); TL(hot, 152, 170, 32, 32);
                var num = Txt(hot, (i + 1).ToString(), 22, new Color32(0x1E, 0x1A, 0x3A, 255)); Full(num.rectTransform);
                hot.gameObject.SetActive(!PointerInput.TouchMode); hotkeyBadges.Add(hot.gameObject); } // 1-4 badges: desktop only (v18.2: toggled live when touch mode starts, no hand rebuild)
                var size = TowerManager.SizeOf(data, 0);
                if (size != Vector2Int.one)
                {
                    var badgeSprite = art == null ? null : size.x == 2 && size.y == 2 ? art.uiBadgeSize2x2 : art.uiBadgeSize1x2;
                    var badge = Img(face, "Size", badgeSprite, badgeSprite != null ? Color.white : Red); TL(badge, 116, -14, 76, 36); // text baked in sprite
                    if (badgeSprite == null) { var t = Txt(badge, size.x + "x" + size.y, 22, Ink); Full(t.rectTransform); }
                }
                if (selected) Outline(face, Gold, 5);
                built.Add(card.gameObject); if (pic != null) pic.HandRects.Add(card); handCards.Add((card.gameObject.AddComponent<CanvasGroup>(), true, i));
                {   // v18.6 one disabled style (unaffordable): the whole card grey 70 % + brightness 0.8, price #FF6B6B
                    var set = new DimSet { tower = true, index = i, cost = session.Towers.Cost(data), price = price, priceBase = price.color };
                    foreach (var g in card.GetComponentsInChildren<Image>(true)) set.full.Add(g.gameObject.AddComponent<UiDisable>());
                    set.k = towerDimK.TryGetValue(i, out var k0) ? k0 : -1f; dimSets.Add(set);
                }
            }
            // ---- block hand: ui_card_blueprint 128x128 (9-slice 24) + ui_icon_block_X 128x128 overlay (uniform, never per-shape scaling),
            // left-aligned on its own line above the tower hand (bottom 24+220+32) so it never overlaps the tower hand.
            // v18.4 (玩法策划 option A): groups ordered by their newest card (the newest is always the rightmost = topmost, fully visible);
            // more than FanStyle.collapseAbove groups fan into a fixed-width overlapping row (HandFanLayout), expanded by tap / hold.
            var groups = session.Blocks.Hand.Groups();
            {
                var hc = session.Blocks.Hand.Cards; var hr = session.Blocks.Hand.Runes; var last = new Dictionary<int, int>();
                for (int gi = 0; gi < groups.Count; gi++) { int l = groups[gi].first; for (int i = 0; i < hc.Count; i++) if (hc[i] == groups[gi].shape && hr[i] == groups[gi].rune) l = i; last[groups[gi].first] = l; }
                groups.Sort((x, y) => last[x.first].CompareTo(last[y.first]));
            }
            bool anyStack = groups.Exists(g => g.count > 1);
            // v16: card w = min(150, budget/7 - 12), budget = safeWidth - 24 - 512 - 24 (DRAW + BATTLE block on the right).
            float safeW = ((RectTransform)blockHand.parent).rect.width; if (safeW <= 0) safeW = 1920;
            float cardW = Mathf.Max(88, Mathf.Min(150, (safeW - 24 - 512 - 24) / 7f - 12));
            float sc = cardW / 128f, stepPx = (cardW + 12) * (anyStack ? 150f / 140f : 1f), left = 24, rowY = 24 + CH + 32;
            var fs = FanStyle; handCountY = rowY + cardW + fs.handCountGapPx; // art v18.4: was +8 (overlapped a selected left card's rune badge)
            handCount.anchoredPosition = new Vector2(24, handCountY); handCardW = cardW; handRowY = rowY; handSafeW = safeW;
            if (PlacementInputController.Instance != null) { var cv = blockHand.GetComponentInParent<Canvas>(); float k = cv ? cv.scaleFactor : 1; PlacementInputController.Instance.CanvasScale = k; PlacementInputController.Instance.HandTop = (rowY + cardW + 8) * k; }
            if (groups.Count > fs.collapseAbove && fs.fixedRowIgnoresStackSpacing) stepPx = cardW + 12; // art v18.4: fanned row always rowSlots wide
            var row = HandFanLayout.Compute(groups.Count, cardW, stepPx, safeW - 24 - 512 - 24 - left, fs, FanNeedExposed(cardW), anyStack && fs.stackTopNeverCovered ? FanStackTopStep(cardW, groups) : 0f);
            // art v18.4: remember where every group was drawn (relayout / enter / xN bump after this rebuild)
            var prevPose = new Dictionary<string, (Vector2 c, float a, int count, float lift)>();
            for (int pi = 0; pi < fanPose.Count && pi < fanCards.Count; pi++) if (fanCards[pi].holder != null) prevPose[fanPose[pi].key] = (fanPose[pi].lastC, fanPose[pi].lastA, fanPose[pi].count, fanPose[pi].lastLift);
            bool firstBuild = !fanEverBuilt; fanEverBuilt = true; fanPose.Clear(); float nowU = Time.unscaledTime;
            if (!row.fanned) { fanTarget = false; fanT = 0f; }
            fanActive = row.fanned; fanRow = row; fanCards.Clear(); fanTApplied = -1f; fanPressMask = -1;
            if (pic != null) { pic.CardTapHook = FanTap; pic.CardHeldHook = FanHeld; }
            int selectedIndex = session.Blocks.Hand.Selected;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var grp = groups[gi]; int idx = grp.first; var shape = grp.shape;
                bool selected = session.Towers.SelectedIndex < 0 && session.Blocks.Hand.Cards.Count > 0 && session.Blocks.Hand.Cards[selectedIndex] == shape && session.Blocks.Hand.Runes[selectedIndex] == grp.rune;
                var holder = Group(blockHand, "Block card " + shape.displayName); holder.anchorMin = holder.anchorMax = Vector2.zero; holder.pivot = Vector2.zero;
                holder.sizeDelta = new Vector2(128, 128); holder.localScale = Vector3.one * sc;
                float y = rowY + (selected ? SelectLiftPx : 0);
                // art v18.4 pose: bottom-centre point + z tilt (holder pivot stays bottom-left; FanCorner turns it into anchoredPosition)
                float fu = groups.Count > 1 ? 2f * gi / (groups.Count - 1) - 1f : 0f, arc = 1f - fu * fu;
                var c0 = new Vector2(left + cardW * .5f + gi * row.collapsedStep, y + (row.fanned ? fs.arcSagPx * arc : 0f));
                var c1 = new Vector2(left + cardW * .5f + gi * row.expandedStep, y + (row.fanned ? fs.expandedLiftPx + fs.expandedArcSagPx * arc : 0f));
                float a0 = row.fanned ? -fs.tiltDeg * fu : 0f, a1 = row.fanned ? -fs.expandedTiltDeg * fu : 0f;
                var pc = FanCorner(c0, a0, cardW); var pe = FanCorner(c1, a1, cardW);
                holder.anchoredPosition = Vector2.Lerp(pc, pe, FanEase());
                var pose = new FanPose { key = shape.name + "|" + grp.rune, c0 = c0, c1 = c1, a0 = a0, a1 = a1, w = cardW, selected = selected, count = grp.count, introStart = -1f, lift = selected ? SelectLiftPx : 0f };
                pose.lastLift = pose.lift;
                if (grp.count > 1 && S("ui_card_blueprint_stack") != null) { var st = Img(holder, "Stack", S("ui_card_blueprint_stack"), Color.white); BL(st, 0, -40, 160, 168); }
                var card = Img(holder, "Card", art ? art.uiCardBlueprint : null, art && art.uiCardBlueprint ? Color.white : Blueprint); Full(card);
                card.GetComponent<Image>().type = Image.Type.Sliced; card.GetComponent<Image>().raycastTarget = true;
                pose.restShadow = row.fanned && gi > 0 && fs.shadowColor.a > 0f;
                if (pose.restShadow) { var sh = card.gameObject.AddComponent<UnityEngine.UI.Shadow>(); sh.effectColor = fs.shadowColor; sh.effectDistance = fs.shadowOffset; pose.shadow = sh; } // art v18.4: the leftmost card covers nothing -> no shadow
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card.GetComponent<Image>(); b.transition = Selectable.Transition.None;
                b.interactable = session.Blocks.PhaseAllows; // v18.6: Build only (allowCombatBlocks false)
                {   // v18.6 (玩法策划): in combat the card can't be dragged (press: no select / no move; drag start: toast 战斗中不能放墙), tap still expands the fan
                    var cp = card.gameObject.AddComponent<CardPointer>(); cp.Tower = false; cp.Index = idx; cp.Enabled = () => session.Blocks.PhaseAllows;
                    cp.Blocked = () => !session.Blocks.PhaseAllows && session.Game.State == GameState.Combat;
                }
                var hover = card.gameObject.AddComponent<CardHover>(); hover.Init(holder, selected); HoverUnify(hover); hover.Active = () => session.Blocks.PhaseAllows; // v18.5b split hover/press; v18.6 also unfanned rows
                var iconSprite = BlockIcon(shape);
                if (iconSprite != null) { var ic = Img(card, "Shape", iconSprite, Color.white); Full(ic); }
                else
                {
                    // fallback: 22px cells on a 4x4 grid (88x88 area), same scale for every shape
                    var cells = shape.cells;
                    if (cells != null && cells.Length > 0)
                    {
                        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                        foreach (var c in cells) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
                        Vector2 off = new Vector2((maxX + minX) * .5f, (maxY + minY) * .5f);
                        foreach (var c in cells) { var cell = Img(card, "Cell", null, new Color(.86f, .93f, 1f)); Center(cell, (c.x - off.x) * 22, -(c.y - off.y) * 22, 20, 20); }
                    }
                }
                if (grp.rune != RuneRules.NoRune)
                {
                    // rune carried by this block: corner badge + rune icon (top-left)
                    var corner = Img(card, "Rune badge", S("ui_rune_badge_corner"), Color.white); TL(corner, -10, -10, 48, 48);
                    var ri = Img(corner, "Rune", S(StoneSignal.VFX.RuneArt.Icon[grp.rune]), Color.white); Center(ri, 0, 0, 34, 34);
                }
                if (grp.count > 1)
                {
                    var badge = Img(card, "Count", S("ui9_badge_count") ?? Rounded, S("ui9_badge_count") != null ? Color.white : Navy);
                    if (row.fanned) TL(badge, fs.fanCountBadgePos.x, fs.fanCountBadgePos.y, 56, 40); else TL(badge, 84, -12, 56, 40); // fanned: on the exposed left strip (never covered)
                    badge.GetComponent<Image>().type = Image.Type.Sliced; pose.badge = badge;
                    var n = Txt(badge, "\u00D7" + grp.count, 26, Ink); Full(n.rectTransform); n.fontStyle = FontStyles.Bold; n.outlineWidth = .25f; n.outlineColor = Navy;
                }
                if (selected) Outline(card, Gold, 4);
                built.Add(holder.gameObject); if (pic != null) pic.HandRects.Add(holder); handCards.Add((holder.gameObject.AddComponent<CanvasGroup>(), false, idx));
                {   // v18.6 one disabled style (combat): card grey 70 % + brightness 0.8; xN / rune badges brightness 0.85 only
                    var set = new DimSet { tower = false, index = idx, k = blockDimK };
                    foreach (var g in holder.GetComponentsInChildren<Image>(true))
                    {
                        bool badge = (pose.badge != null && g.transform.IsChildOf(pose.badge)) || g.name == "Rune badge" || g.name == "Rune";
                        (badge ? set.badge : set.full).Add(g.gameObject.AddComponent<UiDisable>());
                    }
                    if (pose.badge != null) foreach (var t in pose.badge.GetComponentsInChildren<TMP_Text>(true)) set.badgeText.Add((t, t.color));
                    dimSets.Add(set);
                }
                fanCards.Add((holder, hover, card.GetComponent<Image>(), pc, pe, idx));
                // art v18.4 intro: known group -> slide from its old pose; new group -> enter; grown stack -> xN bump
                if (prevPose.TryGetValue(pose.key, out var old))
                {
                    // v18.5b: the selected-card lift has its own channel (selectLiftSeconds / selectLiftEase); a slot change keeps relayoutSeconds
                    var target = pose.Pose(FanEase()); var from = old.c + new Vector2(0f, pose.lift - old.lift);
                    if ((from - target.c).sqrMagnitude > .25f || Mathf.Abs(old.a - target.a) > .05f) { pose.fromC = from; pose.fromA = old.a; pose.fromScale = 1f; pose.introStart = nowU; pose.introDur = fs.relayoutSeconds; }
                    if (Mathf.Abs(old.lift - pose.lift) > .01f && fs.selectLiftSeconds > 0f) { pose.liftFrom = old.lift; pose.liftStart = nowU; pose.lastLift = old.lift; }
                    if (grp.count > old.count && pose.badge != null)
                    {
                        pose.bumpStart = nowU;
                        if (pose.introStart >= 0f && fs.mergeFlyShadow && fs.shadowColor.a > 0f)
                        {   // v18.5b: the merging stack flies on top carrying the fan shadow (also unfanned / expanded, where it rests without one)
                            pose.flyShadow = true;
                            if (pose.shadow == null) { var sh = card.gameObject.AddComponent<UnityEngine.UI.Shadow>(); sh.effectColor = Color.clear; sh.effectDistance = fs.shadowOffset; pose.shadow = sh; }
                        }
                    }
                }
                else if (!firstBuild) { var target = pose.Pose(FanEase()); pose.fromC = target.c + fs.enterOffsetPx; pose.fromA = target.a; pose.fromScale = fs.enterScaleFrom; pose.introStart = nowU; pose.introDur = fs.enterSeconds; pose.enter = true; }
                pose.baseScale = sc; fanPose.Add(pose);
            }
            // v18.5b: several groups entering together (whole-hand redeal) enter left to right, interval = min(stagger, (max - enter) / (n - 1))
            fanRebuildTime = nowU; fanRedealInterval = 0f; fanRedealCount = 0;
            {
                int ne = 0; foreach (var p in fanPose) if (p.enter) ne++;
                if (ne >= 2 && fs.redealStaggerSeconds > 0f)
                {
                    float iv = HandFanLayout.RedealInterval(ne, fs); int j = 0;
                    foreach (var p in fanPose) if (p.enter) p.introStart = nowU + iv * j++;
                    fanRedealInterval = iv; fanRedealCount = ne;
                }
            }
            {   // v18.6: every hand Image shares the UiDisable material (identity at uv1 = 0) -> the hand keeps batching (+0 DC)
                var m = UiDisable.SharedMaterial;
                if (m != null) { foreach (var g in towerHand.GetComponentsInChildren<Image>(true)) g.material = m; foreach (var g in blockHand.GetComponentsInChildren<Image>(true)) g.material = m; }
            }
            ApplyDims(true);
            ApplyFan();
        }
        // ---- v18.4 block-row fan ----------------------------------------------------------------------------------------------------
        private HandFanStyle FanStyle => session != null && session.config != null && session.config.handFan != null ? session.config.handFan : defaultFan;
        private static readonly HandFanStyle defaultFan = new HandFanStyle();
        private bool fanActive, fanTarget; private float fanT, fanTApplied = -1f, fanLastInput; private int fanDownSeen; private float handSafeW = 1920;
        private HandFanLayout.Row fanRow;
        // ---- art v18.4: per-card pose (bottom-centre + tilt), stagger, intro (relayout / enter), xN bump, press un-dim, pill rise
        private sealed class FanPose
        {
            public string key; public Vector2 c0, c1; public float a0, a1, w, baseScale = 1f; public bool selected, enter; public int count;
            public UnityEngine.UI.Shadow shadow; public RectTransform badge;
            public float lift, liftFrom, liftStart = -1f, lastLift; public bool restShadow, flyShadow; // v18.5b
            public Vector2 fromC, lastC; public float fromA, lastA, fromScale = 1f, introStart = -1f, introDur, bumpStart = -1f;
            public (Vector2 c, float a) Pose(float e) => (Vector2.Lerp(c0, c1, e), Mathf.Lerp(a0, a1, e));
        }
        private readonly List<FanPose> fanPose = new List<FanPose>();
        private int fanPressMask = -1; private float handCountY = 434; private bool fanEverBuilt;
        /// bottom-centre c + tilt a (deg) -> anchoredPosition of a bottom-left-pivot holder of width w.
        private static Vector2 FanCorner(Vector2 c, float a, float w) { float r = a * Mathf.Deg2Rad; return c + new Vector2(-Mathf.Cos(r), -Mathf.Sin(r)) * (w * .5f); }
        private static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        private static float EaseOutBack(float t) { t = Mathf.Clamp01(t) - 1f; const float k = 1.70158f; return 1f + t * t * ((k + 1f) * t + k); }
        /// Per-card fan progress with stagger (rightmost first). expandSeconds / collapseSeconds stay the total duration.
        private float FanEaseFor(int i, int n)
        {
            var fs = FanStyle; float total = Mathf.Max(1e-4f, fanTarget ? fs.expandSeconds : fs.collapseSeconds);
            float st = n > 1 ? Mathf.Min(fs.staggerSeconds, total * .5f / (n - 1)) : 0f, per = total - st * (n - 1);
            float elapsed = (fanTarget ? fanT : 1f - fanT) * total, s = Mathf.Clamp01((elapsed - (n - 1 - i) * st) / per);
            return fanTarget ? fs.expandEase.Evaluate(s) : 1f - fs.collapseEase.Evaluate(s);
        }
        // v18.5 review fix: active until ApplyFan has drawn the final frame (k >= 1) and cleared the marker - the time-window test skipped the last
        // frame, so a card could stay a few px / % short of its slot (frame-rate dependent: up to ~1.6 px and 0.5 % scale at 30 fps on the 0.2 s enter).
        private bool FanIntroActive() { foreach (var p in fanPose) if (p.introStart >= 0f || p.bumpStart >= 0f || p.liftStart >= 0f) return true; return false; }
        private const float SelectLiftPx = 12f; private float fanRebuildTime, fanRedealInterval; private int fanRedealCount;
        /// v18.5b (art point 2): expanded step that keeps a xN stack's top card clear of the next card, ref px. The next card reaches left of
        /// its own edge by its rune badge (10 card units) and, when it is a stack too, by its fanned xN badge (-fanCountBadgePos.x = 12).
        private float FanStackTopStep(float cardW, List<BlockHandManager.Group> groups)
        {
            var fs = FanStyle; float reach = 10f;
            for (int i = 0; i + 1 < groups.Count; i++) if (groups[i].count > 1 && groups[i + 1].count > 1) reach = Mathf.Max(reach, -fs.fanCountBadgePos.x);
            return cardW + reach * cardW / 128f + fs.stackTopClearancePx;
        }
        private int FanPressMask() { int m = 0; for (int i = 0; i < fanCards.Count && i < 31; i++) if (fanCards[i].hover != null && fanCards[i].hover.Over) m |= 1 << i; return m; }
        private readonly List<(RectTransform holder, CardHover hover, Image card, Vector2 collapsed, Vector2 expanded, int index)> fanCards = new List<(RectTransform, CardHover, Image, Vector2, Vector2, int)>();
        /// Exposed strip a covered card needs so its rune badge (card x -10..38) and fanned xN badge stay clear of the next card, whose
        /// own rune badge reaches 10 card units left of it (ref px).
        private float FanNeedExposed(float cardW) { var fs = FanStyle; return (Mathf.Max(38f, fs.fanCountBadgePos.x + 56f) + 10f) * cardW / 128f + 2f; }
        private float FanEase() => FanEaseAt(fanT, fanTarget);
        private float FanEaseAt(float t, bool target) { var fs = FanStyle; t = Mathf.Clamp01(t); return target ? fs.expandEase.Evaluate(t) : 1f - fs.collapseEase.Evaluate(1f - t); }
        /// v18.5 review fix: progress t at which the (new) direction's curve gives eased position e (both curves rise monotonically). Reversing a
        /// non-linear ease mid-animation used to jump (EaseOutCubic: expanding at t 0.3 = 0.66 -> collapsing at t 0.3 = 0.03); now it continues from where it is.
        private float FanTFor(float e) { float lo = 0f, hi = 1f; for (int it = 0; it < 20; it++) { float m = (lo + hi) * .5f; if (FanEaseAt(m, fanTarget) < e) lo = m; else hi = m; } return (lo + hi) * .5f; }
        private void ApplyFan()
        {
            fanTApplied = fanT; var fs = FanStyle; float dim = fs.coveredBrightness, now = Time.unscaledTime; int n = fanCards.Count; fanPressMask = FanPressMask();
            for (int i = 0; i < n; i++)
            {
                var f = fanCards[i]; if (f.holder == null) { if (i < fanPose.Count) fanPose[i].introStart = fanPose[i].bumpStart = fanPose[i].liftStart = -1f; continue; }
                if (i >= fanPose.Count) { var p0 = Vector2.Lerp(f.collapsed, f.expanded, FanEase()); if (f.hover != null) f.hover.SetRest(p0); else f.holder.anchoredPosition = p0; continue; }
                var ps = fanPose[i]; float e = FanEaseFor(i, n); var (c, a) = ps.Pose(e); float scale = 1f, introK = 1f;
                if (ps.introStart >= 0f)
                {
                    if (now < ps.introStart) { f.holder.localScale = Vector3.zero; ps.lastC = c; ps.lastA = a; continue; } // v18.5b redeal: waits hidden for its turn
                    float k = ps.introDur <= 0f ? 1f : (now - ps.introStart) / ps.introDur; introK = k;
                    if (k >= 1f) ps.introStart = -1f;
                    else { float q = ps.enter ? EaseOutBack(k) : EaseOutCubic(k); c = Vector2.LerpUnclamped(ps.fromC, c, q); a = Mathf.LerpUnclamped(ps.fromA, a, q); scale = Mathf.LerpUnclamped(ps.fromScale, 1f, q); }
                }
                {   // v18.5b: selected-card lift channel
                    float lc = ps.lift;
                    if (ps.liftStart >= 0f) { float kl = fs.selectLiftSeconds <= 0f ? 1f : (now - ps.liftStart) / fs.selectLiftSeconds; if (kl >= 1f) ps.liftStart = -1f; else lc = Mathf.LerpUnclamped(ps.liftFrom, ps.lift, fs.selectLiftEase.Evaluate(Mathf.Clamp01(kl))); }
                    c.y += lc - ps.lift; ps.lastLift = lc;
                }
                ps.lastC = c; ps.lastA = a;
                var bl = FanCorner(c, a, ps.w);
                f.holder.localRotation = Quaternion.Euler(0f, 0f, a);
                if (!(f.hover != null && f.hover.Raised)) f.holder.localScale = Vector3.one * ps.baseScale * scale;
                if (f.hover != null) f.hover.SetRest(bl); else f.holder.anchoredPosition = bl;
                if (dim < 1f && f.card != null)
                {   // covered cards dim in the collapsed fan; the newest (rightmost), the selected and the pressed card stay at 1
                    bool covered = fanActive && i < n - 1 && !ps.selected && !(f.hover != null && f.hover.Over);
                    float v = covered ? Mathf.Lerp(dim, 1f, e) : 1f; f.card.color = new Color(v, v, v, f.card.color.a);
                }
                if (ps.shadow != null)
                {   // shadow only while collapsed; v18.5b: a merging stack keeps the full shadow in flight, blending to its resting shadow over the last 40 %
                    var sc = fs.shadowColor; float restA = ps.restShadow ? sc.a * (1f - e) : 0f, sa = restA;
                    if (ps.flyShadow) { if (ps.introStart >= 0f) sa = Mathf.Lerp(sc.a, restA, Mathf.SmoothStep(0f, 1f, (introK - .6f) / .4f)); else ps.flyShadow = false; }
                    sc.a = sa; ps.shadow.effectColor = sc;
                }
                if (ps.badge != null)
                {
                    float b = 1f;
                    if (ps.bumpStart >= 0f) { float k = fs.countBumpSeconds <= 0f ? 1f : (now - ps.bumpStart) / fs.countBumpSeconds; if (k >= 1f) ps.bumpStart = -1f; else b = Mathf.Lerp(fs.countBumpScale, 1f, EaseOutCubic(k)); }
                    ps.badge.localScale = Vector3.one * b;
                }
            }
            if (handCount != null) handCount.anchoredPosition = new Vector2(24, handCountY + (fanActive ? fs.expandedLiftPx * FanEase() : 0f)); // the 7/7 pill rises with the expanded row
        }
        private void SetFan(bool on, string why) { if (fanTarget == on) return; float e0 = FanEase(); bool mid = fanT > 0f && fanT < 1f; fanTarget = on; if (mid) fanT = FanTFor(e0); fanLastInput = Time.unscaledTime; FanLastEvent = (on ? "expand: " : "collapse: ") + why; FanEvents++; }
        private bool FanTap(bool tower, int index) { if (tower || !fanActive || fanTarget) return false; SetFan(true, "tap"); return true; }
        private bool FanHeld(bool tower, int index, float held) { if (tower || !fanActive || fanTarget || held < FanStyle.holdExpandSeconds) return false; SetFan(true, "hold " + held.ToString("F2") + " s"); return true; }
        private bool OverFan(Vector2 screen) { foreach (var f in fanCards) if (f.holder != null && RectTransformUtility.RectangleContainsScreenPoint(f.holder, screen, null)) return true; return false; }
        private void UpdateFan()
        {
            bool intro = FanIntroActive(); // art v18.4: relayout / enter / bump also run for unfanned rows
            if (!fanActive) { if (intro || fanTApplied != fanT) ApplyFan(); return; }
            var pic = PlacementInputController.Instance; float now = Time.unscaledTime;
            if (pic != null)
            {
                fanLastInput = Mathf.Max(fanLastInput, pic.LastInputTime);
                if (pic.DownSerial != fanDownSeen) { fanDownSeen = pic.DownSerial; if (fanTarget && !OverFan(pic.DownPos)) SetFan(false, "tap on empty space"); }
                if (fanTarget && pic.Machine.State == PlacementState.Dragging) SetFan(false, "drag start");
            }
            if (fanTarget && now - fanLastInput >= FanStyle.idleCollapseSeconds) SetFan(false, "idle " + FanStyle.idleCollapseSeconds + " s");
            var fs = FanStyle; float dur = fanTarget ? fs.expandSeconds : fs.collapseSeconds;
            fanT = dur <= 0f ? (fanTarget ? 1f : 0f) : Mathf.MoveTowards(fanT, fanTarget ? 1f : 0f, Time.unscaledDeltaTime / dur);
            if (fanT != fanTApplied || intro || FanPressMask() != fanPressMask) ApplyFan();
        }
        /// Diagnostics / tests.
        public bool FanActive => fanActive; public bool FanExpanded => fanTarget; public float FanProgress => fanT;
        public string FanLastEvent { get; private set; } = ""; public int FanEvents { get; private set; }
        public HandFanLayout.Row FanRow => fanRow;
        public void DebugSetFan(bool on) => SetFan(on, "debug");
        /// v18.5b diagnostics: last hand rebuild time (unscaled), redeal interval / entering groups of that rebuild, merge fly shadow alpha per group.
        public float DebugRebuildTime => fanRebuildTime; public float DebugRedealInterval => fanRedealInterval; public int DebugRedealCount => fanRedealCount;
        public bool DebugFlyShadow(int i) => i >= 0 && i < fanPose.Count && fanPose[i].flyShadow;
        /// v18.5 diagnostics: HP plate / small text, hand-count pill, hand groups (draw order), per-card pose data.
        public RectTransform DebugHpPlate => hpPlate; public TextMeshProUGUI DebugHpSmall => hpSmall; public RectTransform DebugHandCount => handCount;
        public RectTransform DebugBlockHand => blockHand; public RectTransform DebugTowerHand => towerHand; public float DebugHandRowY => handRowY; public float DebugHandCardW => handCardW;
        /// Block cards in display order (left -> right = bottom -> top).
        public List<(RectTransform holder, int index)> FanCards() { var l = new List<(RectTransform, int)>(); foreach (var f in fanCards) if (f.holder != null) l.Add((f.holder, f.index)); return l; }
        /// Width of the block row reserved by the camera fit for `cards` slots (all different, collapsed fan above collapseAbove), ref px.
        private float BlockRowWidth(int cards)
        {
            float step = handCardW + 12; if (cards <= FanStyle.collapseAbove) return cards * step - 12;
            var r = HandFanLayout.Compute(cards, handCardW, step, handSafeW - 24 - 512 - 24 - 24, FanStyle, FanNeedExposed(handCardW)); return r.collapsedWidth;
        }
        // ui_icon_block_<T|L|J|S|Z|O|I>: resolved from the shape asset/display name (e.g. "Block_T", "T piece").
        private Sprite BlockIcon(BlockShapeData shape)
        {
            foreach (var n in new[] { shape.name, shape.displayName })
            {
                if (string.IsNullOrEmpty(n)) continue;
                foreach (var token in n.Split('_', ' ', '-'))
                    if (token.Length == 1) { var sp = S("ui_icon_block_" + token.ToUpperInvariant()); if (sp != null) return sp; }
                var last = S("ui_icon_block_" + char.ToUpperInvariant(n[n.Length - 1])); if (last != null) return last;
            }
            return null;
        }
        private void SetSpeed(int index)
        {
            if (index == 0) paused = !paused; else { paused = false; speedIndex = index; }
            TimeController.SetSpeed(speedIndex); TimeController.SetPaused(paused);
            for (int i = 0; i < 4; i++)
            {
                bool on = i == 0 ? paused : !paused && i == speedIndex; var img = speedButtons[i].GetComponent<Image>();
                var sel = S("ui9_button_navy_selected"); var normal = S("ui9_button_navy_normal");
                if (sel != null && normal != null) { img.sprite = on ? sel : normal; img.color = Color.white; } else img.color = on ? Blue : Slate;
            }
        }
        private void CycleTarget() { session.Enemies.Targeting = (TargetMode)(((int)session.Enemies.Targeting + 1) % Enum.GetValues(typeof(TargetMode)).Length); Refresh(); }
        private void OnState(GameState state) { handDirty = true; noticeUntil = 0; HudPhase(state); Refresh(); } // a stale placement notice never carries into reward / game over
        private void ShowNotice(string message)
        {
            var text = Loc.Notice(message); if (string.IsNullOrEmpty(text)) return; float now = Time.unscaledTime;
            if (text == notice && now - noticeShownAt < NoticeRepeatGap) return; // same text within 1 s of its last show: not re-shown
            notice = text; noticeShownAt = now; noticeUntil = now + NoticeSeconds; noticeDirty = true;   // replaces any toast on screen
            noticeFx?.Restart();   // v17.6: pop-in restarts for every shown toast (the ignored repeat above does not restart it)
        }
        /// Diagnostics: route a raw notice through the toast rules (same path as BlockPlacementManager / TowerManager notices).
        public void DebugNotice(string message) => ShowNotice(message);
        /// Picks the toast slot with the most clearance from the core and the route arrows (first slot with >= 48 ref px wins).
        // v18.3 toast anchor (art): inside SafeAreaRoot, top-centre, ToastBelowBanner ref px below the wave banner's bottom edge, every aspect.
        // Safety guard (玩法策划 v18.4): protects ALL three bridge landings - East, West and South (grid.Spawns; there is no north bridge):
        // if the toast rect would cover any landing cell (screen-projected top quad + ToastGuardMargin) it falls back to the top-right slot.
        private const float ToastBelowBanner = 12f, ToastGuardMargin = 12f, ToastFallbackRight = 24f;
        public bool ToastGuardTriggered { get; private set; }
        private void PlaceNotice()
        {
            var cv = noticePill.GetComponentInParent<Canvas>(); float k = cv != null && cv.scaleFactor > 0 ? cv.scaleFactor : 1f;
            float w = noticePill.sizeDelta.x, h = noticePill.sizeDelta.y;
            float y = (hudBanner != null ? -hudBanner.anchoredPosition.y + hudBanner.sizeDelta.y * hudBanner.pivot.y : 116f) + ToastBelowBanner;
            var parent = (RectTransform)noticePill.parent; var pc = new Vector3[4]; parent.GetWorldCorners(pc); // the parent is active: its corners are current
            Rect ToastRect(bool right) => right ? Rect.MinMaxRect(pc[2].x - (ToastFallbackRight + w) * k, pc[2].y - (y + h) * k, pc[2].x - ToastFallbackRight * k, pc[2].y - y * k)
                                             : Rect.MinMaxRect((pc[0].x + pc[2].x) * .5f - w * k * .5f, pc[2].y - (y + h) * k, (pc[0].x + pc[2].x) * .5f + w * k * .5f, pc[2].y - y * k);
            var r = ToastRect(false); float clear = float.MaxValue; string hit = "";
            var cam = session.viewCamera != null ? session.viewCamera : Camera.main; var grid = session.grid;
            if (cam != null && grid != null)
                foreach (var sp in grid.Spawns)
                {
                    var q = LandingRect(cam, grid, sp, ToastGuardMargin * k);
                    float dx = Mathf.Max(0, Mathf.Max(q.xMin - r.xMax, r.xMin - q.xMax)), dy = Mathf.Max(0, Mathf.Max(q.yMin - r.yMax, r.yMin - q.yMax));
                    float d = Mathf.Max(dx, dy); if (r.Overlaps(q)) { d = -1; hit += " " + sp; } clear = Mathf.Min(clear, d);
                }
            ToastGuardTriggered = hit.Length > 0;
            if (ToastGuardTriggered) { TR(noticePill, -ToastFallbackRight, y, w, h); r = ToastRect(true); }
            else TC(noticePill, 0, y, w, h);
            noticeInfo = "notice '" + notice + "' anchor=" + (ToastGuardTriggered ? "TOP-RIGHT fallback (guard: toast would cover landing" + hit + ")" : "banner+" + ToastBelowBanner + " top-centre") +
                         " y=" + y + " ref px, rect=" + r + " (scale " + k.ToString("F2") + "), clearance to nearest landing cell (+" + ToastGuardMargin + " px margin) " + (clear == float.MaxValue ? "-" : clear.ToString("F0") + " px");
        }
        /// Screen rect of an entry's landing cell (top quad), grown by pad pixels.
        public static Rect LandingRect(Camera cam, GridManager grid, Vector2Int cell, float pad)
        {
            float cs = grid.cellSize, top = grid.transform.position.y + (grid.tileTop > 0 ? grid.tileTop : .25f); var o = grid.transform.position;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var wp = new Vector3(o.x + (cell.x + (i & 1)) * cs, top, o.z + (cell.y + (i >> 1)) * cs); Vector2 s = cam.WorldToScreenPoint(wp);
                x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
            }
            return Rect.MinMaxRect(x0 - pad, y0 - pad, x1 + pad, y1 + pad);
        }
        // ---- v18.2 camera fit: HUD rects (screen px, y up) the board / entry bridges must stay clear of (CameraFit).
        /// True once the HUD and the hand have been laid out (rects are valid).
        public bool HudReady => towerHand != null && blockHand != null && built.Count > 0;
        /// Fixed HUD obstacles: top bar (core orb + HP, gold, wave banner, speed buttons), bottom-left hand (4 tower cards, the hand counter
        /// and a block row of FitBlockCards cards - the draw size, so the camera never moves when cards are drawn or played),
        /// bottom-right draw pile + BATTLE. Padded by padPx (screen pixels).
        public const int FitBlockCards = GameBootstrap.CardsPerDraw;
        public void HudObstacles(List<Rect> list, float padPx) => HudObstacles(list, padPx, FitBlockCards, false);
        /// blockCards: width of the reserved block row in cards (FitBlockCards for the accepted Screen fit, BlockHandManager.MaxCards = 7 for the
        /// worst-case coverage check / HudFree). deep: each element's rect also covers its visible children (tower-card 2x2 badge, the draw
        /// pile's 再抽 pill, rune shields on block cards) - used by the v18.3 coverage check and the HudFree fit, not by the accepted Screen fit.
        public void HudObstacles(List<Rect> list, float padPx, int blockCards, bool deep)
        {
            list.Clear(); var c = new Vector3[4];
            void Grow(ref float x0, ref float y0, ref float x1, ref float y1, RectTransform r)
            {
                r.GetWorldCorners(c);
                for (int i = 0; i < 4; i++) { x0 = Mathf.Min(x0, c[i].x); y0 = Mathf.Min(y0, c[i].y); x1 = Mathf.Max(x1, c[i].x); y1 = Mathf.Max(y1, c[i].y); }
            }
            void Add(RectTransform r)
            {
                if (r == null || !r.gameObject.activeInHierarchy) return;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; Grow(ref x0, ref y0, ref x1, ref y1, r);
                if (deep) foreach (var g in r.GetComponentsInChildren<UnityEngine.UI.Graphic>(false)) if (g.enabled && g.color.a > .05f) Grow(ref x0, ref y0, ref x1, ref y1, g.rectTransform);
                list.Add(Rect.MinMaxRect(x0 - padPx, y0 - padPx, x1 + padPx, y1 + padPx));
            }
            Add(hudOrb); Add(hpSmall != null ? hpSmall.rectTransform : null); Add(hpPlate); Add(goldPill); Add(hudBanner);
            foreach (var b in speedButtons) if (b != null) Add((RectTransform)b.transform);
            if (battle != null) Add((RectTransform)battle.transform);
            if (drawPile != null) Add((RectTransform)drawPile.transform);
            Add(handCount);
            foreach (var h in handCards) if (h.tower && h.cg != null) Add((RectTransform)h.cg.transform);
            if (blockHand != null)
            {   // block row at a fixed card count (left-aligned at x 24, rowY; +12 px selected lift; deep: +20 px rune shield above the card)
                blockHand.GetWorldCorners(c); var cv = blockHand.GetComponentInParent<Canvas>(); float k = cv ? cv.scaleFactor : 1f;
                float x0 = c[0].x + 24 * k, y0 = c[0].y + handRowY * k, w = BlockRowWidth(blockCards) * k, h = (handCardW + 12 + (deep ? 20 : 0)) * k;
                if (blockCards > FanStyle.collapseAbove && FanStyle.tiltDeg > 0f)
                {   // art v18.4: the tilted outer cards reach past the flat row - left: the leftmost card's rune badge (TL -10,-10 at 138 card units up),
                    // right: the rightmost card's top-right corner, bottom: the leftmost card's bottom-left corner (3.9 / 20.1 / 7.7 ref px at 3 deg, card 150)
                    float t = FanStyle.tiltDeg * Mathf.Deg2Rad, s = handCardW / 128f, hw = handCardW * .5f;
                    float padL = Mathf.Max(0f, (hw + 10f * s) * Mathf.Cos(t) + 138f * s * Mathf.Sin(t) - hw), padR = Mathf.Max(0f, hw * Mathf.Cos(t) + handCardW * Mathf.Sin(t) - hw), padB = hw * Mathf.Sin(t);
                    x0 -= padL * k; w += (padL + padR) * k; y0 -= padB * k; h += padB * k;
                }
                list.Add(Rect.MinMaxRect(x0 - padPx, y0 - padPx, x0 + w + padPx, y0 + h + padPx));
            }
        }
        /// v18.3 HudFree experiment (art): the HUD as blocks - top banner band (topBandPx x HUD scale, full width), the bottom-left hand group
        /// (bounding rect of the 7-card block row + tower cards + hand counter) and the bottom-right group (draw pile + BATTLE), plus every
        /// individual element (the core orb / HP reach below the band). Screen px, padded.
        /// v18.4: the block row is the collapsed fan of 7 different cards (BlockRowWidth). groupBlocks = false: the hand / right groups are not
        /// merged into bounding blocks (each element exact: the open area above the outer tower cards is usable).
        public void HudFreeObstacles(List<Rect> list, float padPx, float topBandPx, bool groupBlocks = true)
        {
            HudObstacles(list, padPx, BlockHandManager.MaxCards, true);
            var cv = blockHand != null ? blockHand.GetComponentInParent<Canvas>() : null; float k = cv ? cv.scaleFactor : 1f; var safe = HudScaler.SafeArea;
            list.Add(Rect.MinMaxRect(safe.xMin - padPx, safe.yMax - topBandPx * k - padPx, safe.xMax + padPx, safe.yMax + padPx));
            if (!groupBlocks) return;
            var tmp = new List<Rect>(); HudObstacles(tmp, padPx, BlockHandManager.MaxCards, true);
            Rect? hand = null, right = null; float mid = (safe.xMin + safe.xMax) * .5f, low = safe.yMin + safe.height * .5f;
            foreach (var r in tmp)
            {
                if (r.yMax > low) continue;                       // top bar elements
                if (r.center.x < mid) hand = hand.HasValue ? Rect.MinMaxRect(Mathf.Min(hand.Value.xMin, r.xMin), Mathf.Min(hand.Value.yMin, r.yMin), Mathf.Max(hand.Value.xMax, r.xMax), Mathf.Max(hand.Value.yMax, r.yMax)) : r;
                else right = right.HasValue ? Rect.MinMaxRect(Mathf.Min(right.Value.xMin, r.xMin), Mathf.Min(right.Value.yMin, r.yMin), Mathf.Max(right.Value.xMax, r.xMax), Mathf.Max(right.Value.yMax, r.yMax)) : r;
            }
            if (hand.HasValue) list.Add(hand.Value); if (right.HasValue) list.Add(right.Value);
        }
        public GameBootstrap Session => session;
        /// Diagnostics: hand card rects with (tower, hand index).
        public List<(RectTransform rt, bool tower, int index)> DebugHandCards() { var l = new List<(RectTransform, bool, int)>(); foreach (var h in handCards) if (h.cg != null) l.Add(((RectTransform)h.cg.transform, h.tower, h.index)); return l; }
        private void Update()
        {
            if (session == null) return;
            if (hint != null)
            {
                bool on = Time.unscaledTime < noticeUntil && !string.IsNullOrEmpty(notice) && (session.Game.State == GameState.Build || session.Game.State == GameState.Combat);
                if (on && (hint.text != notice || noticeDirty)) { hint.text = notice; noticePill.sizeDelta = new Vector2(Mathf.Clamp(hint.GetPreferredValues(notice).x + StoneSignal.UI.ToastStyle.ExtraWidth(true), StoneSignal.UI.ToastStyle.MinWidth, StoneSignal.UI.ToastStyle.MaxWidth), noticePill.sizeDelta.y); PlaceNotice(); noticeDirty = false; } // pill hugs the text
                else if (!on) hint.text = DefaultHint;
                if (noticePill != null && noticePill.gameObject.activeSelf != on) noticePill.gameObject.SetActive(on);
            }
            UpdateFan();
            UpdateCombatHud();
            {   // drag hand fade
                var pic = PlacementInputController.Instance; bool fade = pic != null && pic.HandFade;
                handAlpha = Mathf.MoveTowards(handAlpha, fade ? HandFadeAlpha : 1f, Time.unscaledDeltaTime * (1f - HandFadeAlpha) / HandFadeSeconds);
                if (fade) fadeKey = (pic.Machine.Tower ? 1000 : 0) + Mathf.Max(0, pic.Machine.Card); else if (handAlpha >= 1f) fadeKey = -1; // the dragged card stays opaque until the others are back
                if (handAlpha != handAlphaApplied || fadeKey != fadeKeyApplied)
                {
                    handAlphaApplied = handAlpha; fadeKeyApplied = fadeKey;
                    foreach (var h in handCards) if (h.cg != null) h.cg.alpha = fadeKey >= 0 && h.tower == (fadeKey >= 1000) && h.index == fadeKey % 1000 ? 1f : handAlpha;
                }
                // v18.3 (玩法策划): faded hand cards do not block raycasts while a drag is on (and until they are opaque again), so a release
                // over them is not taken by a card; the board cell under the card is targeted (PlacementInputController.HandPassThrough).
                bool pass = fade || handAlpha < 1f;
                if (pass != handPassApplied) { handPassApplied = pass; foreach (var h in handCards) if (h.cg != null) h.cg.blocksRaycasts = !pass; }
            }
            if (PointerInput.TouchMode != badgesTouch) { badgesTouch = PointerInput.TouchMode; foreach (var b in hotkeyBadges) if (b != null) b.SetActive(!badgesTouch); } // first real touch (sticky) hides the 1-4 hotkey badges
            if (session.Draws.Next == DrawRules.Offer.Ad && session.AdAvailable != lastAdAvailable) Refresh(); // ad readiness changed (SDK loaded / failed)
            if (Input.GetKeyDown(KeyCode.Space) && session.Game.State == GameState.Build) session.Waves.StartWave();
        }

        // ================= v18.6 combat-phase HUD =================
        private CombatHudStyle CH => session != null && session.config != null && session.config.combatHud != null ? session.config.combatHud : defaultCH;
        private static readonly CombatHudStyle defaultCH = new CombatHudStyle();
        /// Towers can be built now: Build, or Combat with allowCombatTowers (same rule as GameBootstrap's TowerManager gate).
        private bool TowersBuildable => session != null && (session.Game.State == GameState.Build || (session.config.allowCombatTowers && session.Game.State == GameState.Combat));
        /// v18.6 hover unify: every hand card (fanned / unfanned block cards, tower cards) uses the v18.5b split PC hover / press values.
        private void HoverUnify(CardHover h) { var fs = FanStyle; h.Scale = fs.pressScale; h.Lift = fs.pressLiftPx; h.HoverScale = fs.hoverScale; h.HoverLift = fs.hoverLiftPx; }

        // ---- one disabled style: grey 70 % + brightness 0.8 (UiDisable, no hue), badges brightness only, 0.15 s ----
        private sealed class DimSet
        {
            public bool tower; public int index, cost; public float k = -1f; public TMP_Text price; public Color priceBase;
            public readonly List<UiDisable> full = new List<UiDisable>(), badge = new List<UiDisable>();
            public readonly List<(TMP_Text t, Color c)> badgeText = new List<(TMP_Text, Color)>();
        }
        private readonly List<DimSet> dimSets = new List<DimSet>();
        private readonly Dictionary<int, float> towerDimK = new Dictionary<int, float>(); private float blockDimK = -1f;
        private bool DimTarget(DimSet d) => d.tower ? (!TowersBuildable && session.Game.State != GameState.Reward && session.Game.State != GameState.GameOver) || d.cost > session.Economy.Gold
                                                    : !session.Blocks.PhaseAllows && session.Game.State == GameState.Combat;
        private void ApplyDims(bool force)
        {
            if (session == null) return; var ch = CH; float dt = Time.unscaledDeltaTime, step = ch.disabledSeconds <= 0f ? 1f : dt / ch.disabledSeconds;
            foreach (var d in dimSets)
            {
                float target = DimTarget(d) ? 1f : 0f, k = d.k < 0f ? target : force ? d.k : Mathf.MoveTowards(d.k, target, step);
                if (!force && Mathf.Approximately(k, d.k)) continue;
                d.k = k; if (d.tower) towerDimK[d.index] = k; else blockDimK = k;
                float g = ch.disabledGray * k, b = Mathf.Lerp(1f, ch.disabledBrightness, k), bb = Mathf.Lerp(1f, ch.badgeBrightness, k);
                foreach (var fx in d.full) if (fx != null) fx.Set(g, b);
                foreach (var fx in d.badge) if (fx != null) fx.Set(0f, bb);
                foreach (var (t, c) in d.badgeText) if (t != null) t.color = new Color(c.r * bb, c.g * bb, c.b * bb, c.a);
                if (d.price != null) d.price.color = Color.Lerp(d.priceBase, ch.unaffordablePrice, k);
            }
        }
        /// Diagnostics: (tower?, index, dim k 0..1, grey, brightness) per hand card.
        public List<(bool tower, int index, float k, float gray, float brightness)> DebugDims()
        {
            var l = new List<(bool, int, float, float, float)>();
            foreach (var d in dimSets) { var fx = d.full.Count > 0 ? d.full[0] : null; l.Add((d.tower, d.index, d.k, fx != null ? fx.Gray : 0f, fx != null ? fx.Brightness : 1f)); }
            return l;
        }

        // ---- '剩余 N 只' info plate (replaces BATTLE in combat) ----
        private RectTransform plate, plateFill, plateTrack, plateRow; private CanvasGroup plateCg, battleCg; private Image plateDot;
        private TextMeshProUGUI plateLabel, plateNumber, plateSuffix; private int plateN = -1, plateT = -1; private float platePopAt = -10f;
        private enum Anim { Hidden, In, Shown, Out }
        private Anim plateAnim = Anim.Hidden, battleAnim = Anim.Shown; private float plateAt, battleAt; private GameState hudState = GameState.Build;
        public RectTransform DebugPlate => plate; public TextMeshProUGUI DebugPlateNumber => plateNumber; public RectTransform DebugPlateFill => plateFill;
        public Image DebugPlateDot => plateDot; public CanvasGroup DebugPlateGroup => plateCg; public CanvasGroup DebugBattleGroup => battleCg; public Button DebugBattle => battle;
        public string DebugCombatHud => $"plate={plateAnim} a={(plateCg ? plateCg.alpha : -1):F2} s={(plate ? plate.localScale.x : 0):F3} N={plateN}/{plateT} | battle={battleAnim} a={(battleCg ? battleCg.alpha : -1):F2} s={(battle ? battle.transform.localScale.x : 0):F3}";
        private void BuildRemainingPlate(RectTransform root)
        {
            var ch = CH; battleCg = battle.gameObject.AddComponent<CanvasGroup>();
            float h = 104f * ch.plateHeightFrac;
            // own nested canvas: the breathing dot / N pop animate every frame and would otherwise rebuild the whole HUD canvas mesh
            var go = new GameObject("Remaining plate", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)); plate = (RectTransform)go.transform; plate.SetParent(root, false);
            Set(plate, new Vector2(1, 0), new Vector2(.5f, .5f), -24 - 128, 24 + 52, 256, h); // BATTLE rect (BR -24,24 256x104), 0.8 high, vertically centred; pivot centre for the scale-in
            plateCg = go.GetComponent<CanvasGroup>(); plateCg.blocksRaycasts = false; plateCg.interactable = false;
            var face = Img(plate, "Plate", S("ui9_panel_navy") ?? Rounded, S("ui9_panel_navy") != null ? ch.plateColor : new Color(Navy.r, Navy.g, Navy.b, ch.plateColor.a)); Full(face);
            { var fi = face.GetComponent<Image>(); fi.type = Image.Type.Sliced; fi.pixelsPerUnitMultiplier = ch.platePpuMultiplier; fi.raycastTarget = false; } // flat: same language as the HP plate
            var white = UiWhite.Get(art);
            plateTrack = Img(plate, "Bar track", white, ch.barTrack); Set(plateTrack, new Vector2(0, 0), new Vector2(0, 0), 0, 0, 0, 0);
            plateTrack.anchorMin = new Vector2(0, 0); plateTrack.anchorMax = new Vector2(1, 0); plateTrack.offsetMin = new Vector2(ch.barInsetPx, ch.barInsetPx); plateTrack.offsetMax = new Vector2(-ch.barInsetPx, ch.barInsetPx + ch.barHeightPx);
            plateFill = Img(plateTrack, "Bar fill", white, ch.barFill); plateFill.anchorMin = Vector2.zero; plateFill.anchorMax = new Vector2(1, 1); plateFill.pivot = new Vector2(0, .5f); plateFill.offsetMin = plateFill.offsetMax = Vector2.zero;
            // one centred line: [dot] 剩余 N 只 above the bar
            plateRow = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>(); plateRow.SetParent(plate, false);
            plateRow.anchorMin = new Vector2(0, 0); plateRow.anchorMax = new Vector2(1, 1); plateRow.offsetMin = new Vector2(0, ch.barInsetPx + ch.barHeightPx); plateRow.offsetMax = Vector2.zero;
            plateDot = Img(plateRow, "Ember dot", S("ui_reward_ember") ?? white, ch.dotColor).GetComponent<Image>();
            plateLabel = PlateText(Loc.RemainingPrefix, ch.labelPx, ch.labelColor, false); plateNumber = PlateText("0", ch.numberPx, ch.numberColor, true); plateSuffix = PlateText(Loc.RemainingSuffix, ch.labelPx, ch.labelColor, false);
            plateNumber.outlineWidth = ch.numberOutlineWidth; plateNumber.outlineColor = ch.numberOutline;
            go.SetActive(false); plateAnim = Anim.Hidden;
        }
        private TextMeshProUGUI PlateText(string v, float px, Color c, bool bold)
        {
            var t = Txt(plateRow, v, px, c); UseCn(t); t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
            var r = t.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.pivot = new Vector2(.5f, .5f); t.alignment = TextAlignmentOptions.Center; return t;
        }
        private void LayoutPlate()
        {
            var ch = CH; float gap = ch.wordGapPx;
            float wl = plateLabel.GetPreferredValues(plateLabel.text).x, wn = plateNumber.GetPreferredValues(plateNumber.text).x, ws = plateSuffix.GetPreferredValues(plateSuffix.text).x;
            float total = ch.dotPx + ch.dotGapPx + wl + gap + wn + gap + ws, x = -total * .5f;
            void Put(RectTransform r, float w, float hgt) { r.sizeDelta = new Vector2(w, hgt); r.anchoredPosition = new Vector2(x + w * .5f, 0f); x += w; }
            {   // ui_reward_ember has a soft edge: its visible disc (alpha >= 50 %) is dotSpriteVisibleFrac of the rect, so the rect is enlarged to show a dotPx dot
                float vis = Mathf.Max(.1f, ch.dotSpriteVisibleFrac), d = ch.dotPx / vis; var dr = plateDot.rectTransform; dr.sizeDelta = new Vector2(d, d); dr.anchoredPosition = new Vector2(x + ch.dotPx * .5f, 0f); x += ch.dotPx + ch.dotGapPx;
            }
            Put(plateLabel.rectTransform, wl, 40f); x += gap; Put(plateNumber.rectTransform, wn, 48f); x += gap; Put(plateSuffix.rectTransform, ws, 40f);
        }
        /// State change: BATTLE <-> plate transitions.
        private void HudPhase(GameState st)
        {
            if (plate == null || battle == null) return; float now = Time.unscaledTime; var prev = hudState; hudState = st;
            if (st == GameState.Combat)
            {
                plateN = plateT = -1; UpdatePlateContent(true);
                plate.gameObject.SetActive(true); plateAnim = Anim.In; plateAt = now;
                if (battleAnim != Anim.Hidden) { battleAnim = Anim.Out; battleAt = now; }
            }
            else
            {
                if (plateAnim == Anim.In || plateAnim == Anim.Shown) { plateAnim = Anim.Out; plateAt = now; }
                if (st == GameState.Build && (battleAnim == Anim.Hidden || battleAnim == Anim.Out)) { battleAnim = Anim.In; battleAt = now; } // 开战 bounce
                else if (st != GameState.Build && battleAnim != Anim.Hidden) { battleAnim = Anim.Out; battleAt = now; }
            }
            ApplyCombatHud(now);
        }
        private void UpdatePlateContent(bool force)
        {
            var ch = CH; int n = Mathf.Max(0, session.Waves.Remaining), t = Mathf.Max(n, session.Waves.Total);
            if (n != plateN || force)
            {
                if (plateN >= 0 && n < plateN) platePopAt = Time.unscaledTime;  // kill / leak pops; a split (n grows) does not
                plateN = n; plateNumber.text = n.ToString(); plateNumber.color = n <= ch.lowCount ? ch.lowColor : ch.numberColor; LayoutPlate();
            }
            if (t != plateT || force) plateT = t;
            float f = plateT > 0 ? Mathf.Clamp01(plateN / (float)plateT) : 0f;
            if (plateFill != null) { plateFill.anchorMax = new Vector2(f, 1f); plateFill.gameObject.SetActive(f > 0f); }
        }
        private void ApplyCombatHud(float now)
        {
            var ch = CH;
            if (plate != null)
            {
                float a = 1f, sc = 1f;
                if (plateAnim == Anim.In) { float k = ch.plateInSeconds <= 0f ? 1f : (now - plateAt) / ch.plateInSeconds; if (k >= 1f) plateAnim = Anim.Shown; k = Mathf.Clamp01(k); a = k; sc = Mathf.Lerp(ch.plateInScaleFrom, 1f, EaseOutCubic(k)); }
                else if (plateAnim == Anim.Out) { float k = ch.plateOutSeconds <= 0f ? 1f : (now - plateAt) / ch.plateOutSeconds; if (k >= 1f) { plateAnim = Anim.Hidden; plate.gameObject.SetActive(false); } a = 1f - Mathf.Clamp01(k); }
                else if (plateAnim == Anim.Hidden) a = 0f;
                if (plateCg.alpha != a) plateCg.alpha = a; if (plate.localScale.x != sc) plate.localScale = new Vector3(sc, sc, 1f);
                if (plateAnim != Anim.Hidden)
                {
                    float ph = ch.dotPeriod > 0f ? now * Mathf.PI * 2f / ch.dotPeriod : 0f; var dc = ch.dotColor; dc.a = Mathf.Lerp(ch.dotAlphaMin, ch.dotAlphaMax, .5f - .5f * Mathf.Cos(ph)); plateDot.color = dc;
                    float pk = ch.numberPopSeconds <= 0f ? 1f : (now - platePopAt) / ch.numberPopSeconds; float ps = pk >= 1f ? 1f : Mathf.Lerp(ch.numberPopScale, 1f, EaseOutCubic(pk));
                    plateNumber.rectTransform.localScale = new Vector3(ps, ps, 1f);
                }
            }
            if (battle != null && battleCg != null)
            {
                var bt = battle.transform; float a = 1f, sc = 1f;
                if (battleAnim == Anim.Out) { float k = ch.buttonOutSeconds <= 0f ? 1f : (now - battleAt) / ch.buttonOutSeconds; if (k >= 1f) battleAnim = Anim.Hidden; k = Mathf.Clamp01(k); a = 1f - k; sc = Mathf.Lerp(1f, ch.buttonOutScale, k); }
                else if (battleAnim == Anim.In)
                {   // 0.9 -> 1.08 -> 1.0 over buttonBounceSeconds (curve), alpha in over the first 40 %
                    float k = ch.buttonBounceSeconds <= 0f ? 1f : (now - battleAt) / ch.buttonBounceSeconds; if (k >= 1f) battleAnim = Anim.Shown; k = Mathf.Clamp01(k);
                    sc = ch.buttonBounce != null && ch.buttonBounce.length > 0 ? ch.buttonBounce.Evaluate(k) : 1f; a = Mathf.Clamp01(k / .4f);
                }
                else if (battleAnim == Anim.Hidden) { a = 0f; sc = ch.buttonOutScale; }
                bool live = battleAnim == Anim.Shown || battleAnim == Anim.In;
                if (battleCg.alpha != a) battleCg.alpha = a; if (battleCg.blocksRaycasts != live) battleCg.blocksRaycasts = battleCg.interactable = live;
                if (bt.localScale.x != sc) bt.localScale = new Vector3(sc, sc, 1f); // written only on change (HUD canvas rebuild)
            }
        }
        private void UpdateCombatHud()
        {
            float now = Time.unscaledTime;
            if (session.Game.State != hudState) HudPhase(session.Game.State);
            if (plate != null && plateAnim != Anim.Hidden) UpdatePlateContent(false); // also while fading out (the last kill shows 0)
            ApplyCombatHud(now);
            ApplyDims(false);
            UpdatePileDim();
            SetHandCountShown();
        }

        // ---- draw pile in combat: colour x (0.55,0.55,0.6), labels fade, no raycasts, 0.15 s ----
        private readonly List<(Graphic g, Color c)> pileBase = new List<(Graphic, Color)>(); private float pileK = -1f; private CanvasGroup pileCg;
        public float DebugPileDim => pileK;
        private void CapturePileBase()
        {
            pileBase.Clear(); if (drawPile == null) return;
            void Add(Graphic g) { if (g != null) pileBase.Add((g, g.color)); }
            Add(drawPile.top); Add(drawPile.pillImage); Add(drawPile.tail);
            foreach (var img in drawPile.stackRoot.GetComponentsInChildren<Image>(true)) if (img != drawPile.top) Add(img);
            if (pileK > 0f) { float k = pileK; pileK = -2f; ApplyPile(k); }
        }
        private void ApplyPile(float k)
        {
            if (Mathf.Approximately(k, pileK)) return; pileK = k; var m = CH.pileMultiply;
            var mul = Color.Lerp(Color.white, new Color(m.r, m.g, m.b, 1f), k);
            foreach (var (g, c) in pileBase) if (g != null) g.color = new Color(c.r * mul.r, c.g * mul.g, c.b * mul.b, c.a);
            if (drawPile.statusLabel != null) drawPile.statusLabel.alpha = 1f - k;
            if (drawPile.statusIcon != null) { var ic = drawPile.statusIcon.color; ic.a = 1f - k; drawPile.statusIcon.color = ic; }
        }
        private void UpdatePileDim()
        {
            if (drawPile == null) return;
            if (pileCg == null) { pileCg = drawPile.gameObject.GetComponent<CanvasGroup>() ?? drawPile.gameObject.AddComponent<CanvasGroup>(); if (pileBase.Count == 0) CapturePileBase(); }
            bool combat = session.Game.State == GameState.Combat; var ch = CH;
            pileCg.blocksRaycasts = !combat; // raycastTarget off in combat (draw is Build-only)
            float target = combat ? 1f : 0f, k = pileK < 0f ? target : Mathf.MoveTowards(pileK, target, ch.pileSeconds <= 0f ? 1f : Time.unscaledDeltaTime / ch.pileSeconds);
            ApplyPile(k);
        }

        // ---- hand count pill counts up per settled card during a (re)deal ----
        private int handCountShown = -1;
        public int DebugHandCountShown => handCountShown;
        private void SetHandCountShown()
        {
            if (session == null || drawPile == null) return;
            int n = session.Blocks.Hand.Cards.Count;
            if (CH.handCountCountsUp) { float now = Time.unscaledTime; foreach (var p in fanPose) if (p.enter && p.introStart >= 0f && now < p.introStart + p.introDur) n -= p.count; }
            n = Mathf.Max(0, n); if (n == handCountShown) return; handCountShown = n;
            drawPile.SetHandCount(n, BlockHandManager.MaxCards);
        }

        // ---------- reward / game over ----------
        private void BuildRewardPanel(RectTransform root)
        {
            var panel = Img(root, "Rewards", null, new Color(.06f, .09f, .18f, .88f)); Full(panel); rewardPanel = panel.gameObject;
            var title = Txt(panel, Loc.ChooseUpgrade, 52, Ink); Center(title.rectTransform, 0, -300, 1100, 70); title.outlineWidth = .2f; title.outlineColor = Navy;
            for (int i = 0; i < 3; i++)
            {
                int idx = i; var card = Img(panel, "Reward " + i, null, Blueprint); Center(card, (i - 1) * 380, 20, 340, 400); Outline(card, Navy, 4);
                rewardNames[i] = Txt(card, "", 32, Ink); TL(rewardNames[i].rectTransform, 20, 24, 300, 90);
                rewardDescriptions[i] = Txt(card, "", 22, new Color(.86f, .93f, 1f)); TL(rewardDescriptions[i].rectTransform, 20, 120, 300, 130);
                rewardEffects[i] = Txt(card, "", 26, Gold); TL(rewardEffects[i].rectTransform, 20, 250, 300, 50);
                var choose = Btn(card, Loc.Choose, 30, Orange, () => session.Rewards.Choose(idx)); TL((RectTransform)choose.transform, 40, 316, 260, 64);
            }
            rewardPanel.SetActive(false);
            var over = Img(root, "Game over", null, new Color(.05f, .06f, .12f, .92f)); Full(over); overPanel = over.gameObject;
            var text = Txt(over, Loc.CoreDark, 64, Ink); Center(text.rectTransform, 0, -90, 1200, 110);
            var restart = Btn(over, Loc.Restart, 40, Orange, () => { TimeController.ResetAll(); SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); });
            Center((RectTransform)restart.transform, 0, 60, 300, 84);
            overPanel.SetActive(false);
        }
        private StoneSignal.UI.RewardPickUI pickUi; private StoneSignal.UI.RewardGlow pickGlow; private bool picking;
        public StoneSignal.UI.RewardPickUI PickUi => pickUi; public StoneSignal.UI.RewardGlow PickGlow => pickGlow;
        // v16.2 RewardPickUI (art): Show(options, rarity) -> OnPicked(i) -> PlayPick(i, target, onDone) -> Rewards.Choose(i).
        /// Diagnostics (gameplay shot): show the reward cards with forced rarities and placeholder content.
        public bool DebugShowRewardPick(StoneSignal.UI.RewardRarity[] rar, RewardEffect[] effects = null)
        {
            debugRarity = rar; debugEffects = effects; try { return ShowRewardPick(); } finally { debugRarity = null; debugEffects = null; }
        }
        private StoneSignal.UI.RewardRarity[] debugRarity; private RewardEffect[] debugEffects;
        /// Reward card tier: 免广告再抽 (ExtraDraw) is 精良 (Rare, 玩法策划 v18); the other wave rewards stay Common for now.
        public static StoneSignal.UI.RewardRarity RarityOf(RewardEffect e) => (StoneSignal.UI.RewardRarity)(int)RewardTiers.For(e); // v18.3 planner tier table
        public static StoneSignal.UI.RewardRarity RuneRarity(int rune) => (StoneSignal.UI.RewardRarity)(int)RewardTiers.RuneTier(rune); // v18.3: 共鸣·白 稀有, other runes 精良 (display only)
        public static StoneSignal.UI.RewardRarity RarityOf(RewardData r) => r != null ? (StoneSignal.UI.RewardRarity)(int)r.tier : StoneSignal.UI.RewardRarity.Common; // v18.2: data tier (RewardRoll) drives the card frame
        private bool ShowRewardPick()
        {
            if (!EnsurePickUi()) return false;
            return ShowRewardPickContent();
        }
        private bool EnsurePickUi()
        {
            if (!ArtSteps.On(5) || art == null || art.UiSprite("ui9_reward_frame_common") == null) return false;
            if (pickUi == null)
            {
                var cv = Canvas("Reward pick", 20);
                pickUi = cv.gameObject.AddComponent<StoneSignal.UI.RewardPickUI>();
                string[] tier = { "common", "rare", "epic", "legendary" };
                // v17 (Docs/reward_pick_v17.md): frames[4] / bands[4] / tierPill / ember / burst / font
                for (int i = 0; i < 4; i++) { pickUi.frames[i] = art.UiSprite("ui9_reward_frame_" + tier[i]); pickUi.bands[i] = art.UiSprite("ui9_reward_band_" + tier[i]); }
                pickUi.tierPill = art.UiSprite("ui9_reward_tier_pill"); pickUi.ember = art.UiSprite("ui_reward_ember"); pickUi.burst = art.UiSprite("ui_reward_burst");
                for (int i = 0; i < 4; i++) if (pickUi.frames[i] == null || pickUi.bands[i] == null) Debug.LogError("REWARD PICK: missing ui9_reward_frame/band_" + tier[i] + " in ArtCatalog.uiSprites");
                if (pickUi.tierPill == null) Debug.LogError("REWARD PICK: missing ui9_reward_tier_pill in ArtCatalog.uiSprites");
                pickGlow = cv.gameObject.AddComponent<StoneSignal.UI.RewardGlow>();
                pickGlow.glow = art.UiSprite("ui_rune_socket_glow") ?? art.UiSprite("ui_counter_glow_ring");
                Debug.Log("REWARD GLOW sprite=" + (pickGlow.glow ? pickGlow.glow.name + " tex=" + pickGlow.glow.texture.name + " packed=" + pickGlow.glow.packed : "MISSING") +
                          " | frame tex=" + (pickUi.frames[0] ? pickUi.frames[0].texture.name + " packed=" + pickUi.frames[0].packed : "-") + " sameTexture=" + (pickGlow.glow && pickUi.frames[0] && pickGlow.glow.texture == pickUi.frames[0].texture));
                pickUi.font = CnFont ?? TMP_Settings.defaultFontAsset; // v18: card text is Chinese -> CN font primary (one material per text, no Latin/CJK sub-mesh split)
                if ((float)Screen.width / Mathf.Max(1, Screen.height) < 1.5f) pickUi.cardSize = new Vector2(380, 540); // 4:3
                pickUi.OnPicked += i =>
                {
                    if (picking) return; picking = true; if (pickGlow != null) pickGlow.Pick(i);
                    pickUi.PlayPick(i, new Vector2(Screen.width * .5f, Screen.height * .12f), () => { picking = false; session.Rewards.Choose(i); pickUi.gameObject.SetActive(false); });
                };
            }
            return true;
        }
        private bool ShowRewardPickContent()
        {
            if (debugRarity != null)
            {
                // forced rarities, real content: Rare = a rune option (as in a real offer), other tiers = wave rewards from the actual pool
                var dopts = new StoneSignal.UI.RewardOption[debugRarity.Length]; var used = new HashSet<RewardData>();
                RewardEffect[][] pref = { new[] { RewardEffect.AddBlock, RewardEffect.WaveGold }, null, new[] { RewardEffect.AllAttackSpeed, RewardEffect.CannonRadius }, new[] { RewardEffect.AllDamage, RewardEffect.BonusSlot } };
                for (int i = 0; i < dopts.Length; i++)
                {
                    int ri = (int)debugRarity[i];
                    bool own = debugEffects != null && i < debugEffects.Length && RarityOf(debugEffects[i]) == debugRarity[i] && debugRarity[i] != StoneSignal.UI.RewardRarity.Common;
                    if (debugRarity[i] == StoneSignal.UI.RewardRarity.Rare && !own) { dopts[i] = RuneOption(i % RuneRules.Names.Length); continue; } // forced Rare = a rune option unless the effect itself is Rare (ExtraDraw)
                    RewardData pick = null;
                    if (debugEffects != null && i < debugEffects.Length) foreach (var r in session.config.rewards) if (pick == null && r != null && r.effect == debugEffects[i] && !used.Contains(r)) pick = r;
                    if (pick == null) foreach (var e in pref[ri]) foreach (var r in session.config.rewards) if (pick == null && r != null && r.effect == e && !used.Contains(r)) pick = r;
                    if (pick == null) foreach (var r in session.config.rewards) if (pick == null && r != null && !used.Contains(r)) pick = r;
                    if (pick != null) { used.Add(pick); dopts[i] = RewardOption(pick); }
                }
                pickUi.gameObject.SetActive(true); picking = false; pickUi.Show(dopts, debugRarity); FitRewardText(); if (pickGlow != null) pickGlow.Begin(debugRarity); return true;
            }
            int n = Mathf.Min(3, session.Rewards.Choices.Count);
            var opts = new StoneSignal.UI.RewardOption[n]; var rar = new StoneSignal.UI.RewardRarity[n];
            for (int i = 0; i < n; i++)
            {
                int rune = session.Rewards.RuneChoices.Count > i ? session.Rewards.RuneChoices[i] : RuneRules.NoRune;
                if (rune != RuneRules.NoRune) { opts[i] = RuneOption(rune); rar[i] = RuneRarity(rune); }
                else { opts[i] = RewardOption(session.Rewards.Choices[i]); rar[i] = RarityOf(session.Rewards.Choices[i]); } // tier from the reward asset (ExtraDraw = 精良)
            }
            pickUi.gameObject.SetActive(true); picking = false; pickUi.Show(opts, rar); FitRewardText(); if (pickGlow != null) pickGlow.Begin(rar); Refresh();
            return true;
        }
        private StoneSignal.UI.RewardOption RuneOption(int rune) =>
            new StoneSignal.UI.RewardOption { title = Loc.RuneTitle(rune), desc = Loc.RuneDesc(rune), icon = S(StoneSignal.VFX.RuneArt.Icon[Mathf.Clamp(rune, 0, 5)]) ?? S("ui_card_reward_rune") };
        private StoneSignal.UI.RewardOption RewardOption(RewardData r) => new StoneSignal.UI.RewardOption { title = Loc.RewardTitle(r), desc = Loc.RewardDesc(r), icon = RewardIcon(r) };
        // Reward icons: art v17.4 per-effect icons (StoneSignal.UI.RewardIconMap, packed in the HUD atlas by BatchWire); the old closest-sprite
        // mapping below stays as the fallback when an icon is missing. Rune icons are reserved for rune options.
        private Sprite RewardIcon(RewardData r)
        {
            var art17 = S(StoneSignal.UI.RewardIconMap.For(r.effect)); if (art17 != null) return art17;
            Sprite T(string kind) { foreach (var d in session.config.towers) if (d != null && d.icon != null && d.name.StartsWith(kind)) return d.icon; return null; }
            switch (r.effect)
            {
                case RewardEffect.AllDamage: return S("ui_reward_gem") ?? S("ui_coin_gold");
                case RewardEffect.AllAttackSpeed: return S("ui_reward_gem") ?? S("ui_coin_gold");
                case RewardEffect.AllRange: return S("ui_reward_gem") ?? S("ui_coin_gold");
                case RewardEffect.PathSlow: return S("ui_reward_shard") ?? S("ui_coin_gold");
                case RewardEffect.CannonRadius: return T("Seismic");
                case RewardEffect.ArrowRange: return T("Needle");
                case RewardEffect.AddBlock: return r.blockShape != null ? BlockIcon(r.blockShape) : S("ui_icon_block_O");
                case RewardEffect.BonusSlot: return r.blockShape != null ? BlockIcon(r.blockShape) : S("ui_icon_block_L");
                case RewardEffect.NextDraw: return S("ui_draw_pile");
                case RewardEffect.ExtraDraw: return S("ui_draw_pile") ?? S("ui_icon_free"); // no art icon yet (RewardIconMap has none for ExtraDraw)
                case RewardEffect.BaseHP: case RewardEffect.WaveHeal: return art != null ? art.uiOrbCore : null;
                default: return S("ui_coin_gold"); // KillGold, WaveGold, NextWaveGold, TowerDiscount
            }
        }
        // Card text fit (art RewardPickUI untouched): title one line, auto-size 44 -> 26; description wraps inside the band, 28 -> 18.
        private void FitRewardText()
        {
            UnityEngine.Canvas.ForceUpdateCanvases();
            for (int i = 0; ; i++)
            {
                var card = pickUi.transform.Find("RewardCard" + i); if (card == null) break;
                var title = card.Find("Title")?.GetComponent<TMP_Text>();
                if (title != null) { title.enableWordWrapping = false; title.overflowMode = TextOverflowModes.Overflow; title.enableAutoSizing = true; title.fontSizeMin = 26; title.fontSizeMax = 44; AntiOrphan(title, card.name); }
                foreach (var t in card.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == "Desc") { t.enableWordWrapping = true; t.enableAutoSizing = true; t.fontSizeMin = 18; t.fontSizeMax = 28; t.overflowMode = TextOverflowModes.Overflow; AntiOrphan(t, card.name); }
            }
        }
        // ---- v18.3 (玩法策划): no reward / rune card text may wrap leaving a single orphan character on a paragraph's last line.
        // Text-layout fix only (no card / art size change): the last two characters of that paragraph are wrapped in <nobr> so they move to
        // the last line together (TMP then re-wraps / auto-sizes). Punctuation, symbols and spaces do not count as characters.
        private readonly List<string> orphanLog = new List<string>();
        static char OrphanChar(TMP_Text t, int l) { var ti = t.textInfo; var li = ti.lineInfo[l]; for (int c = li.firstCharacterIndex; c <= li.lastCharacterIndex; c++) if (Counted(ti.characterInfo[c].character)) return ti.characterInfo[c].character; return '?'; }
        static bool Counted(char ch) => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && !char.IsSymbol(ch);
        /// Index of the first wrapped paragraph-last line that holds exactly one counted character, or -1.
        public static int OrphanLine(TMP_Text t, out int lines)
        {
            t.ForceMeshUpdate(true, true); var ti = t.textInfo; lines = ti.lineCount;
            for (int l = 1; l < ti.lineCount; l++)
            {
                var li = ti.lineInfo[l]; var prev = ti.lineInfo[l - 1];
                if (prev.lastCharacterIndex < 0 || li.lastCharacterIndex < li.firstCharacterIndex) continue;
                if (ti.characterInfo[prev.lastCharacterIndex].character == '\n') continue;              // hard break: not a wrap
                bool lastOfPara = l == ti.lineCount - 1 || ti.characterInfo[li.lastCharacterIndex].character == '\n';
                if (!lastOfPara) continue;
                int n = 0; for (int c = li.firstCharacterIndex; c <= li.lastCharacterIndex; c++) if (Counted(ti.characterInfo[c].character)) n++;
                if (n == 1) return l;
            }
            return -1;
        }
        private void AntiOrphan(TMP_Text t, string where)
        {
            if (t == null || string.IsNullOrEmpty(t.text) || t.text.Contains("<nobr>")) return;
            int l = OrphanLine(t, out int lines); if (l < 0) return;
            // 1) auto-size a little smaller (<= 8%) if that pulls the orphan back so the paragraph needs one line fewer
            if (t.enableAutoSizing)
            {
                float f0 = t.fontSize, maxW = t.fontSizeMax, floor = Mathf.Max(t.fontSizeMin, f0 * .92f); string txt0 = t.text.Replace("\n", "\\n"); char oc = OrphanChar(t, l);
                for (float f = f0 - .5f; f >= floor - .01f; f -= .5f)
                {
                    t.fontSizeMax = f;
                    if (OrphanLine(t, out int nl) < 0 && nl < lines) { orphanLog.Add(where + "/" + t.name + " '" + txt0 + "' orphan '" + oc + "' on line " + (l + 1) + "/" + lines + " -> auto-size " + f0.ToString("F1") + " -> " + t.fontSize.ToString("F1") + " (" + nl + " line" + (nl > 1 ? "s" : "") + "): FIXED"); return; }
                }
                t.fontSizeMax = maxW; l = OrphanLine(t, out lines); if (l < 0) return;
            }
            // 2) else keep the last two characters together (<nobr>)
            var ti = t.textInfo; var li = ti.lineInfo[l]; int c0 = -1, cp = -1;
            for (int c = li.firstCharacterIndex; c <= li.lastCharacterIndex && c0 < 0; c++) if (Counted(ti.characterInfo[c].character)) c0 = c;
            for (int c = li.firstCharacterIndex - 1; c >= 0 && cp < 0; c--) if (Counted(ti.characterInfo[c].character)) cp = c;
            string raw = t.text, before = raw.Replace("\n", "\\n");
            if (c0 < 0 || cp < 0) { orphanLog.Add(where + "/" + t.name + " '" + before + "' orphan on line " + (l + 1) + "/" + lines + " - NOT FIXABLE (no previous character)"); return; }
            int s0 = ti.characterInfo[cp].index, s1 = ti.characterInfo[c0].index, end = raw.IndexOf('\n', s1); if (end < 0) end = raw.Length;
            while (s0 > 0 && raw[s0 - 1] < 128 && (char.IsLetterOrDigit(raw[s0 - 1]) || ".%+-".IndexOf(raw[s0 - 1]) >= 0)) s0--; // keep a number / Latin token whole ("1.5 秒", not "1." + "5 秒")
            t.text = raw.Substring(0, s0) + "<nobr>" + raw.Substring(s0, end - s0) + "</nobr>" + raw.Substring(end);
            bool ok = OrphanLine(t, out int after) < 0;
            orphanLog.Add(where + "/" + t.name + " '" + before + "' orphan '" + ti.characterInfo[c0].character + "' on line " + (l + 1) + "/" + lines + " -> <nobr> last 2 chars" + (raw.Substring(s0, s1 - s0).Length > 1 && raw[s0] < 128 ? " (+ whole number token)" : "") + ": " + (ok ? "FIXED (" + after + " lines, font " + t.fontSize.ToString("F1") + ")" : "STILL ORPHAN"));
        }
        /// Diagnostics (-cardtext): every reward in the pool and every rune option, three per card set, laid out on the real cards;
        /// per text: lines, characters on the last line, orphan before / after the layout fix.
        public string DebugCardTextAudit()
        {
            if (!EnsurePickUi()) return "no RewardPickUI (art step 5 off?)\n";
            var items = new List<(StoneSignal.UI.RewardOption o, StoneSignal.UI.RewardRarity r, string id)>();
            foreach (var r in session.config.rewards) if (r != null) items.Add((RewardOption(r), RarityOf(r), r.effect.ToString()));
            for (int i = 0; i < RuneRules.Names.Length; i++) items.Add((RuneOption(i), RuneRarity(i), "rune " + RuneRules.Names[i]));
            var sb = new System.Text.StringBuilder(); orphanLog.Clear(); int bad = 0;
            for (int g = 0; g < items.Count; g += 3)
            {
                int n = Mathf.Min(3, items.Count - g); var opts = new StoneSignal.UI.RewardOption[n]; var rar = new StoneSignal.UI.RewardRarity[n];
                for (int i = 0; i < n; i++) { opts[i] = items[g + i].o; rar[i] = items[g + i].r; }
                pickUi.gameObject.SetActive(true); pickUi.Show(opts, rar); FitRewardText();
                for (int i = 0; i < n; i++)
                {
                    var card = pickUi.transform.Find("RewardCard" + i); if (card == null) continue;
                    sb.Append("  ").Append(items[g + i].id).Append(" [").Append(rar[i]).Append(']');
                    foreach (var t in card.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (t.name != "Title" && t.name != "Desc") continue;
                        int ol = OrphanLine(t, out int lines); var ti = t.textInfo; var last = lines > 0 ? ti.lineInfo[lines - 1] : default; int lc = 0;
                        if (lines > 0) for (int c = last.firstCharacterIndex; c <= last.lastCharacterIndex && c >= 0; c++) if (Counted(ti.characterInfo[c].character)) lc++;
                        if (ol >= 0) bad++;
                        sb.Append(" | ").Append(t.name).Append(" '").Append(t.text.Replace("\n", "\\n")).Append("' lines=").Append(lines).Append(" lastLineChars=").Append(lc).Append(" font=").Append(t.fontSize.ToString("F1")).Append(ol >= 0 ? " ORPHAN" : "");
                    }
                    sb.Append('\n');
                }
            }
            pickUi.gameObject.SetActive(false);
            return "card text audit " + Screen.width + "x" + Screen.height + ": " + items.Count + " options, orphans after fix = " + bad + "\n" + sb +
                   "  layout fixes applied: " + (orphanLog.Count == 0 ? "none" : "\n    " + string.Join("\n    ", orphanLog)) + "\n";
        }
        // StoneSignalRoundedCN-Heavy SDF (first TMP fallback, Editor/CjkFontSetup). Labels that are Chinese use it as primary font so digits and
        // CJK come from one atlas/material; pure-number HUD text stays on the default font.
        private static TMP_FontAsset cnFont; private static bool cnLooked;
        private static TMP_FontAsset CnFont { get { if (!cnLooked) { cnLooked = true; foreach (var f in TMP_Settings.fallbackFontAssets) if (f != null && f.name.Contains("RoundedCN")) cnFont = f; } return cnFont; } }
        private static void UseCn(TMP_Text t) { if (t != null && CnFont != null) { float ow = t.outlineWidth; Color32 oc = t.outlineColor; t.font = CnFont; t.fontSharedMaterial = CnFont.material; var inst = t.fontMaterial; t.outlineWidth = ow; t.outlineColor = oc; } } // re-instance on the CN atlas: an outline set earlier left a Liberation material instance that TMP restores on the next outline change (garbled BATTLE label)
        private void ShowRewards()
        {
            if (ShowRewardPick()) return;
            for (int i = 0; i < 3 && i < session.Rewards.Choices.Count; i++)
            {
                int rune = session.Rewards.RuneChoices.Count > i ? session.Rewards.RuneChoices[i] : RuneRules.NoRune;
                if (rune != RuneRules.NoRune) { rewardNames[i].text = Loc.RuneTitle(rune); rewardDescriptions[i].text = Loc.RuneOptionNote; rewardEffects[i].text = Loc.RuneEffects[rune]; continue; }
                var r = session.Rewards.Choices[i]; rewardNames[i].text = Loc.RewardTitle(r); rewardDescriptions[i].text = ""; rewardEffects[i].text = Loc.RewardDesc(r);
            }
            rewardPanel.SetActive(true); Refresh();
        }

        // ---------- builders ----------
        private RectTransform Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.GetComponent<CanvasScaler>(); s.referenceResolution = new Vector2(1920, 1080);
            var safe = new GameObject("SafeAreaRoot", typeof(RectTransform)).GetComponent<RectTransform>(); safe.SetParent(go.transform, false);
            go.AddComponent<HudScaler>().Init(s, safe); // ui_layout_v14: match height (>=16:9) / width (<16:9), scale clamp 0.6-1.6, Screen.safeArea
            return safe;
        }
        private static RectTransform Group(Transform parent, string name) { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r; }
        private static RectTransform Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = sprite; img.color = color; img.raycastTarget = false; return go.GetComponent<RectTransform>();
        }
        private static TextMeshProUGUI Txt(Transform parent, string value, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>(); t.text = value; t.fontSize = size; t.color = color; t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.enableWordWrapping = true; return t;
        }
        private Button Btn(Transform parent, string label, float size, Color color, Action action) => Btn(parent, label, size, null, color, action);
        // 9-slice art button (ui9_*): rounded, thick dark outline, bottom thickness. Missing sprite -> generated rounded placeholder.
        private Button Btn(Transform parent, string label, float size, string sprite, Color color, Action action, TMP_FontAsset font = null)
        {
            var r = Panel(parent, label, sprite, color); r.GetComponent<Image>().raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>(); b.onClick.AddListener(() => action());
            var pressed = sprite != null ? S(sprite.Replace("_normal", "_pressed")) : null;
            if (pressed != null && sprite.EndsWith("_normal")) { b.transition = Selectable.Transition.SpriteSwap; b.spriteState = new SpriteState { pressedSprite = pressed }; }
            var colors = b.colors; colors.disabledColor = new Color(.8f, .8f, .8f, 1); b.colors = colors;
            var t = Txt(r, label, size, Ink); if (font != null) t.font = font; /* before the outline creates a material instance */ Full(t.rectTransform); t.rectTransform.offsetMin = new Vector2(0, 6); t.outlineWidth = .18f; t.outlineColor = Navy; return b;
        }
        private Sprite S(string name) => art != null ? art.UiSprite(name) : null;
        public Sprite DebugSprite(string name) => S(name);   // diagnostics (atlas page check)
        private RectTransform Panel(Transform parent, string name, string sprite, Color fallback)
        {
            var sp = sprite != null ? S(sprite) : null;
            var r = Img(parent, name, sp ?? Rounded, sp != null ? Color.white : fallback);
            r.GetComponent<Image>().type = Image.Type.Sliced;
            if (sp == null) { var o = Img(r, "Edge", RoundedEdge, Navy); Full(o); o.GetComponent<Image>().type = Image.Type.Sliced; o.SetAsFirstSibling(); }
            return r;
        }
        // Generated placeholders (rounded 9-slice, outline ring, circle) - replaced automatically when the art sprite exists.
        private static Sprite rounded, roundedEdge, circle;
        private static Sprite Rounded => rounded != null ? rounded : rounded = Gen(64, 18, 0, false);
        private static Sprite RoundedEdge => roundedEdge != null ? roundedEdge : roundedEdge = Gen(64, 18, 5, false);
        private static Sprite Circle => circle != null ? circle : circle = Gen(96, 48, 0, true);
        private static Sprite Gen(int n, int radius, int ring, bool round)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ui_placeholder" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float cx = Mathf.Clamp(x + .5f, radius, n - radius), cy = Mathf.Clamp(y + .5f, radius, n - radius);
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + .5f);
                if (ring > 0) a -= Mathf.Clamp01(radius - ring - d + .5f) * (Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y)) >= ring ? 1 : 0);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            float b = round ? 0 : radius;
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }
        private static void Outline(RectTransform r, Color c, float w) { var o = r.gameObject.AddComponent<Outline>(); o.effectColor = c; o.effectDistance = new Vector2(w, -w); }
        private static void Set(RectTransform r, Vector2 anchor, Vector2 pivot, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); }
        private static void TL(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(0, 1), new Vector2(0, 1), x, -y, w, h);
        private static void TR(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(1, 1), new Vector2(1, 1), x, -y, w, h);
        private static void TC(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(.5f, 1), new Vector2(.5f, 1), x, -y, w, h);
        private static void BL(RectTransform r, float x, float y, float w, float h) => Set(r, Vector2.zero, Vector2.zero, x, y, w, h);
        private static void BR(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(1, 0), new Vector2(1, 0), x, y, w, h);
        private static void BC(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(.5f, 0), new Vector2(.5f, 0), x, y, w, h);
        private static void BCs(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(.5f, 0), new Vector2(.5f, 0), x, y, w, h);
        private static void BRs(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(1, 0), new Vector2(1, 0), x, y, w, h);
        private static void Center(RectTransform r, float x, float y, float w, float h) => Set(r, new Vector2(.5f, .5f), new Vector2(.5f, .5f), x, -y, w, h);
        private static void Full(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        private void OnDestroy()
        {
            if (session == null) return;
            if (session.Economy != null) session.Economy.Changed -= Refresh;
            if (session.Waves != null) session.Waves.Changed -= Refresh;
            if (session.Blocks != null) { session.Blocks.Changed -= Dirty; session.Blocks.Notice -= ShowNotice; }
            if (session.Towers != null) { session.Towers.Changed -= Dirty; session.Towers.Notice -= ShowNotice; }
            if (session.Game != null) session.Game.StateChanged -= OnState;
            if (session.Rewards != null) session.Rewards.Offered -= ShowRewards;
        }
    }
}
