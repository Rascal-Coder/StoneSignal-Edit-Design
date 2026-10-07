using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Rune ids (玩法策划 spec). Index = glyph cell in T_RuneGlyphAtlas (4x2) and rune icon order.
    public enum RuneId { Blade = 0, Swift = 1, Sight = 2, Frost = 3, Bounty = 4, Resonance = 5 }

    public static class RuneArt
    {
        //                               锋·赤 Blade   疾·苍 Swift   望·金 Sight   霜·紫 Frost   丰·绿 Bounty  共鸣·白 Resonance
        public static readonly string[] Hex = { "D9483B", "3B8FD9", "E8B53A", "8A5CD1", "5BB85A", "F2EEE6" };
        public static readonly string[] Icon = { "ui_rune_blade", "ui_rune_swift", "ui_rune_sight", "ui_rune_frost", "ui_rune_bounty", "ui_rune_resonance" };
        public static Color Color(RuneId r) { ColorUtility.TryParseHtmlString("#" + Hex[(int)r], out var c); return c; }
        public const int ResonanceRadiusCells = 1;   // v16.1 gameplay: Chebyshev 1 = the 8 surrounding cells (matches RuneConfig.resonanceRadiusCells default)
    }

    /// In-world rune glyph on a wall block top: toon shader _RuneIdx/_RuneColor/_RuneAtlas via MaterialPropertyBlock (no extra mesh / decal).
    public static class RuneInlay
    {
        static readonly int RuneIdx = Shader.PropertyToID("_RuneIdx"), RuneColor = Shader.PropertyToID("_RuneColor"), RuneAtlas = Shader.PropertyToID("_RuneAtlas");
        static readonly HashSet<Renderer> withRune = new HashSet<Renderer>();
        static MaterialPropertyBlock mpb;
        /// Assign once at boot (Resources or serialized ref): Assets/Game/Art/Stylized/Runes/T_RuneGlyphAtlas.png
        public static Texture2D Atlas;
        public static bool HasRune(Renderer r) => r && withRune.Contains(r);

        public static void Set(Renderer wall, RuneId rune, float intensity = 1.6f)
        {
            if (!wall) return; mpb ??= new MaterialPropertyBlock();
            wall.GetPropertyBlock(mpb); mpb.SetFloat(RuneIdx, (int)rune); mpb.SetColor(RuneColor, RuneArt.Color(rune) * intensity);
            if (Atlas) Shader.SetGlobalTexture(RuneAtlas, Atlas);   // v16.1: no texture in the MPB (textures can't be instanced); materials already carry _RuneAtlas
            wall.SetPropertyBlock(mpb); withRune.Add(wall);
        }
        public static void Clear(Renderer wall)
        {
            if (!wall) return; mpb ??= new MaterialPropertyBlock();
            wall.GetPropertyBlock(mpb); mpb.SetFloat(RuneIdx, -1); withRune.Remove(wall);
            if (mpb.GetFloat(Shader.PropertyToID("_HiAmount")) > 0.001f) wall.SetPropertyBlock(mpb); else wall.SetPropertyBlock(null);   // v16.1: back to SRP batching
        }
    }

}
