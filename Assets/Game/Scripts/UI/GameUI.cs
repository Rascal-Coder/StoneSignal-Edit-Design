using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace StoneSignal
{
    // In-game HUD per Docs/ui_mockup_v3 (art v11). One overlay canvas for static HUD + one for the hand (dirty rebuilds only),
    // TextMeshPro text, sprites from ArtCatalog (Assets/Game/Art/Stylized/UI, packed in one SpriteAtlas). No debug text.
    // Hand card hover: scale 1.06 and +12 px (ui_card_spec_v13).
    public sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        RectTransform card; Vector2 rest; bool lifted;
        public void Init(RectTransform target, bool alreadyLifted) { card = target; rest = target.anchoredPosition; lifted = alreadyLifted; }
        public void OnPointerEnter(PointerEventData e) { card.localScale = Vector3.one * 1.06f; card.anchoredPosition = rest + (lifted ? Vector2.zero : new Vector2(0, 12)); }
        public void OnPointerExit(PointerEventData e) { card.localScale = Vector3.one; card.anchoredPosition = rest; }
    }
    public sealed class GameUI : MonoBehaviour
    {
        private GameBootstrap session;
        private ArtCatalog art;
        private TextMeshProUGUI hpNumber, hpSmall, gold, wave, hint, targetLabel, drawLeft, drawCost;
        private Button battle;
        private readonly Button[] speedButtons = new Button[4];
        private RectTransform towerHand, blockHand;
        private readonly List<GameObject> built = new List<GameObject>();
        private GameObject rewardPanel, overPanel;
        private readonly TextMeshProUGUI[] rewardNames = new TextMeshProUGUI[3], rewardDescriptions = new TextMeshProUGUI[3], rewardEffects = new TextMeshProUGUI[3];
        private float noticeUntil; private string notice; private bool handDirty = true;
        private int speedIndex = 1; private bool paused;
        private const string DefaultHint = "R rotate  ·  RMB cancel  ·  ghost shows valid / blocked";

        static readonly Color Ink = Color.white, Navy = Hex("1E2A4A"), Slate = Hex("3B4566"), Blue = Hex("2F5FD0"),
            Red = Hex("D9404A"), Gold = Hex("F7C948"), Orange = Hex("F59A3A"), Blueprint = Hex("2A5DB0"), Shadow = new Color(0, 0, 0, .35f);

        public void Initialize(GameBootstrap game)
        {
            session = game; art = game.config.palette != null ? game.config.palette.art : null;
            if (EventSystem.current == null) { var es = new GameObject("Event system"); es.transform.SetParent(transform); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }
            var root = Canvas("HUD", 0);
            // ---- top-left: core orb (HP number only) + small HP/Max below; gold pill
            var orb = Img(root, "Core orb", art ? art.uiOrbCore : null, Hex("3A8FE0")); TL(orb, 24, 18, 160, 160);
            var glow = Img(orb, "Orb light", Circle, new Color(.75f, .93f, 1f, .42f)); Center(glow, 0, 0, 118, 118); // brighter, lighter core
            hpNumber = Txt(orb, "", 64, Ink); Full(hpNumber.rectTransform); hpNumber.outlineWidth = .2f; hpNumber.outlineColor = Navy;
            hpSmall = Txt(root, "", 22, Ink); TL(hpSmall.rectTransform, 24, 178, 160, 30); hpSmall.outlineWidth = .25f; hpSmall.outlineColor = Navy;
            var pill = Panel(root, "Gold", "ui9_pill_gold", Gold); TL(pill, 210, 36, 230, 76);
            var coinRim = Img(pill, "Coin rim", Circle, Navy); TL(coinRim, 12, 10, 54, 54);
            var coin = Img(coinRim, "Coin", Circle, Hex("FFC83D")); Center(coin, 0, 0, 44, 44);
            var dollar = Txt(coin, "$", 28, Hex("8A5A00")); Full(dollar.rectTransform);
            gold = Txt(pill, "", 42, Navy); TL(gold.rectTransform, 70, 4, 146, 64); gold.alignment = TextAlignmentOptions.Center;
            // ---- top-centre wave banner
            var banner = Panel(root, "Wave banner", "ui9_banner_wave_red", Red); TC(banner, 0, 20, 440, 88);
            wave = Txt(banner, "", 44, Ink); Full(wave.rectTransform); wave.outlineWidth = .2f; wave.outlineColor = Navy;
            // ---- top-right speed + strike target
            string[] speeds = { "II", "x1", "x2", "x3" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i; var b = Btn(root, speeds[i], 34, "ui9_button_navy_normal", Slate, () => SetSpeed(idx)); TR((RectTransform)b.transform, -(24 + (3 - i) * 96), 28, 86, 86); speedButtons[i] = b;
            }
            var target = Btn(root, "", 28, "ui9_button_navy_normal", Slate, CycleTarget); TR((RectTransform)target.transform, -24, 126, 374, 68);
            targetLabel = target.GetComponentInChildren<TextMeshProUGUI>();
            // ---- bottom: hint pill, battle, draw deck
            var hintPill = Panel(root, "Hint", "ui9_panel_navy", new Color(.12f, .16f, .28f, .85f)); BL(hintPill, 884, 168, 620, 48);
            hint = Txt(hintPill, DefaultHint, 21, Ink); Full(hint.rectTransform);
            // DRAW deck: 4 offset card layers (card-edge thickness) under the top card; BATTLE sits above it
            var deck = new GameObject("Draw deck", typeof(RectTransform)).GetComponent<RectTransform>(); deck.SetParent(root, false); BR(deck, -70, 30, 176, 200);
            var pile = S("ui_draw_pile");
            for (int i = 4; i >= 1; i--) { var layer = Img(deck, "Layer", pile ?? Rounded, Color.Lerp(Color.white, Navy, .18f + i * .1f)); Full(layer); layer.anchoredPosition = new Vector2(i * 3, -i * 5); if (pile == null) layer.GetComponent<Image>().type = Image.Type.Sliced; }
            var top = Img(deck, "Top", pile ?? Rounded, pile != null ? Color.white : Red); Full(top); if (pile == null) top.GetComponent<Image>().type = Image.Type.Sliced;
            var drawTitle = Txt(top, "DRAW", 36, Ink); TL(drawTitle.rectTransform, 0, 22, 176, 44); drawTitle.outlineWidth = .2f; drawTitle.outlineColor = Navy;
            drawLeft = Txt(top, "", 22, Ink); TL(drawLeft.rectTransform, 0, 64, 176, 30);
            var costPill = Panel(top, "Deck", "ui9_pill_gold", Gold); TL(costPill, 38, 128, 100, 42);
            drawCost = Txt(costPill, "", 26, Navy); Full(drawCost.rectTransform);
            battle = Btn(root, "BATTLE  ►", 46, "ui9_button_battle_orange", Orange, () => session.Waves.StartWave()); BR((RectTransform)battle.transform, -24, 246, 290, 100);
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
            gold.text = session.Economy.Gold.ToString();
            wave.text = "WAVE " + Mathf.Min(session.Waves.WaveIndex + 1, Mathf.Max(1, session.config.waves.Length)) + " / " + session.config.waves.Length;
            drawLeft.text = session.Blocks.Remaining + " left";
            drawCost.text = session.Blocks.Deck != null ? session.Blocks.Deck.Cards.Count.ToString() : "-";
            targetLabel.text = "TARGET: " + session.Enemies.Targeting.ToString().ToUpper() + "  ►";
            battle.interactable = session.Game.State == GameState.Build;
            if (handDirty) RebuildHands();
            if (session.Game.State != GameState.Reward) rewardPanel.SetActive(false);
            overPanel.SetActive(session.Game.State == GameState.GameOver);
        }
        private void RebuildHands()
        {
            handDirty = false;
            foreach (var g in built) Destroy(g); built.Clear();
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
                card.anchoredPosition = new Vector2(24 + CW * .5f + i * 212, 24 + (selected ? 12 : 0));
                card.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(4, -4, u));
                var face = Img(card, "Frame", art ? art.uiCardTower : null, art && art.uiCardTower ? Color.white : Red); Full(face);
                face.GetComponent<Image>().type = Image.Type.Sliced; // 9-slice L28 B64 R28 T28 (sprite borders from art .meta)
                face.GetComponent<Image>().raycastTarget = true;
                var button = face.gameObject.AddComponent<Button>(); button.targetGraphic = face.GetComponent<Image>(); button.interactable = build;
                button.transition = Selectable.Transition.None; // keep the red card art during combat (no grey disabled tint)
                button.onClick.AddListener(() => session.Towers.Select(session.Towers.SelectedIndex == idx ? -1 : idx));
                face.gameObject.AddComponent<CardHover>().Init(card, selected);
                if (data.icon != null) { var icon = Img(face, "Icon", data.icon, Color.white); TL(icon, 35, 15, 130, 130); icon.GetComponent<Image>().preserveAspect = true; }
                var price = Txt(face, session.Towers.Cost(data).ToString(), 36, Ink); TL(price.rectTransform, 20, 162, 130, 44);
                price.fontStyle = FontStyles.Bold; price.outlineWidth = .25f; price.outlineColor = new Color32(0x1E, 0x1A, 0x3A, 255); price.alignment = TextAlignmentOptions.Center;
                var hot = Img(face, "Hotkey", art ? art.uiBadgeHotkey : null, Color.white); TL(hot, 152, 170, 32, 32);
                var num = Txt(hot, (i + 1).ToString(), 22, new Color32(0x1E, 0x1A, 0x3A, 255)); Full(num.rectTransform);
                var size = TowerManager.SizeOf(data, 0);
                if (size != Vector2Int.one)
                {
                    var badgeSprite = art == null ? null : size.x == 2 && size.y == 2 ? art.uiBadgeSize2x2 : art.uiBadgeSize1x2;
                    var badge = Img(face, "Size", badgeSprite, badgeSprite != null ? Color.white : Red); TL(badge, 116, -14, 76, 36); // text baked in sprite
                    if (badgeSprite == null) { var t = Txt(badge, size.x + "x" + size.y, 22, Ink); Full(t.rectTransform); }
                }
                if (selected) Outline(face, Gold, 5);
                built.Add(card.gameObject);
            }
            // ---- block hand: ui_card_blueprint 128x128 (9-slice 24) + ui_icon_block_X 128x128 overlay (uniform, never per-shape scaling),
            // spacing 140, bottom-centre anchor; nudged right only if it would overlap the tower hand.
            var hand = session.Blocks.Hand.Cards;
            float towerRight = 24 + (towers.Length - 1) * 212 + CW + 16;
            float span = hand.Count * 140 - 12, left = Mathf.Max(960 - span * .5f, towerRight);
            for (int i = 0; i < hand.Count; i++)
            {
                int idx = i; var shape = hand[i];
                bool selected = session.Towers.SelectedIndex < 0 && i == session.Blocks.Hand.Selected;
                var card = Img(blockHand, "Block card " + shape.displayName, art ? art.uiCardBlueprint : null, art && art.uiCardBlueprint ? Color.white : Blueprint);
                card.GetComponent<Image>().type = Image.Type.Sliced; card.GetComponent<Image>().raycastTarget = true;
                card.anchorMin = card.anchorMax = new Vector2(.5f, 0); card.pivot = new Vector2(0, 0); card.sizeDelta = new Vector2(128, 128);
                card.anchoredPosition = new Vector2(left - 960 + i * 140, 24 + (selected ? 12 : 0));
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card.GetComponent<Image>(); b.transition = Selectable.Transition.None;
                b.interactable = build || (session.config.allowCombatBlocks && session.Game.State == GameState.Combat);
                b.onClick.AddListener(() => { session.Towers.Select(-1); session.Blocks.SelectCard(idx); });
                card.gameObject.AddComponent<CardHover>().Init(card, selected);
                var iconSprite = BlockIcon(shape);
                if (iconSprite != null) { var ic = Img(card, "Shape", iconSprite, Color.white); Full(ic); }
                else
                {
                    // fallback: 22px cells on a 4x4 grid (88x88 area), same scale for every shape
                    var cells = shape.cells; if (cells == null || cells.Length == 0) { built.Add(card.gameObject); continue; }
                    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                    foreach (var c in cells) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
                    Vector2 off = new Vector2((maxX + minX) * .5f, (maxY + minY) * .5f);
                    foreach (var c in cells) { var cell = Img(card, "Cell", null, new Color(.86f, .93f, 1f)); Center(cell, (c.x - off.x) * 22, -(c.y - off.y) * 22, 20, 20); }
                }
                if (selected) Outline(card, Gold, 4);
                built.Add(card.gameObject);
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
            StoneSignal.VFX.HitStop.Cancel();
            if (index == 0) paused = !paused; else { paused = false; speedIndex = index; }
            Time.timeScale = paused ? 0 : speedIndex;
            for (int i = 0; i < 4; i++)
            {
                bool on = i == 0 ? paused : !paused && i == speedIndex; var img = speedButtons[i].GetComponent<Image>();
                var sel = S("ui9_button_navy_selected"); var normal = S("ui9_button_navy_normal");
                if (sel != null && normal != null) { img.sprite = on ? sel : normal; img.color = Color.white; } else img.color = on ? Blue : Slate;
            }
        }
        private void CycleTarget() { session.Enemies.Targeting = (TargetMode)(((int)session.Enemies.Targeting + 1) % Enum.GetValues(typeof(TargetMode)).Length); Refresh(); }
        private void OnState(GameState state) { handDirty = true; Refresh(); }
        private void ShowNotice(string message) { notice = message; noticeUntil = Time.unscaledTime + 2.4f; }
        private void Update()
        {
            if (session == null) return;
            hint.text = Time.unscaledTime < noticeUntil ? notice : DefaultHint;
            if (Input.GetKeyDown(KeyCode.Space) && session.Game.State == GameState.Build) session.Waves.StartWave();
        }

        // ---------- reward / game over ----------
        private void BuildRewardPanel(RectTransform root)
        {
            var panel = Img(root, "Rewards", null, new Color(.06f, .09f, .18f, .88f)); Full(panel); rewardPanel = panel.gameObject;
            var title = Txt(panel, "CHOOSE AN UPGRADE", 52, Ink); Center(title.rectTransform, 0, -300, 1100, 70); title.outlineWidth = .2f; title.outlineColor = Navy;
            for (int i = 0; i < 3; i++)
            {
                int idx = i; var card = Img(panel, "Reward " + i, null, Blueprint); Center(card, (i - 1) * 380, 20, 340, 400); Outline(card, Navy, 4);
                rewardNames[i] = Txt(card, "", 32, Ink); TL(rewardNames[i].rectTransform, 20, 24, 300, 90);
                rewardDescriptions[i] = Txt(card, "", 22, new Color(.86f, .93f, 1f)); TL(rewardDescriptions[i].rectTransform, 20, 120, 300, 130);
                rewardEffects[i] = Txt(card, "", 26, Gold); TL(rewardEffects[i].rectTransform, 20, 250, 300, 50);
                var choose = Btn(card, "CHOOSE", 30, Orange, () => session.Rewards.Choose(idx)); TL((RectTransform)choose.transform, 40, 316, 260, 64);
            }
            rewardPanel.SetActive(false);
            var over = Img(root, "Game over", null, new Color(.05f, .06f, .12f, .92f)); Full(over); overPanel = over.gameObject;
            var text = Txt(over, "THE CORE WENT DARK", 64, Ink); Center(text.rectTransform, 0, -90, 1200, 90);
            var restart = Btn(over, "RESTART", 40, Orange, () => { StoneSignal.VFX.HitStop.Cancel(); Time.timeScale = 1; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); });
            Center((RectTransform)restart.transform, 0, 60, 300, 84);
            overPanel.SetActive(false);
        }
        private void ShowRewards()
        {
            for (int i = 0; i < 3; i++) { var r = session.Rewards.Choices[i]; rewardNames[i].text = r.displayName; rewardDescriptions[i].text = r.description; rewardEffects[i].text = r.effectText; }
            rewardPanel.SetActive(true); Refresh();
        }

        // ---------- builders ----------
        private RectTransform Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.GetComponent<CanvasScaler>(); s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920, 1080); s.matchWidthOrHeight = .5f;
            return go.GetComponent<RectTransform>();
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
        private Button Btn(Transform parent, string label, float size, string sprite, Color color, Action action)
        {
            var r = Panel(parent, label, sprite, color); r.GetComponent<Image>().raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>(); b.onClick.AddListener(() => action());
            var pressed = sprite != null ? S(sprite.Replace("_normal", "_pressed")) : null;
            if (pressed != null && sprite.EndsWith("_normal")) { b.transition = Selectable.Transition.SpriteSwap; b.spriteState = new SpriteState { pressedSprite = pressed }; }
            var colors = b.colors; colors.disabledColor = new Color(.8f, .8f, .8f, 1); b.colors = colors;
            var t = Txt(r, label, size, Ink); Full(t.rectTransform); t.rectTransform.offsetMin = new Vector2(0, 6); t.outlineWidth = .18f; t.outlineColor = Navy; return b;
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
