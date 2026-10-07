using UnityEngine;

namespace StoneSignal.VFX
{
    /// v17.4: one place that says which particle materials need a texture and which particle systems can draw at all.
    /// Used by the editor check (StylizedVfxTexturesV173.Checks) and meant for the runtime VFX audit (GameplayShot -vfxcheck).
    ///  * StoneSignal/FXAdditive + StoneSignal/FXAlpha are PROCEDURAL: SS_FXCore.hlsl builds the shape from uv (_Shape 0..8:
    ///    dot/ring/streak/smoke/flash/scorch/coin/trail/plate, _Softness, _RingWidth) and has no texture property -> never
    ///    "no texture". Colour = vertex colour x _TintColor x _Intensity.
    ///  * URP Particles/* and StoneSignal/SS_GroundPrint sample _BaseMap -> a null _BaseMap is a real bug (white squares).
    ///  * A renderer only draws when it is enabled AND its system spawns particles (emission rate/bursts, a sub-emitter, or
    ///    script Emit()). Container roots (StylizedVFXBuilder.Root: emission off, renderer off, no material) draw nothing.
    public static class VfxMaterialRules
    {
        public static readonly string[] ProceduralShaders = { "StoneSignal/FXAdditive", "StoneSignal/FXAlpha" };

        public static bool IsProcedural(Material m)
        {
            if (m == null || m.shader == null) return false;
            foreach (var s in ProceduralShaders) if (m.shader.name == s) return true;
            return false;
        }

        public static bool SamplesBaseMap(Material m) =>
            m != null && m.shader != null && (m.shader.name.StartsWith("Universal Render Pipeline/Particles") || m.shader.name == "StoneSignal/SS_GroundPrint");

        /// True when the material needs a main texture but has none (the only "no texture" case that is a bug).
        public static bool MissingTexture(Material m) => SamplesBaseMap(m) && m.GetTexture("_BaseMap") == null;

        static float Max(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: return c.constant;
                case ParticleSystemCurveMode.TwoConstants: return Mathf.Max(c.constantMin, c.constantMax);
                case ParticleSystemCurveMode.Curve: return c.curveMultiplier * MaxKey(c.curve);
                default: return c.curveMultiplier * Mathf.Max(MaxKey(c.curveMin), MaxKey(c.curveMax));
            }
        }
        static float MaxKey(AnimationCurve a) { float m = 0; if (a != null) foreach (var k in a.keys) m = Mathf.Max(m, k.value); return m; }

        /// The emission module spawns something (rate over time / distance or a burst with count > 0).
        public static bool EmitsByModules(ParticleSystem ps)
        {
            if (ps == null) return false;
            var e = ps.emission; if (!e.enabled) return false;
            if (Max(e.rateOverTime) > 0 || Max(e.rateOverDistance) > 0) return true;
            for (int i = 0; i < e.burstCount; i++) if (Max(e.GetBurst(i).count) > 0) return true;
            return false;
        }

        /// ps is a sub-emitter of any enabled sub-emitters module under root.
        public static bool IsSubEmitter(ParticleSystem ps, GameObject root)
        {
            if (ps == null || root == null) return false;
            foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var se = p.subEmitters; if (!se.enabled) continue;
                for (int i = 0; i < se.subEmittersCount; i++) if (se.GetSubEmitterSystem(i) == ps) return true;
            }
            return false;
        }
    }
}
