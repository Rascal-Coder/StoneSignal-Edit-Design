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
        Transform cells;
        bool valid = true;

        void Awake() { cells = new GameObject("Cells").transform; cells.SetParent(transform, false); if (cellTemplate) cellTemplate.SetActive(false); }

        public void SetCells(Vector2Int[] shape)
        {
            if (!cells) Awake();
            for (int i = cells.childCount - 1; i >= 0; i--) Destroy(cells.GetChild(i).gameObject);
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
            for (int i = cells.childCount - 1; i >= 0; i--) Destroy(cells.GetChild(i).gameObject);
            var g = Instantiate(prefab, cells); g.transform.localPosition = Vector3.zero;
            foreach (var b in g.GetComponentsInChildren<Behaviour>()) if (!(b is Animator)) b.enabled = false;
            foreach (var col in g.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var ps in g.GetComponentsInChildren<ParticleSystem>()) ps.gameObject.SetActive(false);
            SetValid(valid);
        }

        public void SetValid(bool ok)
        {
            valid = ok;
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
