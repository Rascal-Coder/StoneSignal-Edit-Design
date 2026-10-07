using UnityEngine;
namespace StoneSignal.VFX
{
    /// v16.2 ember core damage states. Visual only: call SetHealth01(hp/maxHp) whenever HP changes, PlayHit() on each hit.
    /// 1.0-0.70 Intact, 0.70-0.40 Cracked (crack glow), 0.40-0.15 Broken (chunk mesh + smoke), <0.15 Critical (core red flicker + sparks).
    /// Enclosure = mesh swap on one MeshFilter (SM_Core_Enclosure_Intact/Cracked/Broken). Core tint/flash = instanced _HiColor/_HiAmount (MPB, batching-safe).
    /// v17.3: lives on PF_Core_Enclosure (StylizedCoreV173): each state is ONE merged palette mesh (M_Core_Enclosure, crack glow = vertex R x
    /// _VColorEmission), smoke = PF_VFX_CoreSmoke (8), sparks = PF_VFX_CoreSparks (8); GridView fills coreRenderers.
    public class CoreDamageFx : MonoBehaviour
    {
        public enum Stage { Intact, Cracked, Broken, Critical }
        [Range(0, 1)] public float crackedBelow = .70f, brokenBelow = .40f, criticalBelow = .15f;
        public MeshFilter enclosure; public Mesh intactMesh, crackedMesh, brokenMesh;
        public Renderer[] coreRenderers;
        public ParticleSystem smoke, sparks;            // pooled, maxParticles 8 each (<=16 total)
        public Color hitColor = new Color(1f, .95f, .85f), criticalColor = new Color(1f, .12f, .08f);
        public Stage Current { get; private set; } = Stage.Intact;
        static readonly int HiColor = Shader.PropertyToID("_HiColor"), HiAmount = Shader.PropertyToID("_HiAmount");
        MaterialPropertyBlock mpb; float hit, health = 1;

        public void SetHealth01(float h)
        {
            health = Mathf.Clamp01(h);
            var s = health < criticalBelow ? Stage.Critical : health < brokenBelow ? Stage.Broken : health < crackedBelow ? Stage.Cracked : Stage.Intact;
            if (s == Current && enclosure && enclosure.sharedMesh) return;
            Current = s;
            if (enclosure) enclosure.sharedMesh = s == Stage.Intact ? intactMesh : s == Stage.Cracked ? crackedMesh : brokenMesh;
            Toggle(smoke, s >= Stage.Broken); Toggle(sparks, s == Stage.Critical);
            enabled = true;
        }
        public void PlayHit() { hit = 1; enabled = true; if (sparks && Current < Stage.Critical) sparks.Emit(3); }

        static void Toggle(ParticleSystem p, bool on) { if (!p) return; if (on && !p.isPlaying) p.Play(); else if (!on && p.isPlaying) p.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        void Update()
        {
            hit = Mathf.MoveTowards(hit, 0, Time.deltaTime * 6f);
            float crit = Current == Stage.Critical ? (.35f + .35f * Mathf.PerlinNoise(Time.time * 9f, 0)) : 0;
            float amt = Mathf.Max(hit, crit);
            Color c = hit > crit ? hitColor : criticalColor;
            mpb ??= new MaterialPropertyBlock();
            foreach (var r in coreRenderers)
            {
                if (!r) continue;
                if (amt <= 0.001f) r.SetPropertyBlock(null);    // restore SRP batching when idle
                else { r.GetPropertyBlock(mpb); mpb.SetColor(HiColor, c); mpb.SetFloat(HiAmount, amt); r.SetPropertyBlock(mpb); }
            }
            if (hit <= 0 && Current != Stage.Critical) enabled = false;
        }
    }
}
