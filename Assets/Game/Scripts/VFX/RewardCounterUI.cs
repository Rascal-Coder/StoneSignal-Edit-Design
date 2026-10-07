using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal.VFX
{
    /// Gold (or any reward) counter: pill + icon + number roll + punch + glow ring + "+N" income pop. Visual only:
    /// set the true value with SetValue(v, instant) from game code; Add(n) is called by RewardFlyFx on each icon arrival.
    public class RewardCounterUI : MonoBehaviour
    {
        public TMPro.TMP_Text label;          // number
        public RectTransform punchTarget;     // usually the pill root
        public RectTransform icon;            // coin icon (gets a bigger punch + tilt)
        public Image glowRing;                // ui_counter_glow_ring, additive-looking (alpha animated)
        public TMPro.TMP_Text incomePop;      // "+20" floating text (pooled single label, restarts on each add)
        public float rollSpeed = 6f;

        int target; float shown; float punch, ring, pop; int popSum;
        public int Value => target;

        public void SetValue(int v, bool instant = false) { target = v; if (instant) { shown = v; Refresh(); } }
        public void Add(int n)
        {
            target += n; punch = 1; ring = 1;
            popSum = pop > 0 ? popSum + n : n; pop = 1;
            if (incomePop) incomePop.text = "+" + popSum;
        }
        public void ResetFx() { punch = ring = pop = 0; popSum = 0; shown = target; Refresh(); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            shown = Mathf.Abs(target - shown) < .5f ? target : Mathf.Lerp(shown, target, 1 - Mathf.Exp(-rollSpeed * dt));
            punch = Mathf.MoveTowards(punch, 0, dt * 5); ring = Mathf.MoveTowards(ring, 0, dt * 2.5f); pop = Mathf.MoveTowards(pop, 0, dt * 1.2f);
            Refresh();
        }
        void Refresh()
        {
            if (label) label.text = Mathf.RoundToInt(shown).ToString();
            float s = 1 + .14f * Mathf.Sin(punch * Mathf.PI) * punch;
            if (punchTarget) punchTarget.localScale = new Vector3(s, s, 1);
            if (icon) { float si = 1 + .3f * Mathf.Sin(punch * Mathf.PI); icon.localScale = new Vector3(si, si, 1); icon.localRotation = Quaternion.Euler(0, 0, -12 * Mathf.Sin(punch * Mathf.PI * 2) * punch); }
            if (glowRing) { var c = glowRing.color; c.a = ring * ring; glowRing.color = c; glowRing.rectTransform.localScale = Vector3.one * (1 + (1 - ring) * .5f); }
            if (incomePop) { incomePop.alpha = Mathf.Clamp01(pop * 2); incomePop.rectTransform.anchoredPosition = new Vector2(0, -38 - (1 - pop) * -30); }
        }
    }
}
