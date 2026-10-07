using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal.UI
{
    /// v17.6 art: chunky toon toast for GameUI's notice pill ("这里不能放" ...), applied from outside GameUI (one call after the pill is built).
    ///  - ui9_toast 9-slice (navy #2A2F5A, ink stroke, cream outline, navy edge, baked hard shadow): pill 72 ref px tall + 6 px shadow below
    ///    (+3 px right) -> rect height 78; border L40 B41 R43 T35 (pinned by StylizedFxV14.BuildAtlas).
    ///  - optional 44 px left icon: ui_icon_warn (red, invalid actions) / ui_icon_info (gold, neutral info), picked from the text (KindFor).
    ///  - label: StoneSignalRoundedCN-Heavy 30 px white, outline #1E1A3A 0.25; padding 28 each side (+44 icon + 10 gap).
    ///  - motion (ToastFx): pop-in scale 0.85 -> 1.05 -> 1 + fade over 0.18 s, hold 1.2 s, fade-out 0.2 s while rising 12 px. Unscaled time.
    /// GameUI keeps ownership of WHEN / WHERE the toast shows (rules, slot placement, SetActive); ToastFx only animates scale / alpha / a
    /// rise offset on top of the position GameUI sets, restarts on every new text / re-show, and keeps the pill width = text + padding.
    public static class ToastStyle
    {
        public const string PillSprite = "ui9_toast", WarnSprite = "ui_icon_warn", InfoSprite = "ui_icon_info";
        public const float PillHeight = 72f, ShadowX = 3f, ShadowY = 6f, PadX = 28f, IconSize = 44f, IconGap = 10f;
        public const float FontSize = 30f, OutlineWidth = .25f, MinWidth = 200f, MaxWidth = 900f;
        public const float RectHeight = PillHeight + ShadowY;
        public static readonly Color TextColor = Color.white;
        public static readonly Color32 OutlineColor = new Color32(0x1E, 0x1A, 0x3A, 255);
        public enum Kind { None, Warn, Info }

        /// Neutral notices (raw English keys and their Loc.Notice texts) -> gold info icon; everything else is an invalid action -> red warn.
        static readonly HashSet<string> InfoTexts = new HashSet<string>
        {
            "Tower ready", "Placed. Route recalculated.", "No blocks left. Start the next wave.",
            "建造完成", "已放置，路线已更新", "没有墙牌了，开始下一波吧",
        };
        public static Kind KindFor(string message) => string.IsNullOrEmpty(message) ? Kind.None : InfoTexts.Contains(message) ? Kind.Info : Kind.Warn;

        /// Pill width around the label's preferred width: padding both sides + right shadow + icon slot.
        public static float ExtraWidth(bool icon) => PadX * 2f + ShadowX + (icon ? IconSize + IconGap : 0f);

        /// Restyles an existing notice pill (Image + TMP label child) in place and adds the ToastFx animator. Safe to call once after
        /// the pill is built (call AFTER UseCn(label) so the outline lands on the CN material instance). sprite = name -> Sprite
        /// resolver (GameUI: S). Missing ui9_toast keeps the current sprite (only text / motion change).
        public static ToastFx Apply(RectTransform pill, TMP_Text label, System.Func<string, Sprite> sprite)
        {
            if (pill == null || label == null) return null;
            var img = pill.GetComponent<Image>(); var sp = sprite != null ? sprite(PillSprite) : null;
            if (img != null && sp != null)
            {
                img.sprite = sp; img.type = Image.Type.Sliced; img.color = Color.white; img.pixelsPerUnitMultiplier = 1f;
                var edge = pill.Find("Edge"); if (edge != null) edge.gameObject.SetActive(false);   // generated placeholder ring
            }
            if (img != null) img.raycastTarget = false;
            pill.sizeDelta = new Vector2(pill.sizeDelta.x, RectHeight);
            label.fontSize = FontSize; label.enableAutoSizing = false; label.color = TextColor;
            label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.Center;
            label.outlineWidth = OutlineWidth; label.outlineColor = OutlineColor; label.raycastTarget = false;
            var iconRt = pill.Find("Icon") as RectTransform;
            if (iconRt == null)
            {
                var go = new GameObject("Icon", typeof(RectTransform), typeof(Image)); iconRt = (RectTransform)go.transform;
                iconRt.SetParent(pill, false); iconRt.SetAsLastSibling();
            }
            var icon = iconRt.GetComponent<Image>(); icon.raycastTarget = false; icon.preserveAspect = true;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0, 0); iconRt.pivot = new Vector2(0, 0);
            iconRt.anchoredPosition = new Vector2(PadX, ShadowY + (PillHeight - IconSize) * .5f); iconRt.sizeDelta = new Vector2(IconSize, IconSize);
            var fx = pill.GetComponent<ToastFx>(); if (fx == null) fx = pill.gameObject.AddComponent<ToastFx>();
            fx.Init(label, icon, sprite != null ? sprite(WarnSprite) : null, sprite != null ? sprite(InfoSprite) : null);
            return fx;
        }
    }

    /// v17.6 toast motion + layout (see ToastStyle). Lives on the notice pill; GameUI still drives SetActive / text / slot position.
    [DisallowMultipleComponent]
    public sealed class ToastFx : MonoBehaviour
    {
        [Tooltip("pop-in: scale 0.85 -> 1.05 (60 %) -> 1 + fade in")] public float popIn = .18f;
        public float hold = 1.2f, fadeOut = .2f;
        [Tooltip("ref px risen during the fade-out")] public float rise = 12f;
        public float scaleFrom = .85f, scaleOver = 1.05f;
        [Tooltip("pick warn / info from the text (ToastStyle.KindFor); off = keep SetKind()")] public bool autoKind = true;
        /// Visible life of one toast; GameUI.NoticeSeconds should match (1.58 s) so the fade-out is not cut.
        public float TotalSeconds => popIn + hold + fadeOut;
        public float Elapsed => t;
        public ToastStyle.Kind Kind => kind;

        TMP_Text label; Image icon; Sprite warn, info; CanvasGroup group; RectTransform rt;
        float t, lastWidth = -1f; string lastText; Vector2 basePos, written; bool hasBase; ToastStyle.Kind kind = ToastStyle.Kind.Warn;

        internal void Init(TMP_Text l, Image i, Sprite w, Sprite inf)
        {
            label = l; icon = i; warn = w; info = inf; rt = (RectTransform)transform;
            group = GetComponent<CanvasGroup>(); if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            lastText = null; Layout(); Restart();
        }
        /// Restart the pop-in / hold / fade timer (same message shown again -> one toast, timer restarted; never stacks).
        public void Restart() { t = 0f; Apply(); }
        public void SetKind(ToastStyle.Kind k) { autoKind = false; kind = k; Layout(); }

        void OnEnable() { hasBase = false; if (rt != null) { lastText = label != null ? label.text : null; Layout(); Restart(); } }
        void OnDisable() { if (rt != null && hasBase) { rt.anchoredPosition = basePos; rt.localScale = Vector3.one; } hasBase = false; }

        void Layout()
        {
            if (label == null) return;
            if (autoKind) kind = ToastStyle.KindFor(label.text);
            var sp = kind == ToastStyle.Kind.Warn ? warn : kind == ToastStyle.Kind.Info ? info : null;
            bool hasIcon = sp != null && icon != null;
            if (icon != null) { icon.sprite = sp; icon.enabled = hasIcon; }
            var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(ToastStyle.PadX + (hasIcon ? ToastStyle.IconSize + ToastStyle.IconGap : 0f), ToastStyle.ShadowY);
            lr.offsetMax = new Vector2(-(ToastStyle.PadX + ToastStyle.ShadowX), 0f);
            if (rt != null && !string.IsNullOrEmpty(label.text))
            {
                float w = Mathf.Clamp(label.GetPreferredValues(label.text).x + ToastStyle.ExtraWidth(hasIcon), ToastStyle.MinWidth, ToastStyle.MaxWidth);
                if (Mathf.Abs(rt.sizeDelta.x - w) > .5f) rt.sizeDelta = new Vector2(w, ToastStyle.RectHeight);
                lastWidth = rt.sizeDelta.x;
            }
        }

        void LateUpdate()
        {
            if (label == null || rt == null) return;
            // new / replaced message, or GameUI re-showed the same one (it re-sizes the pill to text + 72 on every show) -> pop again
            if (label.text != lastText || (lastWidth >= 0f && Mathf.Abs(rt.sizeDelta.x - lastWidth) > .5f)) { lastText = label.text; Layout(); t = 0f; }
            t += Time.unscaledDeltaTime;
            Apply();
        }

        void Apply()
        {
            if (rt == null) return;
            var cur = rt.anchoredPosition;
            if (!hasBase || (cur - written).sqrMagnitude > .01f) { basePos = cur; hasBase = true; }   // GameUI (re)placed the pill
            float s = 1f, a = 1f, y = 0f;
            if (t < popIn)
            {
                float u = Mathf.Clamp01(t / Mathf.Max(.001f, popIn));
                s = u < .6f ? Mathf.Lerp(scaleFrom, scaleOver, 1f - (1f - u / .6f) * (1f - u / .6f)) : Mathf.Lerp(scaleOver, 1f, Mathf.SmoothStep(0, 1, (u - .6f) / .4f));
                a = 1f - (1f - u) * (1f - u);
            }
            else if (t > popIn + hold)
            {
                float u = Mathf.Clamp01((t - popIn - hold) / Mathf.Max(.001f, fadeOut));
                a = 1f - u; y = rise * (1f - (1f - u) * (1f - u));
            }
            // scale about the pill centre (GameUI anchors it top / bottom / corner): keep the centre fixed
            var size = rt.rect.size; var pv = rt.pivot;
            var fix = new Vector2((.5f - pv.x) * size.x, (.5f - pv.y) * size.y) * (1f - s);
            rt.localScale = new Vector3(s, s, 1f);
            if (group != null) group.alpha = a;
            written = basePos + fix + new Vector2(0f, y); rt.anchoredPosition = written;
        }
    }
}
