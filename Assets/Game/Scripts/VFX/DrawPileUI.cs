using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal.VFX
{
    public enum DrawPileState { Free, Ad, Used, Full }

    /// DRAW pile (art v16). Visual only: game code owns the rule "2 draws per wave: 1st free, 2nd rewarded ad"
    /// and calls SetState / SetStackCount / PlayDrawPulse. Card face carries no price; status lives in the pill below.
    /// Layout (1920x1080 ref): root 200x268, card 124x160 at (30,8) top-left, layer step (+4,+7), pill 172x60 at bottom.
    public class DrawPileUI : MonoBehaviour
    {
        [Header("Sprites (HUD atlas)")]
        public Sprite topCard;        // ui_draw_pile
        public Sprite layerCard;      // ui_draw_pile_layer
        public Sprite pillSprite;     // ui9_draw_bubble (9-slice L30 B28 R30 T28)
        public Sprite tailSprite;     // ui_draw_bubble_tail
        public Sprite freeIcon;       // ui_icon_free
        public Sprite adIcon;         // ui_badge_video_ad

        [Header("Refs (auto-built if empty)")]
        public RectTransform stackRoot;
        public Image top;
        public RectTransform pill;    // bobs
        public Image pillImage, tail, statusIcon;
        public TMPro.TMP_Text statusLabel;
        public Button button;

        [Header("Tuning")]
        public Vector2 cardSize = new Vector2(124, 160);
        public Vector2 layerStep = new Vector2(4, -7);
        public float bobAmplitude = 4f, bobPeriod = 1.6f;
        public Color textColor = new Color32(0x1E, 0x1A, 0x3A, 0xFF);
        [Tooltip("Hand counter label near the block row, e.g. 5/7 (optional)")] public TMPro.TMP_Text handCountLabel;
        public Color usedTint = new Color(.55f, .55f, .6f, .85f);

        public DrawPileState State { get; private set; } = DrawPileState.Free;
        Image[] layers = new Image[4];
        int stackCount = 5;
        float pulseT = -1f; Vector2 pillBase; bool built;

        void Awake() { Build(); SetStackCount(stackCount); SetState(State); }

        void Build()
        {
            if (built) return; built = true;
            var rt = (RectTransform)transform;
            if (rt.sizeDelta == Vector2.zero) rt.sizeDelta = new Vector2(200, 268);
            if (!stackRoot) stackRoot = NewRect("Stack", rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -8), cardSize);
            for (int i = 3; i >= 0; i--)   // back to front; layer i+1 below top
            {
                var img = NewImage("Layer" + (i + 1), stackRoot, layerCard);
                ((RectTransform)img.transform).anchoredPosition = layerStep * (i + 1);
                layers[i] = img;
            }
            if (!top) top = NewImage("Top", stackRoot, topCard);
            if (!pill)
            {
                tail = NewImage("Tail", rt, tailSprite);
                Place(tail.rectTransform, new Vector2(.5f, 0), new Vector2(0, 61), new Vector2(26, 15));
                pillImage = NewImage("Pill", rt, pillSprite); pillImage.type = Image.Type.Sliced;
                pill = pillImage.rectTransform; Place(pill, new Vector2(.5f, 0), new Vector2(0, 34), new Vector2(172, 60));
                tail.transform.SetParent(pill, true);
                statusIcon = NewImage("Icon", pill, freeIcon);
                Place(statusIcon.rectTransform, new Vector2(0, .5f), new Vector2(36, 2), new Vector2(40, 40));
                var lt = new GameObject("Label", typeof(RectTransform)).AddComponent<TMPro.TextMeshProUGUI>();
                lt.transform.SetParent(pill, false); Place(lt.rectTransform, new Vector2(.5f, .5f), new Vector2(18, 2), new Vector2(110, 40));
                lt.fontSize = 24; lt.fontStyle = TMPro.FontStyles.Bold; lt.alignment = TMPro.TextAlignmentOptions.Center; lt.raycastTarget = false;
                statusLabel = lt;
            }
            if (statusLabel) statusLabel.color = textColor;
            if (!button) { button = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.None; }
            var hit = GetComponent<Image>(); if (!hit) { hit = gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); } // full 200x268 touch target
            pillBase = pill.anchoredPosition;
        }

        /// Full = block hand at max (7/7): greyed, label FULL, draws remain for later.
        /// Free = first draw of the wave, Ad = second draw (rewarded video placeholder), Used = both spent (disabled).
        public void SetState(DrawPileState s)
        {
            Build(); State = s;
            bool used = s == DrawPileState.Used || s == DrawPileState.Full;
            if (statusIcon) { statusIcon.enabled = !used; statusIcon.sprite = s == DrawPileState.Free ? freeIcon : adIcon; }
            if (statusLabel) statusLabel.text = s == DrawPileState.Free ? "FREE" : s == DrawPileState.Ad ? "DRAW" : s == DrawPileState.Full ? "FULL" : "0 / 2";
            if (statusLabel) statusLabel.rectTransform.anchoredPosition = new Vector2(used ? 0 : 18, 2);
            var tint = used ? usedTint : Color.white;
            if (top) top.color = tint; if (pillImage) pillImage.color = tint; if (tail) tail.color = tint;
            for (int i = 0; i < layers.Length; i++) if (layers[i]) layers[i].color = used ? usedTint : Color.Lerp(Color.white, new Color(.7f, .7f, .8f), .08f * (i + 1));
            if (button) button.interactable = !used;
        }

        /// Visible card layers including the top card, clamped 3..5.
        public void SetStackCount(int visibleLayers)
        {
            Build(); stackCount = Mathf.Clamp(visibleLayers, 3, 5);
            for (int i = 0; i < layers.Length; i++) if (layers[i]) layers[i].enabled = i < stackCount - 1;
        }

        /// Updates the optional hand counter ("5/7"); turns red-ish when full.
        public void SetHandCount(int count, int max = 7)
        {
            if (!handCountLabel) return;
            handCountLabel.text = count + "/" + max;
            handCountLabel.color = count >= max ? new Color32(0xE5, 0x48, 0x4D, 0xFF) : (Color)new Color32(0xFF, 0xEC, 0xBE, 0xFF);
        }

        /// Short squash/pop on the top card + pill when a draw happens (0.35 s).
        public void PlayDrawPulse() { pulseT = 0f; }

        void Update()
        {
            float t = Time.unscaledTime;
            if (pill) pill.anchoredPosition = pillBase + ((State == DrawPileState.Used || State == DrawPileState.Full) ? Vector2.zero : Vector2.up * Mathf.Sin(t * Mathf.PI * 2f / bobPeriod) * bobAmplitude);
            if (pulseT >= 0f)
            {
                pulseT += Time.unscaledDeltaTime; float k = Mathf.Clamp01(pulseT / .35f);
                float s = 1f + Mathf.Sin(k * Mathf.PI) * .12f * (1f - k * .5f);
                if (top) { top.rectTransform.localScale = new Vector3(s, 2f - s, 1); top.rectTransform.anchoredPosition = Vector2.up * Mathf.Sin(k * Mathf.PI) * 14f; }
                if (pill) pill.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * .08f);
                if (k >= 1f) { pulseT = -1f; if (top) { top.rectTransform.localScale = Vector3.one; top.rectTransform.anchoredPosition = Vector2.zero; } if (pill) pill.localScale = Vector3.one; }
            }
        }

        static RectTransform NewRect(string n, Transform p, Vector2 amin, Vector2 amax, Vector2 pos, Vector2 size)
        {
            var r = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(p, false);
            r.anchorMin = amin; r.anchorMax = amax; r.pivot = new Vector2(0, 1); r.anchoredPosition = pos; r.sizeDelta = size; return r;
        }
        Image NewImage(string n, Transform p, Sprite s)
        {
            var r = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(p, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.sizeDelta = cardSize;
            var img = r.gameObject.AddComponent<Image>(); img.sprite = s; img.raycastTarget = false; return img;
        }
        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size) { r.anchorMin = r.anchorMax = r.pivot = anchor; r.anchoredPosition = pos; r.sizeDelta = size; }
    }
}
