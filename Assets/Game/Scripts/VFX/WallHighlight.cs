using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Placement feedback on the real wall blocks under a tower footprint: green/red pulsing rim glow on block top edges + 0.02 m lift.
    /// Uses the toon shader's _HiAmount/_HiColor via MaterialPropertyBlock on just the highlighted renderers (no extra meshes);
    /// Clear() removes the MPB (unless the block carries a rune) so those renderers return to SRP batching.
    /// Cell -> renderer: Register(cell, renderer) from your grid, or raycast fallback (needs colliders) using gridOrigin/cellSize.
    public class WallHighlight : MonoBehaviour
    {
        public static WallHighlight Instance { get; private set; }
        public Vector3 gridOrigin = new Vector3(-8, 0, -6);
        public float cellSize = 1f;
        [ColorUsage(false, true)] public Color validColor = new Color(.35f, 1.6f, .55f), invalidColor = new Color(1.8f, .3f, .3f);
        static readonly Dictionary<Vector2Int, Renderer> map = new Dictionary<Vector2Int, Renderer>();
        static readonly List<Renderer> lit = new List<Renderer>();
        static MaterialPropertyBlock mpb;
        static readonly int HiAmount = Shader.PropertyToID("_HiAmount"), HiColor = Shader.PropertyToID("_HiColor");

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) { Clear(); Instance = null; } }

        public static void Register(Vector2Int cell, Renderer r) { if (r) map[cell] = r; }
        public static void Unregister(Vector2Int cell) => map.Remove(cell);
        public static void ClearRegistry() { Clear(); map.Clear(); }

        /// Highlight the wall blocks at these grid cells. Call every time the ghost moves/rotates or validity changes.
        public static void SetCells(Vector2Int[] cells, bool valid)
        {
            Clear();
            if (cells == null) return;
            mpb ??= new MaterialPropertyBlock();
            var col = Instance ? (valid ? Instance.validColor : Instance.invalidColor) : (valid ? new Color(.35f, 1.6f, .55f) : new Color(1.8f, .3f, .3f));
            foreach (var c in cells)
            {
                var r = Resolve(c); if (!r) continue;
                r.GetPropertyBlock(mpb); mpb.SetFloat(HiAmount, 1); mpb.SetColor(HiColor, col); r.SetPropertyBlock(mpb); lit.Add(r);
            }
        }
        /// Per-cell validity variant (same order as cells).
        public static void SetCells(Vector2Int[] cells, bool[] valid, bool additive = false)
        {
            if (!additive) Clear(); if (cells == null) return; mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < cells.Length; i++)
            {
                var r = Resolve(cells[i]); if (!r) continue; bool ok = valid == null || i >= valid.Length || valid[i];
                var col = Instance ? (ok ? Instance.validColor : Instance.invalidColor) : (ok ? new Color(.35f, 1.6f, .55f) : new Color(1.8f, .3f, .3f));
                r.GetPropertyBlock(mpb); mpb.SetFloat(HiAmount, 1); mpb.SetColor(HiColor, col); r.SetPropertyBlock(mpb); lit.Add(r);
            }
        }
        public static void Clear()
        {
            mpb ??= new MaterialPropertyBlock();
            foreach (var r in lit) if (r) { r.GetPropertyBlock(mpb); mpb.SetFloat(HiAmount, 0); if (RuneInlay.HasRune(r)) r.SetPropertyBlock(mpb); else r.SetPropertyBlock(null); }
            lit.Clear();
        }
        static Renderer Resolve(Vector2Int c)
        {
            if (map.TryGetValue(c, out var r) && r) return r;
            var o = Instance ? Instance.gridOrigin : new Vector3(-8, 0, -6); float s = Instance ? Instance.cellSize : 1;
            var top = new Vector3(o.x + (c.x + .5f) * s, 20, o.z + (c.y + .5f) * s);
            return Physics.Raycast(top, Vector3.down, out var hit, 40) ? hit.collider.GetComponentInParent<Renderer>() ?? hit.collider.GetComponentInChildren<Renderer>() : null;
        }
    }
}
