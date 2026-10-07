using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal.UI
{
    /// GDD 8.x tiers: 普通 Common / 精良 Rare / 稀有 Epic / 传说 Legendary (sprite suffix common/rare/epic/legendary).
    public enum RewardRarity { Common, Rare, Epic, Legendary }
    [Serializable] public struct RewardOption { public string title; [TextArea] public string desc; public Sprite icon; }

    /// v17 3-choose-1 reward screen (visual only), chunky toon cards matching ui_card_tower_frame / BATTLE (see Docs/reward_pick_v17.md). Show() builds/reuses 3 pooled cards; the game listens to OnPicked(index),
    /// then calls PlayPick(index, targetScreenPos, onDone) to fly the card to its destination (tower / hand / rune bag).
    /// All sprites in HUD.spriteatlas -> one UI batch (+TMP); FX = 16 pooled ember Images (no ParticleSystem, no extra material).
    public class RewardPickUI : MonoBehaviour
    {
        [Tooltip("v17 ui9_reward_frame_{common,rare,epic,legendary} (border 36): navy edge + cream outline + flat fill + gloss")] public Sprite[] frames = new Sprite[4];
        [Tooltip("v17 ui9_reward_band_{rarity} (border 12): dark text band with light top line")] public Sprite[] bands = new Sprite[4];
        public Sprite tierPill, ember, burst;   // ui9_reward_tier_pill (border 29), ui_reward_ember, ui_reward_burst
        public TMP_FontAsset font;
        public Vector2 cardSize = new Vector2(400, 560); public float gap = 56;
        public event Action<int> OnPicked;
                static readonly string[] RarityName = { "普通", "精良", "稀有", "传说" };

        class Card { public RectTransform rt; public CanvasGroup cg; public Image frame, band, icon, shadow, pill; public Image[] sparkles; public TextMeshProUGUI tier, title, desc; public RewardRarity rarity; }
        readonly List<Card> cards = new List<Card>(); readonly Stack<Image> embers = new Stack<Image>(); Image burstImg; RectTransform root; bool busy;

        public void Show(RewardOption[] options, RewardRarity[] rarity)
        {
            root ??= (RectTransform)transform; gameObject.SetActive(true); busy = false;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            var size = aspect < 1.5f ? new Vector2(380, 540) : cardSize; float g = aspect < 1.5f ? 32 : gap;   // 4:3 tighter
            int n = Mathf.Min(3, options.Length);
            while (cards.Count < n) cards.Add(MakeCard(cards.Count));
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i]; c.rt.gameObject.SetActive(i < n); if (i >= n) continue;
                var r = rarity != null && i < rarity.Length ? rarity[i] : RewardRarity.Common; c.rarity = r; int ri = (int)r;
                c.rt.sizeDelta = size; c.rt.anchoredPosition = new Vector2((i - (n - 1) * .5f) * (size.x + g), -40); c.rt.localScale = Vector3.one; c.rt.localRotation = Quaternion.identity; c.cg.alpha = 1;
                c.frame.sprite = frames[ri]; c.band.sprite = bands[ri]; c.tier.text = RarityName[ri]; c.tier.color = r == RewardRarity.Legendary ? new Color32(255, 236, 170, 255) : Color.white;
                c.icon.sprite = c.shadow.sprite = options[i].icon; c.icon.enabled = c.shadow.enabled = options[i].icon; c.title.text = options[i].title; c.desc.text = options[i].desc;
                foreach (var sp in c.sparkles) sp.gameObject.SetActive(r == RewardRarity.Legendary);
                c.cg.interactable = c.cg.blocksRaycasts = true;
            }
        }

        public void PlayPick(int index, Vector2 targetScreen, Action onDone) { if (!busy && index >= 0 && index < cards.Count) StartCoroutine(Pick(index, targetScreen, onDone)); }

        IEnumerator Pick(int index, Vector2 targetScreen, Action onDone)
        {
            busy = true; foreach (var c in cards) c.cg.interactable = c.cg.blocksRaycasts = false;
            var chosen = cards[index]; bool legend = chosen.rarity == RewardRarity.Legendary;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, targetScreen, GetComponentInParent<Canvas>().worldCamera, out var target);
            Vector2 start = chosen.rt.anchoredPosition; chosen.rt.SetAsLastSibling();
            // others crumble into embers (8 + 8 of the 16 pooled)
            for (int i = 0; i < cards.Count; i++) if (i != index && cards[i].rt.gameObject.activeSelf) StartCoroutine(Crumble(cards[i]));
            // burst
            burstImg ??= NewImage("Burst", burst, root); burstImg.gameObject.SetActive(true); burstImg.rectTransform.anchoredPosition = start; burstImg.transform.SetSiblingIndex(chosen.rt.GetSiblingIndex());
            for (float t = 0; t < .25f; t += Time.unscaledDeltaTime)
            {
                float k = t / .25f; chosen.rt.localScale = Vector3.one * (1 + .12f * Mathf.Sin(k * Mathf.PI * .5f));
                float bs = (legend ? 3.2f : 2.4f) * k; burstImg.rectTransform.sizeDelta = new Vector2(260, 260) * bs; burstImg.color = new Color(1, .9f, .6f, 1 - k * .6f);
                burstImg.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 90);
                yield return null;
            }
            for (float t = 0; t < .45f; t += Time.unscaledDeltaTime)
            {
                float k = t / .45f, e = k * k * (3 - 2 * k);
                chosen.rt.anchoredPosition = Vector2.Lerp(start, target, e) + Vector2.up * Mathf.Sin(e * Mathf.PI) * 160;
                chosen.rt.localScale = Vector3.one * Mathf.Lerp(1.12f, .18f, e); chosen.rt.localRotation = Quaternion.Euler(0, 0, -12 * e);
                burstImg.color = new Color(1, .9f, .6f, .4f * (1 - k));
                yield return null;
            }
            burstImg.gameObject.SetActive(false); chosen.cg.alpha = 0;
            onDone?.Invoke(); OnPicked?.Invoke(index);
            yield return new WaitForSecondsRealtime(.15f); gameObject.SetActive(false); busy = false;
        }

        IEnumerator Crumble(Card c)
        {
            var list = new List<Image>();
            for (int k = 0; k < 8; k++) { var e = embers.Count > 0 ? embers.Pop() : NewImage("Ember", ember, root); e.gameObject.SetActive(true); list.Add(e); }
            var vel = new Vector2[8]; Vector2 p0 = c.rt.anchoredPosition;
            for (int k = 0; k < 8; k++) { list[k].rectTransform.anchoredPosition = p0 + new Vector2(UnityEngine.Random.Range(-.45f, .45f) * c.rt.sizeDelta.x, UnityEngine.Random.Range(-.5f, .3f) * c.rt.sizeDelta.y); vel[k] = new Vector2(UnityEngine.Random.Range(-60, 60), UnityEngine.Random.Range(80, 220)); list[k].rectTransform.sizeDelta = Vector2.one * UnityEngine.Random.Range(10, 18); }
            for (float t = 0; t < .55f; t += Time.unscaledDeltaTime)
            {
                float k = t / .55f; c.cg.alpha = 1 - k; c.rt.localScale = Vector3.one * (1 - .08f * k); c.rt.anchoredPosition = p0 + Vector2.down * 40 * k * k;
                for (int j = 0; j < 8; j++) { list[j].rectTransform.anchoredPosition += vel[j] * Time.unscaledDeltaTime; list[j].color = Color.Lerp(new Color(1, .8f, .3f), new Color(1, .35f, .1f, 0), k); }
                yield return null;
            }
            c.rt.gameObject.SetActive(false); foreach (var e in list) { e.gameObject.SetActive(false); embers.Push(e); }
        }

        void Update()
        {   // legendary: 3 flat sparkles twinkle (scale only) - no glow/sheen ornaments in the v17 style
            foreach (var c in cards)
            {
                if (!c.rt.gameObject.activeSelf || c.rarity != RewardRarity.Legendary) continue;
                for (int k = 0; k < c.sparkles.Length; k++) c.sparkles[k].rectTransform.localScale = Vector3.one * (.75f + .35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.2f + k * 1.7f)));
            }
        }

        Card MakeCard(int i)
        {   // layout = reward_pick_v17.png (ref 400x560): icon 180 @ y70, title 44 @ y300, band 180 at bottom, tier pill 150x60 over the top edge
            var c = new Card(); var go = new GameObject("RewardCard" + i, typeof(RectTransform), typeof(CanvasGroup)); c.rt = (RectTransform)go.transform; c.rt.SetParent(root, false); c.cg = go.GetComponent<CanvasGroup>();
            c.frame = NewImage("Frame", frames[0], c.rt, Image.Type.Sliced); Stretch(c.frame.rectTransform, 0);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = c.frame; int idx = i; btn.onClick.AddListener(() => { if (!busy) OnPicked?.Invoke(idx); });
            c.band = NewImage("Band", bands[0], c.rt, Image.Type.Sliced); var br = c.band.rectTransform;
            br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(1, 0); br.pivot = new Vector2(.5f, 0); br.offsetMin = new Vector2(12, 12); br.offsetMax = new Vector2(-12, 192);
            c.shadow = NewImage("IconShadow", null, c.rt); Top(c.shadow.rectTransform, 78, new Vector2(180, 180)); c.shadow.rectTransform.anchoredPosition += new Vector2(6, 0); c.shadow.color = new Color(0, 0, 0, .25f); c.shadow.preserveAspect = true;
            c.icon = NewImage("Icon", null, c.rt); Top(c.icon.rectTransform, 70, new Vector2(180, 180)); c.icon.preserveAspect = true;
            c.title = Text("Title", c.rt, 44); Top(c.title.rectTransform, 270, new Vector2(-40, 60), true); Outline(c.title, 5);
            c.desc = Text("Desc", c.band.rectTransform, 28); Stretch(c.desc.rectTransform, 14); Outline(c.desc, 3);
            c.pill = NewImage("TierPill", tierPill, c.rt, Image.Type.Sliced); Top(c.pill.rectTransform, -28, new Vector2(150, 60));
            c.tier = Text("Tier", c.pill.rectTransform, 28); Stretch(c.tier.rectTransform, 0);
            c.sparkles = new Image[3]; var sp = new[] { new Vector3(40, -44, 32), new Vector3(-46, -60, 24), new Vector3(-70, -230, 18) };
            for (int k = 0; k < 3; k++)
            {
                var im = NewImage("Sparkle" + k, ember, c.rt); c.sparkles[k] = im; /* integration fix: array was never filled -> NRE in Show */ var r = im.rectTransform; bool right = sp[k].x < 0;
                r.anchorMin = r.anchorMax = new Vector2(right ? 1 : 0, 1); r.anchoredPosition = new Vector2(sp[k].x, sp[k].y); r.sizeDelta = Vector2.one * sp[k].z;
            }
            return c;
        }
        void Outline(TextMeshProUGUI t, float px) { t.color = Color.white; t.outlineColor = new Color32(30, 26, 58, 255); t.outlineWidth = Mathf.Clamp01(px / t.fontSize * 2.2f); t.fontStyle = FontStyles.Bold; }
        Image NewImage(string n, Sprite s, Transform p, Image.Type type = Image.Type.Simple) { var go = new GameObject(n, typeof(RectTransform), typeof(Image)); go.transform.SetParent(p, false); var im = go.GetComponent<Image>(); im.sprite = s; im.type = type; im.raycastTarget = n == "Frame"; return im; }
        TextMeshProUGUI Text(string n, Transform p, float size) { var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false); var t = go.AddComponent<TextMeshProUGUI>(); if (font) t.font = font; t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.enableWordWrapping = true; return t; }
        static void Stretch(RectTransform r, float inset) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.one * inset; r.offsetMax = -Vector2.one * inset; }
        static void Top(RectTransform r, float y, Vector2 size, bool stretchX = false)
        { r.anchorMin = new Vector2(stretchX ? 0 : .5f, 1); r.anchorMax = new Vector2(stretchX ? 1 : .5f, 1); r.pivot = new Vector2(.5f, 1); r.anchoredPosition = new Vector2(0, -y); r.sizeDelta = size; }
    }
}
