using TMPro;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// v18.6 (美术策划 4) screen-space floating number: spawns `spawnAbove` px above the target's HP-bar top edge, drifts diagonally to
    /// (side x driftX, driftY) px over `life` s (EaseOutCubic), fades over the last `fadeTail` of its life, pops popFrom -> 1 over popSeconds.
    /// Lives on the DamageNumbers overlay canvas (sortingOrder EnemyHpBarsUI.NumbersOrder = one below the HP bars). Unscaled time.
    public sealed class UiDamageNumber : MonoBehaviour
    {
        internal TextMeshProUGUI text; internal RectTransform rt; internal float amount, age; internal int key; internal bool crit;
        internal Enemy target; internal Vector2 anchorPx; internal float side = 1f, px = 24f; Color color;
        internal float StackLimit => DamageNumbers.Style.numberStackSeconds;
        internal void Begin(Vector2 barTopPx, Enemy follow, float value, Color c, bool isCrit, float sizePx, float sideSign)
        {
            anchorPx = barTopPx; target = follow; amount = value; color = c; crit = isCrit; px = sizePx; side = sideSign; age = 0f;
            text.fontSize = px; Refresh(); gameObject.SetActive(true); Place();
        }
        internal void Stack(float value) { amount += value; age = Mathf.Min(age, DamageNumbers.Style.numberPopFromSeconds * .5f); Refresh(); }
        void Refresh() { text.text = crit ? Mathf.RoundToInt(amount) + "!" : Mathf.RoundToInt(amount).ToString(); text.color = color; }
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
            Vector2 p = anchorPx + new Vector2(side * st.numberDriftXPx * e, st.numberSpawnAbovePx + st.numberDriftYPx * e) * s;
            float pop = st.numberPopFromSeconds > 0f && age < st.numberPopFromSeconds ? Mathf.Lerp(st.numberPopFrom, 1f, age / st.numberPopFromSeconds) : 1f;
            rt.localScale = Vector3.one * pop;
            // clamp inside the safe area (text pivot = bottom centre)
            var safe = HudScaler.SafeArea; float hw = text.preferredWidth * .5f * pop, hh = text.preferredHeight * pop;
            p.x = Mathf.Clamp(p.x, safe.xMin + hw, Mathf.Max(safe.xMin + hw, safe.xMax - hw)); p.y = Mathf.Clamp(p.y, safe.yMin, Mathf.Max(safe.yMin, safe.yMax - hh));
            var parent = (RectTransform)rt.parent; float inv = parent.lossyScale.x > 0f ? 1f / parent.lossyScale.x : 1f;
            rt.anchoredPosition = p * inv;
            float fadeStart = 1f - st.numberFadeTail; text.alpha = t <= fadeStart || st.numberFadeTail <= 0f ? 1f : 1f - (t - fadeStart) / st.numberFadeTail;
        }
    }
}
