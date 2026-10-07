using UnityEngine;
namespace StoneSignal.VFX
{
    /// v16.2 ember core damage states. Visual only: call SetHealth01(hp/maxHp) whenever HP changes, PlayHit() on each hit.
    /// 1.0-0.70 Intact, 0.70-0.40 Cracked (crack glow), 0.40-0.15 Broken (chunk mesh + smoke), <0.15 Critical (core red pulse + sparks).
    /// Enclosure = mesh swap on one MeshFilter (SM_Core_Enclosure_Intact/Cracked/Broken).
    /// v17.3: lives on PF_Core_Enclosure (StylizedCoreV173): each state is ONE merged palette mesh (M_Core_Enclosure, crack glow = vertex R x
    /// _VColorEmission), smoke = PF_VFX_CoreSmoke (8), sparks = PF_VFX_CoreSparks (8); GridView fills coreRenderers.
    /// v17.5: hit / critical flash = whole-object red tint + fresnel rim through the existing per-instance ToonCore props _StatusTint /
    /// _StatusRim (same MPB path as EnemyStatusFx: instancing buffer SSWallProps or UnityPerMaterial, no keyword, no new material),
    /// on every core renderer (GridView: SM_Prop_Core_01_Base = plinth + pillars + crystal) AND the enclosure renderer (weaker, enclosureFlash).
    /// The v17.4 _HiColor/_HiAmount ring only lit upward faces 0.34-0.46 m from the centre. PlayHit = 0.12 s flash; Critical = 1.5 Hz pulse.
    /// Idle -> SetPropertyBlock(null) on all of them (SRP batching restored, draw calls unchanged: 1 renderer = 1 DC either way).
    public class CoreDamageFx : MonoBehaviour
    {
        public enum Stage { Intact, Cracked, Broken, Critical }
        [Range(0, 1)] public float crackedBelow = .70f, brokenBelow = .40f, criticalBelow = .15f;
        public MeshFilter enclosure; public Mesh intactMesh, crackedMesh, brokenMesh;
        public Renderer[] coreRenderers;
        public ParticleSystem smoke, sparks;            // pooled, maxParticles 8 each (<=16 total)
        [Tooltip("v17.5: hit flash colour (gamma, #FF5A5A); _StatusTint = colour.linear x tintBoost, _StatusRim = colour.linear x rimBoost")] public Color hitColor = new Color(1f, .353f, .353f);
        [Tooltip("v17.5: critical pulse colour (gamma, #FF8087 = the v17.3 mockup's pink-red core)")] public Color criticalColor = new Color(1f, .502f, .529f);
        [Tooltip("v17.5: tint brightness. ToonCore tint is c*.55 + tint*.45, so the mockup's LIGHTER pink core (#A696B5 -> #F7A4BD) needs tint > 1; 2.3 keeps lit tops < bloom threshold 1.1")] public float tintBoost = 2.3f;
        [Tooltip("v17.5: hit flash length (s), linear fade")] public float hitDuration = .12f;
        [Tooltip("v17.5: _StatusTint alpha at the start of a hit")] public float hitTint = .65f;
        [Tooltip("v17.5: critical pulse frequency (Hz)")] public float criticalHz = 1.5f;
        [Tooltip("v17.5: _StatusTint alpha over the critical pulse (min, max); max ~ the v17.3 mockup red core")] public Vector2 criticalTint = new Vector2(.20f, .55f);
        [Tooltip("v17.5: _StatusRim colour = flash colour x rimBoost, alpha = tint alpha")] public float rimBoost = 1.2f;
        [Tooltip("v17.5: flash strength on the enclosure relative to the core prop")] [Range(0, 1)] public float enclosureFlash = .6f;
        public Stage Current { get; private set; } = Stage.Intact;
        static readonly int StatusTint = Shader.PropertyToID("_StatusTint"), StatusRim = Shader.PropertyToID("_StatusRim");
        MaterialPropertyBlock mpb; Renderer encRenderer; float hit, health = 1; bool flashing;

        void Awake() { if (enclosure) encRenderer = enclosure.GetComponent<Renderer>(); }

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
            hit = Mathf.MoveTowards(hit, 0, Time.deltaTime / Mathf.Max(.01f, hitDuration));
            float hitA = hit * hitTint;
            float crit = Current == Stage.Critical ? Mathf.Lerp(criticalTint.x, criticalTint.y, .5f - .5f * Mathf.Cos(Time.time * criticalHz * 2f * Mathf.PI)) : 0;
            float a = Mathf.Max(hitA, crit);
            Color c = (hitA > crit ? hitColor : criticalColor).linear;   // SetVector: values go to the shader as-is (linear)
            if (a <= .001f) { if (flashing) { Apply(null, 0, c); flashing = false; } }
            else { Apply(mpb ??= new MaterialPropertyBlock(), a, c); flashing = true; }
            if (hit <= 0 && Current != Stage.Critical && !flashing) enabled = false;
        }

        void Apply(MaterialPropertyBlock block, float a, Color c)
        {
            if (coreRenderers != null) foreach (var r in coreRenderers) Set(r, block, a, c);
            if (!encRenderer && enclosure) encRenderer = enclosure.GetComponent<Renderer>();
            Set(encRenderer, block, a * enclosureFlash, c);
        }
        void Set(Renderer r, MaterialPropertyBlock block, float a, Color c)
        {
            if (!r) return;
            if (block == null) { r.SetPropertyBlock(null); return; }   // idle: restore SRP batching
            r.GetPropertyBlock(block);
            block.SetVector(StatusTint, new Vector4(c.r * tintBoost, c.g * tintBoost, c.b * tintBoost, a));
            block.SetVector(StatusRim, new Vector4(c.r * rimBoost, c.g * rimBoost, c.b * rimBoost, a));   // fresnel rim
            r.SetPropertyBlock(block);
        }
    }
}
