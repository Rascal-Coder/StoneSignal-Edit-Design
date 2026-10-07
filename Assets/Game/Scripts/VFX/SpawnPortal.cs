using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// v17.2 spawn portal (PF_VFX_SpawnPortal). Idle (calm): light warm scorch, thick notched ember ring that slowly rotates with a gentle
    /// emissive pulse + soft halo, faint inner glyph (all in SS_SpawnPortal, 1 DC; runestones = ArtCatalog toggle).
    /// PlaySpawn: 0-0.3 s bright ember flash (ground shader flash + expanding ring wave + alpha-blended HDR Flash billboard + additive painted 4x4 Flare),
    /// cracks glow, rubble pops (pooled <=8), painted dust puffs (2x2 sheet); enemy rises from y-0.9 clipped by toon _GroundClip, then onDone.
    /// API unchanged: PlaySpawn(Transform, float, Action), SetActive(bool).
    public class SpawnPortal : MonoBehaviour
    {
        public Renderer ground;               // SS_SpawnPortal quad
        public ParticleSystem dust, flare;    // shared bursts (emit on demand)
        [Tooltip("v17.2: additive soft flash billboard (optional)")] public ParticleSystem flash;
        public Transform[] rubble;            // pooled chunks (<=8), hidden when idle
        public float riseDepth = .9f, crackDecay = 1.1f;
        [Tooltip("v17.2: flare envelope: full for flareHold s, then fades to 0 at flareLength s")] public float flareHold = .14f, flareLength = .55f;
        [HideInInspector] public float flareDecay = 2.2f;   // v16.2 field kept for serialized prefabs (unused)
        static readonly int FlareId = Shader.PropertyToID("_Flare"), FlareTId = Shader.PropertyToID("_FlareT"), ActiveId = Shader.PropertyToID("_Active"), CrackId = Shader.PropertyToID("_Crack"), ClipId = Shader.PropertyToID("_GroundClip");
        MaterialPropertyBlock mpb; static MaterialPropertyBlock enemyMpb;
        float flareV, flareT = 1, crackV, active = 1, rubbleT = -1, spawnT = -1;
        readonly Vector3[] rubbleVel = new Vector3[8];
        static readonly List<Renderer> tmp = new List<Renderer>();

        void Awake() { mpb = new MaterialPropertyBlock(); if (rubble != null) foreach (var r in rubble) if (r) r.gameObject.SetActive(false); Push(); }

        public void SetActive(bool on) { active = on ? 1 : 0; Push(); }

        public void PlaySpawn(Transform enemy, float duration = .6f, Action onDone = null)
        {
            spawnT = 0; flareV = 1; flareT = 0; crackV = 1; Push();
            if (flash) flash.Emit(1);
            if (flare) flare.Emit(1);
            if (dust) dust.Emit(6);
            PopRubble();
            if (!enemy) { onDone?.Invoke(); return; }
            StartCoroutine(Rise(enemy, Mathf.Max(.05f, duration), onDone));
        }

        void PopRubble()
        {
            if (rubble == null) return; rubbleT = 0;
            for (int i = 0; i < rubble.Length && i < 8; i++)
            {
                var r = rubble[i]; if (!r) continue; r.gameObject.SetActive(true);
                float a = i * 0.9f + UnityEngine.Random.value * .5f; float d = UnityEngine.Random.Range(.25f, .6f);
                r.localPosition = new Vector3(Mathf.Cos(a) * d, -.05f, Mathf.Sin(a) * d); r.localRotation = UnityEngine.Random.rotation;
                rubbleVel[i] = new Vector3(Mathf.Cos(a) * .6f, UnityEngine.Random.Range(1.8f, 2.6f), Mathf.Sin(a) * .6f);
            }
        }

        IEnumerator Rise(Transform enemy, float dur, Action onDone)
        {
            float groundY = transform.position.y;
            Vector3 end = enemy.position; end.y = Mathf.Max(end.y, groundY);
            enemy.GetComponentsInChildren(true, tmp); var rs = tmp.ToArray();
            SetClip(rs, new Vector4(groundY, 1, 0, 0));
            for (float t = 0; t < dur && enemy; t += Time.deltaTime)
            {
                float k = 1 - Mathf.Pow(1 - t / dur, 3);
                enemy.position = new Vector3(end.x, end.y - riseDepth * (1 - k), end.z);
                yield return null;
            }
            if (enemy) { enemy.position = end; SetClip(rs, Vector4.zero); }
            onDone?.Invoke();
        }

        static void SetClip(Renderer[] rs, Vector4 clip)
        {
            enemyMpb ??= new MaterialPropertyBlock();
            foreach (var r in rs) { if (!r || r is ParticleSystemRenderer) continue; r.GetPropertyBlock(enemyMpb); enemyMpb.SetVector(ClipId, clip); r.SetPropertyBlock(enemyMpb); }
        }

        void Update()
        {
            float dt = Time.deltaTime; bool dirty = false;
            if (spawnT >= 0)
            {
                spawnT += dt; float len = Mathf.Max(flareHold + .01f, flareLength);
                flareV = spawnT < flareHold ? 1 : Mathf.Clamp01(1 - (spawnT - flareHold) / (len - flareHold));
                flareT = Mathf.Clamp01(spawnT / len);
                if (spawnT >= len) { spawnT = -1; flareV = 0; flareT = 1; }
                dirty = true;
            }
            if (crackV > 0) { crackV = Mathf.Max(0, crackV - dt * crackDecay); dirty = true; }
            if (dirty) Push();
            if (rubbleT >= 0 && rubble != null)
            {
                rubbleT += dt; bool any = false;
                for (int i = 0; i < rubble.Length && i < 8; i++)
                {
                    var r = rubble[i]; if (!r || !r.gameObject.activeSelf) continue;
                    rubbleVel[i].y -= 9f * dt; var p = r.localPosition + rubbleVel[i] * dt;
                    if (p.y < 0 && rubbleVel[i].y < 0) { p.y = 0; rubbleVel[i] *= .3f; rubbleVel[i].y = 0; }
                    r.localPosition = p; r.Rotate(220 * dt, 0, 140 * dt, Space.Self);
                    if (rubbleT > 1.1f) r.localScale = Vector3.one * Mathf.Max(0, 1 - (rubbleT - 1.1f) * 3); // shrink out
                    if (rubbleT > 1.45f) { r.gameObject.SetActive(false); r.localScale = Vector3.one; } else any = true;
                }
                if (!any) rubbleT = -1;
            }
        }
        void Push()
        {
            if (!ground) return; mpb ??= new MaterialPropertyBlock();
            ground.GetPropertyBlock(mpb); mpb.SetFloat(FlareId, flareV); mpb.SetFloat(FlareTId, flareT); mpb.SetFloat(ActiveId, active); mpb.SetFloat(CrackId, crackV); ground.SetPropertyBlock(mpb);
        }
    }
}
