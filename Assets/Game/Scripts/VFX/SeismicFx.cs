using UnityEngine;

namespace StoneSignal.VFX
{
    /// v18.6b Seismic (岩甲兽) fire FX per the art spec, built at runtime from the existing FX_Muzzle_Mortar / FX_Explosion_HE prefabs
    /// (same SS_FXCore materials, no new assets, no edited art files) and played through the existing StylizedVfx pools:
    ///  - muzzle: flash 0.14 s (scale 0.3 -> 1.0 over 0-0.04 s, then fade), core #FFF2C8 + outer glow #FF9A3D (same flash material), 4 smoke puffs;
    ///  - impact ring: horizontal (ground +0.02 m, not camera-facing), radius 0 -> splash radius over 0.35 s EaseOutCubic, width 0.14 -> 0.04 m,
    ///    colour #FFD27A -> #FF8A3D, alpha 0.9 -> 0 over the last 40 %;
    ///  - ground shockwave: second concentric ring, +0.05 s, to 1.6 m (x splash / 1.35 so it tracks upgrades), alpha 0.4;
    ///  - 8 dust puffs spreading along the ground (#C9A57A, 0.4 s), scorch decal 0.6 s (#2B1A12 alpha 0.35). Screen shake unchanged.
    /// Rings share one material instance (SS_FXAdditive ring, _RingWidth animated per frame) so they batch together; the dust shares the
    /// explosion smoke material and the glow the flash material. Templates are parked under an inactive root; StylizedVfx keys its pool by them.
    public static class SeismicFx
    {
        public const float RingSeconds = .35f, RingWidthFrom = .14f, RingWidthTo = .04f, RingAlpha = .9f, RingFadeTail = .4f, RingGroundOffset = .02f;
        public const float WaveDelay = .05f, WaveRadius = 1.6f, WaveAlpha = .4f, BaseSplash = 1.35f;
        // ---- ART DECISION PENDING (art director: v18.6b spec below vs v19b "no ember orange / no scorch"). Every colour and the scorch
        // ---- live here only; templates are built on first use, so set these before the first Seismic shot (e.g. from a config hook).
        public static Color RingFrom = new Color32(0xFF, 0xD2, 0x7A, 0xFF), RingTo = new Color32(0xFF, 0x8A, 0x3D, 0xFF);   // v18.6b: #FFD27A -> #FF8A3D
        public static Color FlashCore = new Color32(0xFF, 0xF2, 0xC8, 0xFF), FlashGlow = new Color32(0xFF, 0x9A, 0x3D, 0xFF); // v18.6b: core #FFF2C8, glow #FF9A3D
        public static Color Dust = new Color32(0xC9, 0xA5, 0x7A, 0xFF), Scorch = new Color32(0x2B, 0x1A, 0x12, 0xFF);        // v18.6b: dust #C9A57A, scorch #2B1A12
        public static bool ScorchEnabled = true; public static float ScorchSeconds = .6f, ScorchAlpha = .35f;                 // v18.6b: scorch on, 0.6 s, alpha 0.35
        public const float FlashSeconds = .14f, FlashGrowSeconds = .04f, FlashFrom = .3f, DustSeconds = .4f;
        public const int DustCount = 8, SmokePuffs = 4;

