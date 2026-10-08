using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal
{
    /// v18.6 (美术策划 4): enemy HP bars on their own screen-space overlay canvas (sortingOrder BarsOrder) - above every floating damage
    /// number (DamageNumbers canvas, one order below) and below the HUD. Each frame the bar follows Enemy.HpBarAnchor (the invisible 3D bar:
    /// position above the model, spawn scale-in, fill); size = the old 3D bar's world size projected (hpBarWorldWidth x hpBarWorldHeight).
    /// One white atlas sprite (UiWhite) tinted with the palette's bar colours: all bars batch into one draw call.
    public sealed class EnemyHpBarsUI : MonoBehaviour
    {
        public const int BarsOrder = -20, NumbersOrder = -21;
        public static EnemyHpBarsUI Instance { get; private set; }
        EnemyManager enemies; Camera cam; CombatHudStyle style; Sprite white; Color back = new Color(.85f, .25f, .3f), fill = new Color(.35f, .85f, .4f);
        RectTransform root;
        sealed class Bar { public RectTransform back, fill; public Image backImg, fillImg; }
        readonly List<Bar> pool = new List<Bar>(); int used;
        readonly Dictionary<Enemy, Rect> lastRects = new Dictionary<Enemy, Rect>();
        public void Initialize(EnemyManager registry, VisualPalette palette, Camera camera, CombatHudStyle st)
        {
            enemies = registry; cam = camera; style = st ?? new CombatHudStyle(); Instance = this;
            white = UiWhite.Get(palette != null ? palette.art : null);
            if (palette != null) { back = MatColor(palette.invalid, back); fill = MatColor(palette.valid, fill); }
            var go = new GameObject("HP bars", typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = BarsOrder; c.pixelPerfect = false;
            root = (RectTransform)go.transform;
            Enemy.ScreenSpaceBars = true;
        }
        static Color MatColor(Material m, Color fallback)
        {
            if (m == null) return fallback; var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : fallback; c.a = 1f; return c;
        }
        Bar Get(int i)
        {
            while (pool.Count <= i)
            {
                var b = new Bar();
                b.back = Img("HP back", root, back, out b.backImg); b.fill = Img("HP fill", b.back, fill, out b.fillImg);
                b.fill.anchorMin = new Vector2(0f, 0f); b.fill.anchorMax = new Vector2(0f, 1f); b.fill.pivot = new Vector2(0f, .5f); b.fill.anchoredPosition = Vector2.zero;
                pool.Add(b);
            }
            return pool[i];
        }
        RectTransform Img(string n, Transform parent, Color c, out Image img)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            img = go.GetComponent<Image>(); img.sprite = white; img.color = c; img.raycastTarget = false;
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(.5f, .5f); return r;
        }
        /// Screen rect (px, y up) of an enemy's bar this frame, if shown.
        public bool TryGetBar(Enemy e, out Rect r) => lastRects.TryGetValue(e, out r);
        /// World -> screen bar rect (also for enemies not drawn yet this frame).
        public bool Compute(Enemy e, out Rect r)
        {
            r = default; if (e == null || !e.Alive || cam == null) return false;
            var a = e.HpBarAnchor; if (a == null) return false;
            float k = e.HpBarScale01; if (k <= .001f) return false;
            Vector3 c = a.position, right = cam.transform.right * (style.hpBarWorldWidth * .5f);
            Vector3 s0 = cam.WorldToScreenPoint(c - right), s1 = cam.WorldToScreenPoint(c + right), sc = cam.WorldToScreenPoint(c);
            if (sc.z <= 0f) return false;
            float full = Vector2.Distance(s0, s1), w = full * k, h = full * (style.hpBarWorldHeight / Mathf.Max(.0001f, style.hpBarWorldWidth)) * k;
            r = new Rect(sc.x - w * .5f, sc.y - h * .5f, w, h); return true;
        }
        void LateUpdate()
        {
            if (enemies == null || root == null) return;
            used = 0; lastRects.Clear();
            float pxScale = root.lossyScale.x > 0f ? 1f / root.lossyScale.x : 1f; // overlay canvas without scaler: 1 unit = 1 px
            foreach (var e in enemies.Active)
            {
                if (!Compute(e, out var r)) continue;
                var b = Get(used++); if (!b.back.gameObject.activeSelf) b.back.gameObject.SetActive(true);
                b.back.anchoredPosition = r.center * pxScale; b.back.sizeDelta = r.size * pxScale;
                float f = e.HpFraction; b.fill.sizeDelta = new Vector2(r.width * pxScale * f, 0f); b.fillImg.enabled = f > 0f;
                lastRects[e] = r;
            }
            for (int i = used; i < pool.Count; i++) if (pool[i].back.gameObject.activeSelf) pool[i].back.gameObject.SetActive(false);
        }
        public int VisibleBars => used;
        void OnDestroy() { if (Instance == this) Instance = null; Enemy.ScreenSpaceBars = false; }
    }
}
