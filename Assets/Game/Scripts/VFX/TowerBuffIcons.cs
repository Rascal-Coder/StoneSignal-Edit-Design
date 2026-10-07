using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Buff icons floating above a tower (up to 4, e.g. 2x2 tower on 4 rune cells) + optional link glow line to the rune cell.
    /// SpriteRenderers sharing the HUD atlas material -> batch together. Billboards to camera.
    public class TowerBuffIcons : MonoBehaviour
    {
        public SpriteRenderer[] slots = new SpriteRenderer[4];   // built by StylizedFxV14 (PF_UI_TowerBuffIcons)
        public Sprite[] runeIcons = new Sprite[6];
        public LineRenderer[] links = new LineRenderer[4];
        public float height = 1.7f, spacing = .36f;
        Camera cam;

        /// runes: up to 4 runes; cellsWorld: rune cell centres (wall tops) for link glow, same order (null = no links).
        public void Set(IList<RuneId> runes, IList<Vector3> cellsWorld = null)
        {
            int n = runes == null ? 0 : Mathf.Min(4, runes.Count);
            for (int i = 0; i < slots.Length; i++)
            {
                bool on = i < n; if (slots[i]) { slots[i].gameObject.SetActive(on); if (on) { slots[i].sprite = runeIcons[(int)runes[i]]; slots[i].transform.localPosition = new Vector3((i - (n - 1) * .5f) * spacing, height, 0); } }
                if (links[i])
                {
                    bool l = on && cellsWorld != null && i < cellsWorld.Count; links[i].gameObject.SetActive(l);
                    if (l) { var c = RuneArt.Color(runes[i]); links[i].startColor = new Color(c.r, c.g, c.b, .9f); links[i].endColor = new Color(c.r, c.g, c.b, 0); links[i].SetPosition(0, cellsWorld[i] + Vector3.up * .05f); links[i].SetPosition(1, transform.position + Vector3.up * .6f); }
                }
            }
        }
        public void Clear() => Set(null);
        void LateUpdate()
        {
            if (!cam) cam = Camera.main; if (!cam) return;
            foreach (var s in slots) if (s && s.gameObject.activeSelf) { s.transform.rotation = cam.transform.rotation; s.transform.localScale = Vector3.one * (.28f + .02f * Mathf.Sin(Time.time * 3)); }
        }
    }
}
