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
            DamageKind.Fire => new Color(1f, .55f, .2f), DamageKind.Ice => new Color(.55f, .9f, 1f),
            DamageKind.Lightning => new Color(.8f, .68f, 1f), DamageKind.Explosive => new Color(1f, .7f, .35f),
            DamageKind.Heal => new Color(.5f, 1f, .55f), _ => Color.white
        };

        /// target: the enemy (or null). Same target+kind (non-crit) within StackWindow -> the existing number grows instead of a new one.
        public static DamageNumber Spawn(Vector3 worldPos, float amount, DamageKind kind = DamageKind.Physical, bool crit = false, Object target = null)
        {
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
