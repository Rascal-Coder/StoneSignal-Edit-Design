using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    public enum DamageKind { Physical, Fire, Ice, Lightning, Explosive, Heal }

    /// Pooled TextMeshPro damage numbers.
    /// Depth decision: Emberward-style arcade readability - numbers always draw on top (TMP Distance Field Overlay,
    /// sorting order 100). They spawn above the hit enemy and drift up, so they never read as "behind" a wall; at the
    /// ortho game camera, walls (0.6 m) are low and occluding numbers would only hide damage feedback. Call DamageNumbers.Spawn(...) from gameplay; nothing to place in the scene.
    /// Prefab: Resources/StylizedVFX/PF_FX_DamageNumber = Assets/Game/VFX/Stylized/Resources/StylizedVFX/PF_FX_DamageNumber.prefab.
    public static class DamageNumbers
    {
        public static float StackWindow = 0.35f;   // repeated hits on the same target+kind within this window add up
        public static float CritScale = 1.6f, BaseScale = 1f;
        public static Color CritColor = new Color(1f, 0.82f, 0.18f);
        static readonly Stack<DamageNumber> Pool = new Stack<DamageNumber>();
        static readonly Dictionary<int, DamageNumber> Live = new Dictionary<int, DamageNumber>();
        static GameObject _prefab; static Transform _root;

        public static Color ColorFor(DamageKind k) => k switch
        {
            DamageKind.Fire => new Color(1f, .42f, .32f) /* v19 coral red #FF6B52 (no tower deals Fire yet) */, DamageKind.Ice => new Color(.55f, .9f, 1f),
            DamageKind.Lightning => new Color(.93f, 1f, .55f) /* v19 lemon-white #EDFF8C (雷铃兽 / Pulse) */, DamageKind.Explosive => new Color(1f, .7f, .35f),
            DamageKind.Heal => new Color(.5f, 1f, .55f), _ => Color.white
        };

        // ---- v18.6 (美术策划 4): screen-space numbers one layer below the screen-space HP bars ----
        public static CombatHudStyle Style = new CombatHudStyle();
        static bool _ui; static Camera _cam; static RectTransform _uiRoot; static TMPro.TMP_FontAsset _font; static Material _fontMat;
        static readonly Stack<UiDamageNumber> UiPool = new Stack<UiDamageNumber>();
        static readonly Dictionary<int, UiDamageNumber> UiLive = new Dictionary<int, UiDamageNumber>();
        static readonly Dictionary<int, float> Side = new Dictionary<int, float>();
        static readonly Dictionary<int, List<UiDamageNumber>> Queues = new Dictionary<int, List<UiDamageNumber>>();
        static readonly List<int> pruneScratch = new List<int>();
        static void PruneQueues()
        {
            pruneScratch.Clear();
            foreach (var kv in Queues) { kv.Value.RemoveAll(x => x == null || !x.gameObject.activeSelf || x.queueId != kv.Key || x.Fading); if (kv.Value.Count == 0) pruneScratch.Add(kv.Key); }
            foreach (var k in pruneScratch) Queues.Remove(k);
        }
        /// Diagnostics: live queued numbers on one enemy (oldest first).
        public static List<UiDamageNumber> DebugQueue(Object target) { var l = new List<UiDamageNumber>(); if (target && Queues.TryGetValue(target.GetInstanceID(), out var q)) foreach (var x in q) if (x && x.gameObject.activeSelf) l.Add(x); return l; }
        /// HudScaler px scale (1080p reference px -> screen px).
        public static float PxScale => HudScaler.ScaleFor(Screen.width, Screen.height);
        public static int UiLiveCount { get { int n = 0; if (_uiRoot) foreach (Transform c in _uiRoot) if (c.gameObject.activeSelf) n++; return n; } }
        /// Switch to screen-space numbers (sortingOrder EnemyHpBarsUI.NumbersOrder). Font + shared material come from PF_FX_DamageNumber (one draw call).
        public static void ConfigureScreenSpace(CombatHudStyle style, Camera cam)
        {
            Style = style ?? new CombatHudStyle(); _cam = cam; _ui = true;
            if (!_prefab) _prefab = Resources.Load<GameObject>("StylizedVFX/PF_FX_DamageNumber");
            var src = _prefab ? _prefab.GetComponent<TMPro.TextMeshPro>() : null;
            if (src) { _font = src.font; _fontMat = src.fontSharedMaterial; }
            if (!_uiRoot)
            {
                var go = new GameObject("[DamageNumbers UI]", typeof(RectTransform), typeof(Canvas)); Object.DontDestroyOnLoad(go);
                var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = EnemyHpBarsUI.NumbersOrder;
                c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                _uiRoot = (RectTransform)go.transform;
            }
        }
        static UiDamageNumber GetUi()
        {
            while (UiPool.Count > 0) { var p = UiPool.Pop(); if (p) return p; }
            if (!_uiRoot) return null;
            var go = new GameObject("dmg", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)); go.transform.SetParent(_uiRoot, false); go.SetActive(false);
            var t = go.GetComponent<TMPro.TextMeshProUGUI>(); if (_font) t.font = _font; if (_fontMat) t.fontSharedMaterial = _fontMat;
            t.alignment = TMPro.TextAlignmentOptions.Bottom; t.enableWordWrapping = false; t.overflowMode = TMPro.TextOverflowModes.Overflow; t.raycastTarget = false; t.fontStyle = TMPro.FontStyles.Bold;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(.5f, 0f); rt.sizeDelta = new Vector2(160f, 40f);
            var n = go.AddComponent<UiDamageNumber>(); n.text = t; n.rt = rt;
            {   // v18.6b crit '!': its own glyph numberCritGapPx right of the number (same font material -> same batch, +0 DC)
                var bg = new GameObject("crit", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)); bg.transform.SetParent(go.transform, false);
                var bt = bg.GetComponent<TMPro.TextMeshProUGUI>(); if (_font) bt.font = _font; if (_fontMat) bt.fontSharedMaterial = _fontMat;
                bt.alignment = TMPro.TextAlignmentOptions.BottomLeft; bt.enableWordWrapping = false; bt.overflowMode = TMPro.TextOverflowModes.Overflow; bt.raycastTarget = false; bt.fontStyle = TMPro.FontStyles.Bold; bt.text = "!";
                var brt = (RectTransform)bg.transform; brt.anchorMin = brt.anchorMax = new Vector2(.5f, 0f); brt.pivot = new Vector2(0f, 0f); brt.sizeDelta = new Vector2(40f, 40f);
                n.bang = bt; bg.SetActive(false);
            }
            return n;
        }
        internal static void ReleaseUi(UiDamageNumber n)
        {
            if (n.key != 0 && UiLive.TryGetValue(n.key, out var l) && l == n) UiLive.Remove(n.key);
            n.gameObject.SetActive(false); n.target = null; UiPool.Push(n);
        }
        static UiDamageNumber SpawnUi(Vector3 worldPos, float amount, DamageKind kind, bool crit, Object target)
        {
            var enemy = target as Enemy; int id = target ? target.GetInstanceID() : 0; int key = id != 0 ? id * 8 + (int)kind : 0;
            if (key != 0 && !crit && Style.numberStackSeconds > 0f && UiLive.TryGetValue(key, out var live) && live && live.gameObject.activeSelf && live.age < live.StackLimit) { live.Stack(amount); return live; }
            var n = GetUi(); if (!n) return null;
            Vector2 top; var bars = EnemyHpBarsUI.Instance;
            if (enemy != null && bars != null && (bars.TryGetBar(enemy, out var r) || bars.Compute(enemy, out r))) top = new Vector2(r.center.x, r.yMax);
            else { var cam = _cam ? _cam : Camera.main; Vector3 sp = cam ? cam.WorldToScreenPoint(worldPos) : Vector3.zero; top = sp; }
            float side = 1f; if (id != 0) { side = Side.TryGetValue(id, out var last) ? -last : 1f; Side[id] = side; if (Side.Count > 256) Side.Clear(); }
            n.key = key; if (key != 0 && !crit) UiLive[key] = n;
            float px = (crit ? Style.numberCritPx : Style.numberNormalPx) * PxScale;
            // v18.6b queue (美术策划): a number within numberQueueSeconds of the previous one on the same enemy spawns numberQueueStepPx above
            // the previous number's CURRENT position; at most numberQueueMax per enemy - one more pushes the oldest straight into its fade-out.
            float lift = 0f;
            if (id != 0)
            {
                if (!Queues.TryGetValue(id, out var q)) { if (Queues.Count > 128) PruneQueues(); Queues[id] = q = new List<UiDamageNumber>(4); }
                q.RemoveAll(x => x == null || !x.gameObject.activeSelf || x.queueId != id || x.Fading);
                var prev = q.Count > 0 ? q[q.Count - 1] : null;
                if (prev != null && prev.age < Style.numberQueueSeconds) lift = prev.CurrentLiftPx + Style.numberQueueStepPx;
                while (q.Count >= Mathf.Max(1, Style.numberQueueMax)) { q[0].ForceFade(); q.RemoveAt(0); }
                q.Add(n);
            }
            n.queueId = id;
            n.Begin(top, enemy, amount, crit ? CritColor : ColorFor(kind), crit, px, side, lift);
            return n;
        }

        /// target: the enemy (or null). Same target+kind (non-crit) within StackWindow -> the existing number grows instead of a new one.
        public static Component Spawn(Vector3 worldPos, float amount, DamageKind kind = DamageKind.Physical, bool crit = false, Object target = null)
        {
            if (_ui) return SpawnUi(worldPos, amount, kind, crit, target);
            int key = target ? target.GetInstanceID() * 8 + (int)kind : 0;
            if (key != 0 && !crit && Live.TryGetValue(key, out var live) && live && live.gameObject.activeSelf && live.age < StackWindow + live.popTime)
            { live.Stack(amount); return live; }
            var n = Get(); if (!n) return null;
            n.key = key; if (key != 0 && !crit) Live[key] = n;
            n.Begin(worldPos + Vector3.up * 0.2f, amount, crit ? CritColor : ColorFor(kind), crit, crit ? CritScale : BaseScale);
            n.text.fontStyle = TMPro.FontStyles.Bold;
            return n;
        }

        static DamageNumber Get()
        {
            while (Pool.Count > 0) { var p = Pool.Pop(); if (p) return p; }
            if (!_prefab) _prefab = Resources.Load<GameObject>("StylizedVFX/PF_FX_DamageNumber");
            if (!_prefab) { Debug.LogWarning("DamageNumbers: PF_FX_DamageNumber missing in Resources/StylizedVFX"); return null; }
            if (!_root) { _root = new GameObject("[DamageNumbers]").transform; }
            var go = Object.Instantiate(_prefab, _root); go.SetActive(false);
            return go.GetComponent<DamageNumber>();
        }

        internal static void Release(DamageNumber n)
        {
            if (n.key != 0 && Live.TryGetValue(n.key, out var l) && l == n) Live.Remove(n.key);
            n.gameObject.SetActive(false); Pool.Push(n);
        }
    }
}
