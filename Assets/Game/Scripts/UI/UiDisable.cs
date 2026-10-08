using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal
{
    /// v18.6 one disabled style (美术策划): grayscale `Gray` (0..1) and brightness `Brightness` written to TEXCOORD1 for the
    /// StoneSignal/UI/Disabled shader (Resources/StoneSignalUIDisabled). Neutral - never a hue overlay. Needs the canvas TexCoord1 channel.
    [DisallowMultipleComponent]
    public sealed class UiDisable : BaseMeshEffect
    {
        [SerializeField] float gray, brightness = 1f;
        public float Gray => gray; public float Brightness => brightness;
        public void Set(float g, float b)
        {
            g = Mathf.Clamp01(g); b = Mathf.Clamp01(b);
            if (Mathf.Approximately(g, gray) && Mathf.Approximately(b, brightness)) return;
            gray = g; brightness = b; if (graphic != null) graphic.SetVerticesDirty();
        }
        // ---- v18.6b silhouette outline (tower cards, v17 white outline): copies of the graphic's own quads, offset `outlinePx` (local
        // units = 1080p ref px on a scaled canvas) in OutlineDirs directions and drawn underneath in a flat colour (shader flag uv1.x + 2:
        // vertex colour x sprite alpha). The union is the face silhouette dilated by outlinePx: outside the card, same corner shape
        // (offset curve of the face's rounded corners). Same material / atlas -> +0 draw calls. The grey / brightness of the disabled
        // style applies to the copies too, so a disabled card's outline goes grey with it.
        [SerializeField] float outlinePx; [SerializeField] Color outlineColor = Color.white;
        public const int OutlineDirs = 16;
        public float OutlinePx => outlinePx; public Color OutlineColor => outlineColor;
        public void SetOutline(float px, Color c)
        {
            px = Mathf.Max(0f, px); if (Mathf.Approximately(px, outlinePx) && c == outlineColor) return;
            outlinePx = px; outlineColor = c; if (graphic != null) graphic.SetVerticesDirty();
        }
        static readonly System.Collections.Generic.List<UIVertex> stream = new System.Collections.Generic.List<UIVertex>(), outStream = new System.Collections.Generic.List<UIVertex>();
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var fx = new Vector4(gray, 1f - brightness, 0f, 0f);
            stream.Clear(); vh.GetUIVertexStream(stream);
            outStream.Clear();
            if (outlinePx > 0f && stream.Count > 0)
            {
                var flat = new Vector4(gray + 2f, 1f - brightness, 0f, 0f); Color32 oc = outlineColor;
                for (int d = 0; d < OutlineDirs; d++)
                {
                    float a = d * Mathf.PI * 2f / OutlineDirs; var off = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * outlinePx;
                    for (int i = 0; i < stream.Count; i++) { var v = stream[i]; v.position += off; v.uv1 = flat; v.color = new Color32(oc.r, oc.g, oc.b, (byte)(oc.a * v.color.a / 255)); outStream.Add(v); }
                }
            }
            for (int i = 0; i < stream.Count; i++) { var v = stream[i]; v.uv1 = fx; outStream.Add(v); }
            vh.Clear(); vh.AddUIVertexTriangleStream(outStream);
        }
        static Material mat; static bool looked;
        /// Shared material (one for every Image on the hand canvas -> they keep batching). Null if the shader is missing (fallback: no grey).
        public static Material SharedMaterial
        {
            get
            {
                if (!looked) { looked = true; var sh = Resources.Load<Shader>("StoneSignalUIDisabled"); if (sh == null) sh = Shader.Find("StoneSignal/UI/Disabled"); if (sh != null && sh.isSupported) mat = new Material(sh) { name = "StoneSignal UI Disabled (shared)" }; }
                return mat;
            }
        }
    }

    /// v18.6: a solid white sprite cut from an existing HUD-atlas sprite (ui_reward_burst has a 68 x 68 px pure white square), so tinted
    /// bars (remaining plate progress, screen-space HP bars) batch with the atlas. No new texture. Null -> Image default white.
    public static class UiWhite
    {
        static Sprite white; static bool made;
        public static Sprite Get(ArtCatalog art)
        {
            if (made) return white; made = true;
            var src = art != null ? art.UiSprite("ui_reward_burst") : null; if (src == null || src.texture == null) return null;
            try
            {
                if (src.packed && src.packingMode == SpritePackingMode.Tight) return null;
                Rect tr = src.textureRect; Vector2 off = src.textureRectOffset;
                // white square in sprite pixels (bottom-up): x 94..161, y 95..162; inset 8 px against bilinear bleed -> 51 x 51 at (102, 103)
                var r = new Rect(tr.x + 102f - off.x, tr.y + 103f - off.y, 51f, 51f);
                if (r.xMin < tr.xMin || r.yMin < tr.yMin || r.xMax > tr.xMax || r.yMax > tr.yMax) return null;
                white = Sprite.Create(src.texture, r, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
                white.name = "ui_white (ui_reward_burst centre)";
            }
            catch (System.Exception e) { Debug.LogWarning("UiWhite: " + e.Message); white = null; }
            return white;
        }
    }
}
