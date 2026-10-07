using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Placement preview (visual only, no gameplay). Block: SetCells(cells) builds one translucent stone + soft outline per cell.
    /// Tower: SetModel(prefab) clones the tower renderers with the ghost material. SetValid(true/false) = green / red tint.
    /// PlayDrop(pos) = squash-in + dust puff where the real piece lands.
    /// v17.5: puffs 0.5-0.8 m growing 1.6x, #D9B48A a 1, life ~0.55 s, at the block base just outside the outer edges, slight outward
    /// push (dustSpeed) + upward drift (dustRise); still max DustMax (12), pooled (StylizedPlacementFX FX_DropDust).
    /// v17.4: PlayDrop is called once per placed cell in the same frame; the cells are collected and ONE soft dust ring is emitted
    /// on the drop-in landing (v18.2, art-requested) around the outer edges of the whole shape (max DustMax particles, world space, pooled system, no Instantiate,
    /// no material instances).
    public class PlacementGhost : MonoBehaviour
    {
        public GameObject cellTemplate;            // child "Cell" (stone + outline), disabled
        public Material ghostMat, outlineMat;      // shared; tints switched via MaterialPropertyBlock-free material swap
        public Material validMat, invalidMat, validOutline, invalidOutline;
        public ParticleSystem dust;
        public float cellSize = 1f;
        Transform cells, footprint;
        bool valid = true;
        Vector2Int footSize = Vector2Int.one; int footRot;
        bool[] cellValid = new bool[0];
        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        void Awake() { cells = new GameObject("Cells").transform; cells.SetParent(transform, false); if (cellTemplate) cellTemplate.SetActive(false); }

        public void SetCells(Vector2Int[] shape)
        {
            if (!cells) Awake();
            for (int i = cells.childCount - 1; i >= 0; i--) Kill(cells.GetChild(i).gameObject);
            foreach (var c in shape)
            {
                var g = Instantiate(cellTemplate, cells); g.SetActive(true);
                g.transform.localPosition = new Vector3(c.x * cellSize, 0, c.y * cellSize);
            }
            SetValid(valid);
        }

        public void SetModel(GameObject prefab)
        {
            if (!cells) Awake();
            for (int i = cells.childCount - 1; i >= 0; i--) Kill(cells.GetChild(i).gameObject);
            var g = Instantiate(prefab, cells); g.transform.localPosition = Vector3.zero;
            foreach (var b in g.GetComponentsInChildren<Behaviour>()) if (!(b is Animator)) b.enabled = false;
            foreach (var col in g.GetComponentsInChildren<Collider>()) Kill(col);
            foreach (var ps in g.GetComponentsInChildren<ParticleSystem>()) ps.gameObject.SetActive(false);
            SetValid(valid);
        }

        /// Tower footprint highlight: size in cells (1x1, 1x2, 2x2), rotation in 90 deg steps (matches the tower's yaw).
        /// Cells are centred on this transform (= tower pivot). Cell index = y * size.x + x in unrotated footprint space.
        public void SetFootprint(Vector2Int size, int rotation90)
        {
            if (!footprint) { footprint = new GameObject("Footprint").transform; footprint.SetParent(transform, false); }
            for (int i = footprint.childCount - 1; i >= 0; i--) Kill(footprint.GetChild(i).gameObject);
            footSize = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y)); footRot = ((rotation90 % 4) + 4) % 4;
            footprint.localRotation = Quaternion.Euler(0, footRot * 90, 0);
            cellValid = new bool[footSize.x * footSize.y];
            for (int y = 0; y < footSize.y; y++) for (int x = 0; x < footSize.x; x++)
            {
                var g = Instantiate(cellTemplate, footprint); g.SetActive(true); g.name = "Cell_" + (y * footSize.x + x);
                g.transform.localPosition = new Vector3((x - (footSize.x - 1) * .5f) * cellSize, 0, (y - (footSize.y - 1) * .5f) * cellSize);
                cellValid[y * footSize.x + x] = valid;
            }
            for (int i = 0; i < cellValid.Length; i++) SetCellValid(i, valid);
        }
        public Vector2Int FootprintSize => footSize;
        public int FootprintRotation => footRot;

        /// Per-cell green (valid) / red (invalid). Index as in SetFootprint.
        public void SetCellValid(int idx, bool ok)
        {
            if (!footprint || idx < 0 || idx >= footprint.childCount) return;
            cellValid[idx] = ok;
            foreach (var r in footprint.GetChild(idx).GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = r.name.Contains("Outline") ? (ok ? validOutline : invalidOutline) : (ok ? validMat : invalidMat);
        }

        public void SetValid(bool ok)
        {
            valid = ok;
            if (footprint) for (int i = 0; i < footprint.childCount; i++) SetCellValid(i, ok);
            if (!cells) return;
            foreach (var r in cells.GetComponentsInChildren<Renderer>(true))
            {
                bool outline = r.name.Contains("Outline");
                var m = outline ? (ok ? validOutline : invalidOutline) : (ok ? validMat : invalidMat);
                var arr = r.sharedMaterials; for (int i = 0; i < arr.Length; i++) arr[i] = m; r.sharedMaterials = arr;
            }
        }

        public void PlayDrop(Transform placed)
        {
            if (!placed) return;
            if (dust)
            {
                dustCells.Add(placed.position);                   // before Drop() lifts it (only x/z are used); emitted by the first cell's landing callback
            }
            StartCoroutine(Drop(placed));
        }

        // ---- v17.4 landing dust ring
        public const int DustMax = 12;
        [Tooltip("v17.5: outward puff speed (m/s, drag slows it) - slight push, v17.4 was 0.7-1.15")] public Vector2 dustSpeed = new Vector2(.45f, .8f);
        [Tooltip("v17.5: upward drift speed (m/s)")] public Vector2 dustRise = new Vector2(.25f, .45f);
        [Tooltip("v17.5: puff centre height above the ghost's base (m) - at the block base (v17.4 0.12)")] public float dustLift = .06f;
        // art-requested (v18.2): the dust is emitted from the drop-in animation's landing callback (Drop -> Landed, the frame the block
        // reaches its rest height), not after a hard-coded delay, so it stays aligned if the animation changes. Landing = DropTime * DropFall.
        public const float DropTime = .28f, DropFall = .4f, DropHeight = .35f;
        /// Seconds from PlayDrop to touchdown (currently 0.28 * 0.4 = 0.112 s).
        public static float LandTime => DropTime * DropFall;
        /// Diagnostics: Time.time of the last landing callback that emitted dust.
        public float LastLandTime { get; private set; } = -1f;
        static readonly HashSet<Vector2Int> dustKeys = new HashSet<Vector2Int>();
        readonly List<Vector3> dustCells = new List<Vector3>(8);
        readonly List<Vector3> dustEdges = new List<Vector3>(24);   // xyz = edge midpoint, paired with dustNormals
        readonly List<Vector3> dustNormals = new List<Vector3>(24);
        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        void Landed() { if (dustCells.Count == 0) return; LastLandTime = Time.time; EmitDust(); }   // one ring per placement (cells of the same frame)
        void OnDisable() { dustCells.Clear(); }

        void EmitDust()
        {
            float cs = Mathf.Max(.01f, cellSize), y = transform.position.y + dustLift;
            var keys = dustKeys; keys.Clear(); var origin = dustCells[0];
            foreach (var c in dustCells) keys.Add(new Vector2Int(Mathf.RoundToInt((c.x - origin.x) / cs), Mathf.RoundToInt((c.z - origin.z) / cs)));
            dustEdges.Clear(); dustNormals.Clear();
            foreach (var k in keys)
                foreach (var d in Dirs)
                {
                    if (keys.Contains(k + d)) continue;   // inner edge: no dust between two cells of the same piece
                    var n = new Vector3(d.x, 0, d.y);
                    dustEdges.Add(new Vector3(origin.x + k.x * cs, y, origin.z + k.y * cs) + n * cs * .5f); dustNormals.Add(n);
                }
            dustCells.Clear();
            int edges = dustEdges.Count; if (edges == 0 || !dust) return;
            if (!dust.isPlaying) dust.Play();   // loop on, emission module off: Play() spawns nothing by itself
            int count = DustMax - dust.particleCount; if (count <= 0) return;   // art-requested fix: fixed 12 per landing (was max(edges, 8), capped 12)
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                int e = edges <= count ? i % edges : Mathf.FloorToInt(i * edges / (float)count);   // spread evenly round the outline
                var n = dustNormals[e]; var along = new Vector3(-n.z, 0, n.x);
                float slide = edges < count && i >= edges ? (i / edges % 2 == 1 ? .3f : -.3f) : Random.Range(-.18f, .18f);
                ep.position = dustEdges[e] + along * slide * cs + n * Random.Range(.02f, .1f) * cs;   // v17.5: just outside the outer edge
                ep.velocity = (n * Random.Range(dustSpeed.x, dustSpeed.y) + along * Random.Range(-.12f, .12f)) + Vector3.up * Random.Range(dustRise.x, dustRise.y);
                dust.Emit(ep, 1);
            }
        }

        IEnumerator Drop(Transform t)
        {
            var s0 = t.localScale; var p0 = t.localPosition; bool landed = false;
            for (float k = 0; k < DropTime; k += Time.deltaTime)
            {
                if (!t) { Landed(); yield break; }
                float u = k / DropTime, fall = Mathf.Min(1, u / DropFall);
                float sq = Mathf.Sin(u * Mathf.PI) * .18f * (1 - u);       // squash then settle
                t.localScale = new Vector3(s0.x * (1 + sq), s0.y * (1 - sq * 1.4f), s0.z * (1 + sq));
                t.localPosition = p0 + Vector3.up * (1 - fall) * DropHeight;  // short drop-in
                if (!landed && fall >= 1) { landed = true; Landed(); }    // touchdown frame -> landing dust
                yield return null;
            }
            if (!landed) Landed();
            if (t) { t.localScale = s0; t.localPosition = p0; }
        }
    }
}
