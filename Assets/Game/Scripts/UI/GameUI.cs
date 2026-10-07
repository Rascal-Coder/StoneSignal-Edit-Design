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
            hpNumber = Txt(orb, "", 64, Ink); Full(hpNumber.rectTransform); hpNumber.outlineWidth = .2f; hpNumber.outlineColor = Navy;
            hpSmall = Txt(root, "", 22, Ink); TL(hpSmall.rectTransform, 24, 178, 160, 30); hpSmall.outlineWidth = .25f; hpSmall.outlineColor = Navy;
            var pill = Img(root, "Gold", null, Gold); TL(pill, 210, 38, 215, 70); Outline(pill, Navy, 3);
            var coin = Img(pill, "Coin", null, Hex("E3A21A")); TL(coin, 10, 10, 50, 50); Outline(coin, Navy, 2);
            var dollar = Txt(coin, "$", 30, Navy); Full(dollar.rectTransform);
            gold = Txt(pill, "", 40, Navy); TL(gold.rectTransform, 66, 4, 140, 62); gold.alignment = TextAlignmentOptions.Center;
            // ---- top-centre wave banner
            var banner = Img(root, "Wave banner", null, Red); TC(banner, 0, 24, 420, 80); Outline(banner, Navy, 4);
            wave = Txt(banner, "", 44, Ink); Full(wave.rectTransform); wave.outlineWidth = .2f; wave.outlineColor = Navy;
            // ---- top-right speed + strike target
            string[] speeds = { "II", "x1", "x2", "x3" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i; var b = Btn(root, speeds[i], 34, Slate, () => SetSpeed(idx)); TR((RectTransform)b.transform, -(24 + (3 - i) * 96), 32, 84, 84); speedButtons[i] = b;
            }
            var target = Btn(root, "", 28, Slate, CycleTarget); TR((RectTransform)target.transform, -24, 130, 372, 64);
            targetLabel = target.GetComponentInChildren<TextMeshProUGUI>();
            // ---- bottom: hint pill, battle, draw deck
            var hintPill = Img(root, "Hint", null, new Color(.12f, .16f, .28f, .85f)); BC(hintPill, -100, 192, 670, 46);
            hint = Txt(hintPill, DefaultHint, 21, Ink); Full(hint.rectTransform);
            battle = Btn(root, "BATTLE  ►", 44, Orange, () => session.Waves.StartWave()); BR((RectTransform)battle.transform, -24, 186, 270, 90);
            var deck = new GameObject("Draw deck", typeof(RectTransform)).GetComponent<RectTransform>(); deck.SetParent(root, false); BR(deck, -260, 24, 170, 180);
            for (int i = 3; i >= 1; i--) { var layer = Img(deck, "Layer", null, Color.Lerp(Red, Navy, .45f)); Full(layer); layer.anchoredPosition = new Vector2(i * 4, -i * 5); Outline(layer, Navy, 2); }
            var top = Img(deck, "Top", null, Red); Full(top); Outline(top, Navy, 3);
            var drawTitle = Txt(top, "DRAW", 36, Ink); TL(drawTitle.rectTransform, 0, 16, 170, 44); drawTitle.outlineWidth = .2f; drawTitle.outlineColor = Navy;
            drawLeft = Txt(top, "", 22, Ink); TL(drawLeft.rectTransform, 0, 58, 170, 30);
            var costPill = Img(top, "Deck", null, Gold); TL(costPill, 35, 118, 100, 40); Outline(costPill, Navy, 2);
            drawCost = Txt(costPill, "", 26, Navy); Full(drawCost.rectTransform);
            var handCanvas = Canvas("Hand", 1);
            towerHand = Group(handCanvas, "Tower hand"); BL(towerHand, 20, 20, 720, 210);
            blockHand = Group(handCanvas, "Block hand"); BL(blockHand, 700, 22, 760, 140);
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
            for (int i = 0; i < towers.Length; i++)
            {
                int idx = i; var data = towers[i];
                bool selected = session.Towers.SelectedIndex == i;
                var card = new GameObject("Tower card " + data.displayName, typeof(RectTransform)).GetComponent<RectTransform>();
                card.SetParent(towerHand, false); BL(card, i * 150, selected ? 30 : 0, 165, 178);
                card.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(5, -5, towers.Length > 1 ? i / (float)(towers.Length - 1) : .5f)); // fanned +-5 deg
                var shadow = Img(card, "Shadow", art ? art.uiCardTower : null, Shadow); Full(shadow); shadow.anchoredPosition = new Vector2(6, -8);
                var face = Img(card, "Frame", art ? art.uiCardTower : null, art && art.uiCardTower ? Color.white : Red); Full(face);
                var button = face.gameObject.AddComponent<Button>(); button.targetGraphic = face.GetComponent<Image>(); button.interactable = build;
                button.onClick.AddListener(() => session.Towers.Select(session.Towers.SelectedIndex == idx ? -1 : idx));
                if (data.icon != null) { var icon = Img(face, "Icon", data.icon, Color.white); Center(icon, 0, 14, 104, 104); icon.GetComponent<Image>().preserveAspect = true; }
                var price = Txt(face, session.Towers.Cost(data).ToString(), 40, Ink); BCs(price.rectTransform, 0, 10, 120, 46); price.outlineWidth = .25f; price.outlineColor = Navy;
                var hot = Img(face, "Hotkey", art ? art.uiBadgeHotkey : null, Navy); BRs(hot, -6, 8, 34, 34);
                var num = Txt(hot, (i + 1).ToString(), 20, Ink); Full(num.rectTransform);
                var size = TowerManager.SizeOf(data, 0);
                if (size != Vector2Int.one)
                {
                    var badgeSprite = art == null ? null : size.x == 2 && size.y == 2 ? art.uiBadgeSize2x2 : art.uiBadgeSize1x2;
                    var badge = Img(card, "Size", badgeSprite, Red); TL(badge, -4, -26, 74, 34);
                    if (badgeSprite == null) { var t = Txt(badge, size.x + "X" + size.y, 20, Ink); Full(t.rectTransform); }
                }
                if (selected) Outline(face, Gold, 5);
                built.Add(card.gameObject);
            }
            var hand = session.Blocks.Hand.Cards;
            for (int i = 0; i < hand.Count; i++)
            {
                int idx = i; var shape = hand[i];
                bool selected = session.Towers.SelectedIndex < 0 && i == session.Blocks.Hand.Selected;
                var card = Img(blockHand, "Block card " + shape.displayName, art ? art.uiCardBlueprint : null, art && art.uiCardBlueprint ? Color.white : Blueprint);
                BL(card, i * 150, selected ? 12 : 0, 131, 131);
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card.GetComponent<Image>();
                b.interactable = build || (session.config.allowCombatBlocks && session.Game.State == GameState.Combat);
                b.onClick.AddListener(() => { session.Towers.Select(-1); session.Blocks.SelectCard(idx); });
                // shape only: centred mini cells
                var cells = shape.cells; if (cells == null || cells.Length == 0) continue;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var c in cells) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
                float s = Mathf.Min(80f / (maxX - minX + 1), 80f / (maxY - minY + 1), 26);
                Vector2 off = new Vector2((maxX + minX) * .5f, (maxY + minY) * .5f);
                foreach (var c in cells) { var cell = Img(card, "Cell", null, new Color(.86f, .93f, 1f)); Center(cell, (c.x - off.x) * s, -(c.y - off.y) * s, s - 3, s - 3); }
                if (selected) Outline(card, Gold, 4);
                built.Add(card.gameObject);
            }
        }
        private void SetSpeed(int index)
        {
            StoneSignal.VFX.HitStop.Cancel();
            if (index == 0) paused = !paused; else { paused = false; speedIndex = index; }
            Time.timeScale = paused ? 0 : speedIndex;
            for (int i = 0; i < 4; i++) speedButtons[i].GetComponent<Image>().color = (i == 0 ? paused : !paused && i == speedIndex) ? Blue : Slate;
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
        private static Button Btn(Transform parent, string label, float size, Color color, Action action)
        {
            var r = Img(parent, label, null, color); r.GetComponent<Image>().raycastTarget = true; Outline(r, Navy, 3);
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>(); b.onClick.AddListener(() => action());
            var t = Txt(r, label, size, Ink); Full(t.rectTransform); return b;
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
