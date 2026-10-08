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
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var v = new UIVertex(); var fx = new Vector4(gray, 1f - brightness, 0f, 0f);
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); v.uv1 = fx; vh.SetUIVertex(v, i); }
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
