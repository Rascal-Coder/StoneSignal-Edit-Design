using TMPro;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// v18.6 (美术策划 4) screen-space floating number: spawns `spawnAbove` px above the target's HP-bar top edge, drifts diagonally to
    /// (side x driftX, driftY) px over `life` s (EaseOutCubic), fades over the last `fadeTail` of its life, pops popFrom -> 1 over popSeconds.
    /// Lives on the DamageNumbers overlay canvas (sortingOrder EnemyHpBarsUI.NumbersOrder = one below the HP bars). Unscaled time.
    public sealed class UiDamageNumber : MonoBehaviour
    {
        internal TextMeshProUGUI text, bang; internal RectTransform rt; internal float amount, age; internal int key, queueId; internal bool crit;
        internal Enemy target; internal Vector2 anchorPx; internal float side = 1f, px = 24f, liftPx; Color color;
        internal float StackLimit => DamageNumbers.Style.numberStackSeconds;
        /// v18.6b: forced into its fade-out by a newer number (queue max).
        internal bool Fading { get; private set; }
        /// Vertical offset (ref px) of the number above its spawn anchor right now: queue lift + drift so far (the next queued number spawns
        /// numberQueueStepPx above this).
        internal float CurrentLiftPx { get { var st = DamageNumbers.Style; return liftPx + st.numberDriftYPx * EaseOutCubic(Mathf.Clamp01(age / st.numberSeconds)); } }
        /// Diagnostics: screen px rect of the '!' glyph (zero when not a crit).
        internal TextMeshProUGUI Bang => bang;
        internal void Begin(Vector2 barTopPx, Enemy follow, float value, Color c, bool isCrit, float sizePx, float sideSign, float lift = 0f)
        {
            anchorPx = barTopPx; target = follow; amount = value; color = c; crit = isCrit; px = sizePx; side = sideSign; age = 0f; liftPx = lift; Fading = false;
            text.fontSize = px; if (bang != null) { bang.fontSize = px; bang.gameObject.SetActive(crit); }
            Refresh(); gameObject.SetActive(true); Place();
        }
        internal void ForceFade()
        {
            var st = DamageNumbers.Style; Fading = true;
            age = Mathf.Max(age, st.numberSeconds * (1f - Mathf.Clamp01(st.numberFadeTail)));
        }
        internal void Stack(float value) { amount += value; age = Mathf.Min(age, DamageNumbers.Style.numberPopFromSeconds * .5f); Refresh(); }
        void Refresh() { text.text = Mathf.RoundToInt(amount).ToString(); text.color = color; if (bang != null) bang.color = color; }
        static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        void LateUpdate()
        {
            age += Time.unscaledDeltaTime; var st = DamageNumbers.Style;
            if (age >= st.numberSeconds) { DamageNumbers.ReleaseUi(this); return; }
            Place();
        }
        void Place()
        {
            var st = DamageNumbers.Style; float s = DamageNumbers.PxScale;
            var bars = EnemyHpBarsUI.Instance;
            if (target != null && target.Alive && bars != null && bars.TryGetBar(target, out var r)) anchorPx = new Vector2(r.center.x, r.yMax);
            float t = Mathf.Clamp01(age / st.numberSeconds), e = EaseOutCubic(t);
            Vector2 p = anchorPx + new Vector2(side * st.numberDriftXPx * e, st.numberSpawnAbovePx + liftPx + st.numberDriftYPx * e) * s;
            float pop = st.numberPopFromSeconds > 0f && age < st.numberPopFromSeconds ? Mathf.Lerp(st.numberPopFrom, 1f, age / st.numberPopFromSeconds) : 1f;
            rt.localScale = Vector3.one * pop;
            // clamp inside the safe area (text pivot = bottom centre; a crit '!' extends the right side by gap + its width)
            var safe = HudScaler.SafeArea; float numW = text.preferredWidth, hh = text.preferredHeight * pop, left = numW * .5f * pop, right = left;
            if (crit && bang != null)
            {
                // Ink gap, not preferredWidth: TMP side bearings made the '!' sit ~2 px from the digits.
                // Place so glyph-right of the number to glyph-left of '!' is numberCritGapPx ref px (x PxScale), in screen px.
                text.ForceMeshUpdate(); bang.ForceMeshUpdate();
                float inkR = text.textBounds.max.x, bangL = bang.textBounds.min.x;
                if (inkR < 1f) inkR = numW * .5f;
                float ppm = Mathf.Max(1e-4f, text.rectTransform.lossyScale.x);
                float localGap = st.numberCritGapPx * s / ppm;
                float ax = inkR - bangL + localGap;
                bang.rectTransform.anchoredPosition = new Vector2(ax, 0f);
                right = (ax + Mathf.Max(bang.preferredWidth, bang.textBounds.size.x)) * pop;
            }
            p.x = Mathf.Clamp(p.x, safe.xMin + left, Mathf.Max(safe.xMin + left, safe.xMax - right)); p.y = Mathf.Clamp(p.y, safe.yMin, Mathf.Max(safe.yMin, safe.yMax - hh));
            var parent = (RectTransform)rt.parent; float inv = parent.lossyScale.x > 0f ? 1f / parent.lossyScale.x : 1f;
            rt.anchoredPosition = p * inv;
            float fadeStart = 1f - st.numberFadeTail; text.alpha = t <= fadeStart || st.numberFadeTail <= 0f ? 1f : 1f - (t - fadeStart) / st.numberFadeTail;
            if (bang != null && crit) bang.alpha = text.alpha;
        }
    }
}
