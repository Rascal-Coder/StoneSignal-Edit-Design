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
    public sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        RectTransform card; Vector2 rest; Vector3 restScale; bool lifted;
        public void Init(RectTransform target, bool alreadyLifted) { card = target; rest = target.anchoredPosition; restScale = target.localScale; lifted = alreadyLifted; }
        public void OnPointerEnter(PointerEventData e) { card.localScale = restScale * 1.06f; card.anchoredPosition = rest + (lifted ? Vector2.zero : new Vector2(0, 12)); }
        public void OnPointerExit(PointerEventData e) { card.localScale = restScale; card.anchoredPosition = rest; }
    }
    public sealed class GameUI : MonoBehaviour
    {
        private GameBootstrap session;
        private ArtCatalog art;
        private TextMeshProUGUI hpNumber, hpSmall, gold, wave, hint, targetLabel;
        private RectTransform goldPill, handCount; private StoneSignal.VFX.DrawPileUI drawPile; private StoneSignal.VFX.RewardCounterUI goldCounter;
        public RectTransform GoldTarget => goldPill; public StoneSignal.VFX.RewardCounterUI GoldCounter => goldCounter;
        private Button battle;
        private readonly Button[] speedButtons = new Button[4];
        private RectTransform towerHand, blockHand;
        private readonly List<GameObject> built = new List<GameObject>();
        private GameObject rewardPanel, overPanel;
        private readonly TextMeshProUGUI[] rewardNames = new TextMeshProUGUI[3], rewardDescriptions = new TextMeshProUGUI[3], rewardEffects = new TextMeshProUGUI[3];
        private float noticeUntil; private string notice; private bool handDirty = true;
        private int speedIndex = 1; private bool paused;
        private const string DefaultHint = Loc.RotateHint;

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
            hpSmall = Txt(root, "", 22, Ink); TL(hpSmall.rectTransform, 24, 172, 150, 30); hpSmall.outlineWidth = .25f; hpSmall.outlineColor = Navy;
            var pill = Panel(root, "Gold", "ui9_pill_gold", Gold); TL(pill, 190, 44, 220, 72);
            RectTransform coin;
            if (S("ui_coin_gold") != null) { coin = Img(pill, "Coin", S("ui_coin_gold"), Color.white); TL(coin, 6, 6, 60, 60); }
            else
            {
                var coinRim = Img(pill, "Coin rim", Circle, Navy); TL(coinRim, 10, 9, 54, 54); coin = coinRim;
                var face = Img(coinRim, "Coin", Circle, Hex("FFC83D")); Center(face, 0, 0, 44, 44);
                var dollar = Txt(face, "$", 28, Hex("8A5A00")); Full(dollar.rectTransform);
            }
            gold = Txt(pill, "", 40, Navy); TL(gold.rectTransform, 66, 4, 146, 64); gold.alignment = TextAlignmentOptions.Center;
            // kill gold flies into this counter (art RewardFlyFx + RewardCounterUI)
            goldCounter = pill.gameObject.AddComponent<StoneSignal.VFX.RewardCounterUI>(); goldCounter.label = gold; goldCounter.punchTarget = pill; goldCounter.icon = coin;
            var ringSprite = S("ui_counter_glow_ring");
            if (ringSprite != null) { var ring = Img(pill, "Glow ring", ringSprite, new Color(1, 1, 1, 0)); TL(ring, -30, -30, 126, 126); ring.GetComponent<Image>().raycastTarget = false; goldCounter.glowRing = ring.GetComponent<Image>(); }
            var pop = Txt(pill, "", 28, Gold); TL(pop.rectTransform, 80, 66, 140, 36); pop.outlineWidth = .25f; pop.outlineColor = Navy; goldCounter.incomePop = pop;
            goldCounter.SetValue(session.Economy.Gold, true);
            goldPill = pill;
            // ---- top-centre wave banner
            var banner = Panel(root, "Wave banner", "ui9_banner_wave_red", Red); TC(banner, 0, 24, 440, 92);
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
            battle = Btn(root, Loc.Battle, 44, "ui9_button_battle_orange", Orange, () => session.Waves.StartWave(), CnFont); BR((RectTransform)battle.transform, -24, 24, 256, 104);
            battle.name = "BATTLE"; if (drawPile.statusLabel != null) UseCn(drawPile.statusLabel); { var bl = battle.GetComponentInChildren<TextMeshProUGUI>(); bl.rectTransform.offsetMin = new Vector2(22, 8); bl.rectTransform.offsetMax = new Vector2(-22, 0); bl.enableWordWrapping = false; bl.overflowMode = TextOverflowModes.Overflow; bl.enableAutoSizing = true; bl.fontSizeMin = 28; bl.fontSizeMax = 40; } // label kept inside the 9-slice face (it touched the rim)
            var handCanvas = Canvas("Hand", 1);
            towerHand = Group(handCanvas, "Tower hand"); Full(towerHand);
            blockHand = Group(handCanvas, "Block hand"); Full(blockHand);
            BuildRewardPanel(Canvas("Overlays", 2)); 
            session.Economy.Changed += Refresh; session.Waves.Changed += Refresh;
            session.Blocks.Changed += Dirty; session.Towers.Changed += Dirty;
            session.Game.StateChanged += OnState; session.Rewards.Offered += ShowRewards;
            session.Blocks.Notice += ShowNotice; session.Towers.Notice += ShowNotice;
            SetSpeed(1); Refresh();
        }

        // ---------- state ----------
        private void Dirty() { handDirty = true; Refresh(); }
        private void Refresh()
        {
            if (session == null) return;
            hpNumber.text = session.Economy.HP.ToString();
            hpSmall.text = session.Economy.HP + "/" + session.Economy.MaxHP;
            wave.text = Loc.Wave(Mathf.Min(session.Waves.WaveIndex + 1, Mathf.Max(1, session.config.waves.Length)), session.config.waves.Length);
            var offer = session.Draws.Next;
            var st = session.Blocks.Hand.IsFull ? StoneSignal.VFX.DrawPileState.Full : offer == DrawRules.Offer.Free ? StoneSignal.VFX.DrawPileState.Free : offer == DrawRules.Offer.Ad ? StoneSignal.VFX.DrawPileState.Ad : StoneSignal.VFX.DrawPileState.Used;
            if (drawPile.State != st) drawPile.SetState(st);
            { var dl = Loc.DrawStatus(st); if (drawPile.statusLabel != null && drawPile.statusLabel.text != dl) drawPile.statusLabel.text = dl; } // art DrawPileUI writes English; label text is ours
            drawPile.SetStackCount(st == StoneSignal.VFX.DrawPileState.Free || st == StoneSignal.VFX.DrawPileState.Full ? 5 : st == StoneSignal.VFX.DrawPileState.Ad ? 4 : 3);
            if (session.Game.State != GameState.Build && drawPile.button.interactable) drawPile.button.interactable = false;
            drawPile.SetHandCount(session.Blocks.Hand.Cards.Count, BlockHandManager.MaxCards);
            goldCounter.SetValue(session.Economy.Gold - session.GoldInFlight);
            battle.interactable = session.Game.State == GameState.Build;
            if (handDirty) RebuildHands();
            if (session.Game.State != GameState.Reward) { rewardPanel.SetActive(false); if (pickUi != null && !picking) pickUi.gameObject.SetActive(false); }
            overPanel.SetActive(session.Game.State == GameState.GameOver);
        }
        private void RebuildHands()
        {
            handDirty = false;
            foreach (var g in built) Destroy(g); built.Clear();
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
                var button = face.gameObject.AddComponent<Button>(); button.targetGraphic = face.GetComponent<Image>(); button.interactable = build;
                button.transition = Selectable.Transition.None; // keep the red card art during combat (no grey disabled tint)
                { var cp = face.gameObject.AddComponent<CardPointer>(); cp.Tower = true; cp.Index = idx; cp.Enabled = () => session.Game.State == GameState.Build; } // tap / drag (PlacementInputController)
                face.gameObject.AddComponent<CardHover>().Init(card, selected);
                if (data.icon != null) { var icon = Img(face, "Icon", data.icon, Color.white); TL(icon, 35, 15, 130, 130); icon.GetComponent<Image>().preserveAspect = true; }
                var price = Txt(face, session.Towers.Cost(data).ToString(), 36, Ink); TL(price.rectTransform, 20, 162, 130, 44);
                price.fontStyle = FontStyles.Bold; price.outlineWidth = .25f; price.outlineColor = new Color32(0x1E, 0x1A, 0x3A, 255); price.alignment = TextAlignmentOptions.Center;
                if (!PointerInput.TouchMode) { var hot = Img(face, "Hotkey", art ? art.uiBadgeHotkey : null, Color.white); TL(hot, 152, 170, 32, 32);
                var num = Txt(hot, (i + 1).ToString(), 22, new Color32(0x1E, 0x1A, 0x3A, 255)); Full(num.rectTransform); } // 1-4 badges: desktop only
                var size = TowerManager.SizeOf(data, 0);
                if (size != Vector2Int.one)
                {
                    var badgeSprite = art == null ? null : size.x == 2 && size.y == 2 ? art.uiBadgeSize2x2 : art.uiBadgeSize1x2;
                    var badge = Img(face, "Size", badgeSprite, badgeSprite != null ? Color.white : Red); TL(badge, 116, -14, 76, 36); // text baked in sprite
                    if (badgeSprite == null) { var t = Txt(badge, size.x + "x" + size.y, 22, Ink); Full(t.rectTransform); }
                }
                if (selected) Outline(face, Gold, 5);
                built.Add(card.gameObject); if (pic != null) pic.HandRects.Add(card);
            }
            // ---- block hand: ui_card_blueprint 128x128 (9-slice 24) + ui_icon_block_X 128x128 overlay (uniform, never per-shape scaling),
            // spacing 140, bottom-centre anchor; nudged right only if it would overlap the tower hand.
            var groups = session.Blocks.Hand.Groups();
            bool anyStack = groups.Exists(g => g.count > 1);
            // v16: card w = min(150, budget/7 - 12), budget = safeWidth - 24 - 512 - 24 (DRAW + BATTLE block on the right).
            // Row sits left-aligned on its own line above the tower hand (bottom 24+220+32) so it never overlaps the hand.
            float safeW = ((RectTransform)blockHand.parent).rect.width; if (safeW <= 0) safeW = 1920;
            float cardW = Mathf.Max(88, Mathf.Min(150, (safeW - 24 - 512 - 24) / 7f - 12));
            float sc = cardW / 128f, step = (cardW + 12) / sc * (anyStack ? 150f / 140f : 1f), left = 24, rowY = 24 + CH + 32;
            handCount.anchoredPosition = new Vector2(24, rowY + cardW + 8);
            if (PlacementInputController.Instance != null) { var cv = blockHand.GetComponentInParent<Canvas>(); float k = cv ? cv.scaleFactor : 1; PlacementInputController.Instance.CanvasScale = k; PlacementInputController.Instance.HandTop = (rowY + cardW + 8) * k; }
            int selectedIndex = session.Blocks.Hand.Selected;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var grp = groups[gi]; int idx = grp.first; var shape = grp.shape;
                bool selected = session.Towers.SelectedIndex < 0 && session.Blocks.Hand.Cards.Count > 0 && session.Blocks.Hand.Cards[selectedIndex] == shape && session.Blocks.Hand.Runes[selectedIndex] == grp.rune;
                var holder = Group(blockHand, "Block card " + shape.displayName); holder.anchorMin = holder.anchorMax = Vector2.zero; holder.pivot = Vector2.zero;
                holder.sizeDelta = new Vector2(128, 128); holder.localScale = Vector3.one * sc;
                holder.anchoredPosition = new Vector2(left + gi * step * sc, rowY + (selected ? 12 : 0));
                if (grp.count > 1 && S("ui_card_blueprint_stack") != null) { var st = Img(holder, "Stack", S("ui_card_blueprint_stack"), Color.white); BL(st, 0, -40, 160, 168); }
                var card = Img(holder, "Card", art ? art.uiCardBlueprint : null, art && art.uiCardBlueprint ? Color.white : Blueprint); Full(card);
                card.GetComponent<Image>().type = Image.Type.Sliced; card.GetComponent<Image>().raycastTarget = true;
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card.GetComponent<Image>(); b.transition = Selectable.Transition.None;
                b.interactable = build || (session.config.allowCombatBlocks && session.Game.State == GameState.Combat);
                { var cp = card.gameObject.AddComponent<CardPointer>(); cp.Tower = false; cp.Index = idx; cp.Enabled = () => b.interactable; }
                card.gameObject.AddComponent<CardHover>().Init(holder, selected);
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
                    var badge = Img(card, "Count", S("ui9_badge_count") ?? Rounded, S("ui9_badge_count") != null ? Color.white : Navy); TL(badge, 84, -12, 56, 40);
                    badge.GetComponent<Image>().type = Image.Type.Sliced;
                    var n = Txt(badge, "\u00D7" + grp.count, 26, Ink); Full(n.rectTransform); n.fontStyle = FontStyles.Bold; n.outlineWidth = .25f; n.outlineColor = Navy;
                }
                if (selected) Outline(card, Gold, 4);
                built.Add(holder.gameObject); if (pic != null) pic.HandRects.Add(holder);
            }
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
        private void OnState(GameState state) { handDirty = true; Refresh(); }
        private void ShowNotice(string message) { notice = Loc.Notice(message); noticeUntil = Time.unscaledTime + 2.4f; }
        private void Update()
        {
            if (session == null) return;
            if (hint != null) hint.text = Time.unscaledTime < noticeUntil ? notice : DefaultHint;
            if (Input.GetKeyDown(KeyCode.Space) && session.Game.State == GameState.Build) session.Waves.StartWave();
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
            var text = Txt(over, Loc.CoreDark, 64, Ink); Center(text.rectTransform, 0, -90, 1200, 90);
            var restart = Btn(over, Loc.Restart, 40, Orange, () => { TimeController.ResetAll(); SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); });
            Center((RectTransform)restart.transform, 0, 60, 300, 84);
            overPanel.SetActive(false);
        }
        private StoneSignal.UI.RewardPickUI pickUi; private StoneSignal.UI.RewardGlow pickGlow; private bool picking;
        public StoneSignal.UI.RewardPickUI PickUi => pickUi; public StoneSignal.UI.RewardGlow PickGlow => pickGlow;
        // v16.2 RewardPickUI (art): Show(options, rarity) -> OnPicked(i) -> PlayPick(i, target, onDone) -> Rewards.Choose(i).
        /// Diagnostics (gameplay shot): show the reward cards with forced rarities and placeholder content.
        public bool DebugShowRewardPick(StoneSignal.UI.RewardRarity[] rar)
        {
            debugRarity = rar; try { return ShowRewardPick(); } finally { debugRarity = null; }
        }
        private StoneSignal.UI.RewardRarity[] debugRarity;
        private bool ShowRewardPick()
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
            if (debugRarity != null)
            {
                // forced rarities, real content: Rare = a rune option (as in a real offer), other tiers = wave rewards from the actual pool
                var dopts = new StoneSignal.UI.RewardOption[debugRarity.Length]; var used = new HashSet<RewardData>();
                RewardEffect[][] pref = { new[] { RewardEffect.AddBlock, RewardEffect.WaveGold }, null, new[] { RewardEffect.AllAttackSpeed, RewardEffect.CannonRadius }, new[] { RewardEffect.AllDamage, RewardEffect.BonusSlot } };
                for (int i = 0; i < dopts.Length; i++)
                {
                    int ri = (int)debugRarity[i];
                    if (debugRarity[i] == StoneSignal.UI.RewardRarity.Rare) { dopts[i] = RuneOption(i % RuneRules.Names.Length); continue; }
                    RewardData pick = null;
                    foreach (var e in pref[ri]) foreach (var r in session.config.rewards) if (pick == null && r != null && r.effect == e && !used.Contains(r)) pick = r;
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
                if (rune != RuneRules.NoRune) { opts[i] = RuneOption(rune); rar[i] = StoneSignal.UI.RewardRarity.Rare; }
                else { opts[i] = RewardOption(session.Rewards.Choices[i]); rar[i] = StoneSignal.UI.RewardRarity.Common; } // rarity placeholder until gameplay assigns tiers
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
                case RewardEffect.BaseHP: case RewardEffect.WaveHeal: return art != null ? art.uiOrbCore : null;
                default: return S("ui_coin_gold"); // KillGold, WaveGold, NextWaveGold, TowerDiscount
            }
        }
        // Card text fit (art RewardPickUI untouched): title one line, auto-size 44 -> 26; description wraps inside the band, 28 -> 18.
        private void FitRewardText()
        {
            for (int i = 0; ; i++)
            {
                var card = pickUi.transform.Find("RewardCard" + i); if (card == null) break;
                var title = card.Find("Title")?.GetComponent<TMP_Text>();
                if (title != null) { title.enableWordWrapping = false; title.overflowMode = TextOverflowModes.Overflow; title.enableAutoSizing = true; title.fontSizeMin = 26; title.fontSizeMax = 44; }
                foreach (var t in card.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == "Desc") { t.enableWordWrapping = true; t.enableAutoSizing = true; t.fontSizeMin = 18; t.fontSizeMax = 28; t.overflowMode = TextOverflowModes.Overflow; }
            }
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
                if (rune != RuneRules.NoRune) { rewardNames[i].text = Loc.RuneTitle(rune); rewardDescriptions[i].text = "镶嵌此符文的墙块，其上的塔获得符文效果"; rewardEffects[i].text = Loc.RuneEffects[rune]; continue; }
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
