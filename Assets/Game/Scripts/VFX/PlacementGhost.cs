using System.Collections;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Placement preview (visual only, no gameplay). Block: SetCells(cells) builds one translucent stone + soft outline per cell.
    /// Tower: SetModel(prefab) clones the tower renderers with the ghost material. SetValid(true/false) = green / red tint.
    /// PlayDrop(pos) = squash-in + dust puff where the real piece lands.
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

        public void PlayDrop(Transform placed) { if (placed) StartCoroutine(Drop(placed)); }

        IEnumerator Drop(Transform t)
        {
            var s0 = t.localScale; var p0 = t.localPosition;
            if (dust) { dust.transform.position = t.position; dust.Play(); }
            for (float k = 0; k < .28f; k += Time.deltaTime)
            {
                float u = k / .28f;
                float sq = Mathf.Sin(u * Mathf.PI) * .18f * (1 - u);       // squash then settle
                t.localScale = new Vector3(s0.x * (1 + sq), s0.y * (1 - sq * 1.4f), s0.z * (1 + sq));
                t.localPosition = p0 + Vector3.up * (1 - Mathf.Min(1, u * 2.5f)) * .35f;  // short drop-in
                yield return null;
            }
            t.localScale = s0; t.localPosition = p0;
        }
    }
}