        static Transform parkRoot; static GameObject muzzleSrc, muzzleTpl, impactSrc, impactTpl;
        static Transform Park()
        {
            if (parkRoot == null) { var go = new GameObject("[SeismicFx templates]"); go.SetActive(false); Object.DontDestroyOnLoad(go); parkRoot = go.transform; }
            return parkRoot;
        }
        static ParticleSystem Find(GameObject root, string name) { foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) if (ps.name == name) return ps; return null; }
        static Gradient Grad(Color a, Color b, float alphaHoldUntil, float a0 = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a0, alphaHoldUntil), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// Muzzle template built from FX_Muzzle_Mortar (null source -> null).
        public static GameObject Muzzle(GameObject src)
        {
            if (src == null || !Application.isPlaying) return src; if (muzzleTpl != null && muzzleSrc == src) return muzzleTpl;
            muzzleSrc = src; muzzleTpl = Object.Instantiate(src, Park()); muzzleTpl.name = src.name + " (v18.6b Seismic)";
            var flash = Find(muzzleTpl, "Flash");
            if (flash != null)
            {
                Shape(flash, FlashCore, 1f);
                var glow = Object.Instantiate(flash.gameObject, flash.transform.parent); glow.name = "Glow"; glow.transform.SetSiblingIndex(flash.transform.GetSiblingIndex());
                var gp = glow.GetComponent<ParticleSystem>(); var m = gp.main; m.startSize = new ParticleSystem.MinMaxCurve(m.startSize.constant * 1.8f); Shape(gp, FlashGlow, .55f);
            }
            var smoke = Find(muzzleTpl, "Smoke");
            if (smoke != null) { var e = smoke.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)SmokePuffs) }); }
            return muzzleTpl;
        }
        static void Shape(ParticleSystem ps, Color c, float alpha)
        {
            var m = ps.main; m.startLifetime = FlashSeconds; m.startColor = new Color(c.r, c.g, c.b, alpha);
            var sz = ps.sizeOverLifetime; sz.enabled = true; float k = FlashGrowSeconds / FlashSeconds;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, FlashFrom, 0f, (1f - FlashFrom) / k), new Keyframe(k, 1f, 0f, 0f), new Keyframe(1f, 1f)));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, k);
        }

        /// Impact template built from FX_Explosion_HE: the Shockwave becomes the ground impact ring, + a second ring, ground dust, short scorch.
        public static GameObject Impact(GameObject src)
        {
            if (src == null || !Application.isPlaying) return src; if (impactTpl != null && impactSrc == src) return impactTpl;
            impactSrc = src; impactTpl = Object.Instantiate(src, Park()); impactTpl.name = src.name + " (v18.6b Seismic)";
            var fx = impactTpl.AddComponent<SeismicImpactFx>();
            var ring = Find(impactTpl, "Shockwave");
            if (ring != null)
            {
                RingSystem(ring); fx.ring = ring;
                var w = Object.Instantiate(ring.gameObject, ring.transform.parent); w.name = "GroundWave"; w.transform.SetSiblingIndex(ring.transform.GetSiblingIndex() + 1);
                fx.wave = w.GetComponent<ParticleSystem>();
            }
            var smoke = Find(impactTpl, "Smoke");
            if (smoke != null)
            {
                var d = Object.Instantiate(smoke.gameObject, smoke.transform.parent); d.name = "GroundDust"; d.transform.SetSiblingIndex(smoke.transform.GetSiblingIndex() + 1);
                var dp = d.GetComponent<ParticleSystem>(); var m = dp.main;
                m.startLifetime = DustSeconds; m.startColor = new Color(Dust.r, Dust.g, Dust.b, .85f); m.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 3.6f); m.gravityModifier = 0f;
                m.startSize = new ParticleSystem.MinMaxCurve(.22f, .34f); // x the prefab root scale (Hierarchy)
                var e = dp.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)DustCount) });
                var sh = dp.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .12f; sh.radiusThickness = 0f; sh.rotation = new Vector3(-90f, 0f, 0f); sh.arc = 360f; sh.arcMode = ParticleSystemShapeMultiModeValue.Loop; sh.arcSpread = 1f / DustCount;
                d.transform.localPosition = new Vector3(0f, .04f, 0f);
                var lv = dp.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 6f; lv.multiplyDragByParticleSize = false;
                var col = dp.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, .35f);
                var sz = dp.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .7f, 1f, 1.4f));
                fx.dust = dp;
            }
            var sc = Find(impactTpl, "Scorch");
            if (sc != null && !ScorchEnabled) sc.gameObject.SetActive(false);
            else if (sc != null)
            {
                var m = sc.main; m.startLifetime = ScorchSeconds; m.startColor = new Color(Scorch.r, Scorch.g, Scorch.b, ScorchAlpha);
                var col = sc.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, .5f);
                fx.scorch = sc;
            }
            return impactTpl;
        }
        static void RingSystem(ParticleSystem ps)
        {
            var m = ps.main; m.startLifetime = RingSeconds + WaveDelay + .05f; m.startSpeed = 0f; m.startSize = 0f; m.simulationSpace = ParticleSystemSimulationSpace.Local; m.maxParticles = 1;
            var sz = ps.sizeOverLifetime; sz.enabled = false; var col = ps.colorOverLifetime; col.enabled = false;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.alignment = ParticleSystemRenderSpace.Local; r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        public static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
    }

    /// Per-instance driver of the two ground rings (size, ring width, colour, alpha per frame). Rings use one material instance per
    /// pooled instance (SS_FXAdditive ring shape, _RingWidth animated) -> both rings batch together.
    public sealed class SeismicImpactFx : MonoBehaviour
    {
        public ParticleSystem ring, wave, dust, scorch;
        Material mat; float started = -1f, radius = SeismicFx.BaseSplash;
        readonly ParticleSystem.Particle[] one = new ParticleSystem.Particle[1];
        /// Last values (diagnostics): ring radius / width in metres, alpha; wave radius / alpha.
        public float RingRadius { get; private set; } public float RingWidth { get; private set; } public float RingA { get; private set; }
        public float WaveR { get; private set; } public float WaveA { get; private set; } public float Splash => radius;
        public static SeismicImpactFx Last { get; private set; }
        public void Begin(float splashRadius)
        {
            radius = splashRadius > 0f ? splashRadius : SeismicFx.BaseSplash; started = Time.time; Last = this;
            float s = Mathf.Max(.0001f, transform.lossyScale.x);
            foreach (var p in new[] { ring, wave }) if (p != null) p.transform.localPosition = new Vector3(0f, SeismicFx.RingGroundOffset / s, 0f);
            if (mat == null && ring != null)
            {
                var r = ring.GetComponent<ParticleSystemRenderer>(); mat = new Material(r.sharedMaterial) { name = "M_FX_Add_Ring (Seismic v18.6b instance)" };
                r.sharedMaterial = mat; if (wave != null) wave.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            }
            LateUpdate();
        }
        void LateUpdate()
        {
            if (started < 0f) return;
            float t = Time.time - started, s = Mathf.Max(.0001f, transform.lossyScale.x);
            // ring 1: radius 0 -> splash, EaseOutCubic over RingSeconds; width .14 -> .04 m; colour from -> to; alpha .9 -> 0 over the last 40 %
            float k = Mathf.Clamp01(t / SeismicFx.RingSeconds), e = SeismicFx.EaseOutCubic(k);
            float R = radius * e, W = Mathf.Lerp(SeismicFx.RingWidthFrom, SeismicFx.RingWidthTo, e);
            float fadeStart = 1f - SeismicFx.RingFadeTail, a = k < fadeStart ? SeismicFx.RingAlpha : SeismicFx.RingAlpha * (1f - (k - fadeStart) / SeismicFx.RingFadeTail);
            if (t >= SeismicFx.RingSeconds) a = 0f;
            // shader ring: centre radius (1 - w) of the unit quad, half-max width w -> quad half-size h = R + W, w = W / h
            float h = R + W, wfrac = Mathf.Clamp(W / Mathf.Max(h, .001f), .02f, .5f);
            if (mat != null) mat.SetFloat("_RingWidth", wfrac);
            Set(ring, 2f * h / s, Color.Lerp(SeismicFx.RingFrom, SeismicFx.RingTo, e), a);
            RingRadius = R; RingWidth = W; RingA = a;
            // ring 2 (ground shockwave): +WaveDelay, radius to WaveRadius x (splash / 1.35), alpha .4 (same fade shape), same width fraction
            float t2 = t - SeismicFx.WaveDelay, k2 = Mathf.Clamp01(t2 / SeismicFx.RingSeconds), e2 = SeismicFx.EaseOutCubic(k2);
            float R2 = SeismicFx.WaveRadius * (radius / SeismicFx.BaseSplash) * e2;
            float a2 = t2 < 0f || t2 >= SeismicFx.RingSeconds ? 0f : k2 < fadeStart ? SeismicFx.WaveAlpha : SeismicFx.WaveAlpha * (1f - (k2 - fadeStart) / SeismicFx.RingFadeTail);
            Set(wave, 2f * R2 / Mathf.Max(.001f, 1f - wfrac) / s, Color.Lerp(SeismicFx.RingFrom, SeismicFx.RingTo, e2), a2);
            WaveR = R2; WaveA = a2;
            if (t > SeismicFx.RingSeconds + SeismicFx.WaveDelay + .02f) started = -1f;
        }
        readonly ParticleSystem.Particle[] buf = new ParticleSystem.Particle[1];
        void Set(ParticleSystem p, float size, Color c, float alpha)
        {
            if (p == null) return;
            if (p.GetParticles(buf) < 1) return;
            buf[0].startSize = Mathf.Max(0f, size); c.a = Mathf.Clamp01(alpha); buf[0].startColor = c; buf[0].position = Vector3.zero;
            p.SetParticles(buf, 1);
        }
        void OnDisable() { started = -1f; }
        void OnDestroy() { if (mat != null) Destroy(mat); }
    }
}
