using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal.UI
{
    /// Per-rarity soft glow behind each RewardPickUI card (user-approved spec; art's RewardPickUI untouched).
    /// One Image per card, a sibling placed directly below its card under the same nested canvas, default UI material (no additive),
    /// sprite from HUD.spriteatlas (same texture as the card frames) so it batches with the frame. Animated only through vertex
    /// colour + RectTransform scale. Follows the card's position/scale/rotation and CanvasGroup alpha (fade in, crumble out).
    public sealed class RewardGlow : MonoBehaviour
    {
        public Sprite glow;
        public float sizeFactor = 1.25f, fadeIn = .3f;
        struct Tier { public Color c; public float a, period, amp, scaleAmp; }
        static readonly Tier[] Tiers =
        {
            new Tier { c = Hex(0xB8BCC8), a = .25f, period = 0,    amp = 0,    scaleAmp = 0 },
            new Tier { c = Hex(0x4FA3FF), a = .45f, period = 2.0f, amp = .12f, scaleAmp = 0 },
            new Tier { c = Hex(0xA96BFF), a = .60f, period = 1.6f, amp = .15f, scaleAmp = .04f },
            new Tier { c = Hex(0xFFB43A), a = .80f, period = 1.2f, amp = .20f, scaleAmp = .06f },
        };
        static readonly Color FlareColor = Hex(0xFF6A2A);
        static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, 1);
        class G { public RectTransform card; public CanvasGroup cg; public Image img, flare; public int tier; }
        readonly System.Collections.Generic.List<G> gs = new System.Collections.Generic.List<G>();
        float t0; int picked = -1; float pickT;
        [Tooltip("Optional slow-rotating legendary flare. Off by default: rotated bounds overlap the neighbour cards (+3 DC measured).")] public bool flareLayer = false;

        public void Begin(RewardRarity[] rarity)
        {
            t0 = Time.unscaledTime; picked = -1;
            for (int i = 0; ; i++)
            {
                var card = transform.Find("RewardCard" + i) as RectTransform; if (card == null) break;
                if (gs.Count <= i) gs.Add(new G { card = card, cg = card.GetComponent<CanvasGroup>(), img = NewImg("Glow" + i) });
                var g = gs[i]; g.tier = rarity != null && i < rarity.Length ? (int)rarity[i] : 0;
                bool legend = g.tier == (int)RewardRarity.Legendary && flareLayer;
                if (legend && g.flare == null) g.flare = NewImg("GlowFlare" + i);
                if (g.flare != null) g.flare.gameObject.SetActive(legend);
            }
            Order(); LateUpdate();
        }
        void OnDisable() { foreach (var g in gs) { if (g.img) g.img.enabled = false; if (g.flare) g.flare.enabled = false; } }
        public void Pick(int index) { picked = index; pickT = Time.unscaledTime; }
        Image NewImg(string n)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image)); go.transform.SetParent(transform, false);
            var im = go.GetComponent<Image>(); im.sprite = glow; im.raycastTarget = false; im.color = new Color(1, 1, 1, 0); return im;
        }
        // glow (and legendary flare) directly below its card: Glow0 Card0 Glow1 Card1 ... (interleaved, no cross-card overlap)
        void Order()
        {
            foreach (var g in gs)
            {
                int ci = g.card.GetSiblingIndex();
                if (g.flare != null) { g.flare.transform.SetSiblingIndex(ci); ci = g.card.GetSiblingIndex(); }
                g.img.transform.SetSiblingIndex(ci);
            }
        }
        // Base size so the glow quad at peak pulse (1 + scaleAmp) stays inside the gap to the neighbour card: a glow quad
        // overlapping the neighbour card breaks the canvas batch (measured +3 DC for Legendary 1.25 x 1.06 at 16:9).
        // The pulse itself (1..1+scaleAmp) is untouched; only the resting size of high tiers shrinks a few percent.
        float SizeFor(int i, float scaleAmp)
        {
            var c = gs[i].card; float w = Mathf.Max(1, c.sizeDelta.x), gap = float.MaxValue;
            for (int j = 0; j < gs.Count; j++)
                if (j != i && gs[j].card != null && gs[j].card.gameObject.activeInHierarchy)
                    gap = Mathf.Min(gap, Mathf.Abs(gs[j].card.anchoredPosition.x - c.anchoredPosition.x) - w);
            if (gap == float.MaxValue || gap <= 0) return sizeFactor;
            return Mathf.Min(sizeFactor, (w + 2 * gap * .95f) / (w * (1 + scaleAmp))); // edge stops 5% of the gap short of the neighbour card
        }
        void LateUpdate()
        {
            float now = Time.unscaledTime, fin = fadeIn > 0 ? Mathf.Clamp01((now - t0) / fadeIn) : 1;
            for (int i = 0; i < gs.Count; i++)
            {
                var g = gs[i]; bool on = g.card != null && g.card.gameObject.activeInHierarchy;
                g.img.enabled = on && glow != null; if (g.flare != null) g.flare.enabled = on && glow != null && g.flare.gameObject.activeSelf;
                if (!on) continue;
                var tr = Tiers[Mathf.Clamp(g.tier, 0, 3)];
                float w = tr.period > 0 ? Mathf.Sin(now * 6.2832f / tr.period) : 0, a = (tr.a + tr.amp * w) * fin, sc = 1 + tr.scaleAmp * (.5f + .5f * w);
                float cardA = g.cg != null ? g.cg.alpha : 1;
                if (i == picked)
                {   // flash to 1 then gone (0.12 s up, 0.25 s out), independent of the card's own flight
                    float k = now - pickT; a = k < .12f ? Mathf.Lerp(a, 1, k / .12f) : Mathf.Lerp(1, 0, (k - .12f) / .25f); cardA = 1;
                }
                var rt = g.img.rectTransform; rt.anchorMin = g.card.anchorMin; rt.anchorMax = g.card.anchorMax; rt.pivot = g.card.pivot;
                rt.anchoredPosition = g.card.anchoredPosition; rt.sizeDelta = g.card.sizeDelta * SizeFor(i, tr.scaleAmp); rt.localRotation = g.card.localRotation;
                rt.localScale = g.card.localScale * sc;
                var c = tr.c; c.a = Mathf.Clamp01(a * cardA); g.img.color = c;
                if (g.flare != null && g.flare.enabled)
                {
                    var fr = g.flare.rectTransform; fr.anchorMin = rt.anchorMin; fr.anchorMax = rt.anchorMax; fr.pivot = rt.pivot; fr.anchoredPosition = rt.anchoredPosition;
                    fr.sizeDelta = g.card.sizeDelta * (sizeFactor * 1.1f); fr.localScale = g.card.localScale; fr.localRotation = Quaternion.Euler(0, 0, now * 12f);
                    var fc = FlareColor; fc.a = Mathf.Clamp01(.35f * fin * cardA * (i == picked ? a : 1)); g.flare.color = fc;
                }
            }
        }
    }
}
