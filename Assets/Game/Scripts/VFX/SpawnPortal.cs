using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// v16.2 art-led spawn portal (PF_VFX_SpawnPortal). Idle: painted rune ring breathing on a scorched decal, 3-4 runestones (1 merged toon mesh).
    /// PlaySpawn: cracks light up + ring flare, rubble chunks pop (pooled, <=8, shared), dust puff + painted 4x4 flipbook flare (shared emitters),
    /// enemy rises from y-0.9 (ease-out) clipped at ground by toon _GroundClip MPB, then onDone (start walking).
    /// Steady state: 2 DC (ground quad + runestones). API unchanged from v16.1.
    public class SpawnPortal : MonoBehaviour
    {
        public Renderer ground;               // SS_SpawnPortal quad
        public ParticleSystem dust, flare;    // shared bursts (emit on demand)
        public Transform[] rubble;            // pooled chunks (<=8), hidden when idle
        public float riseDepth = .9f, flareDecay = 2.2f, crackDecay = 1.1f;
        static readonly int FlareId = Shader.PropertyToID("_Flare"), ActiveId = Shader.PropertyToID("_Active"), CrackId = Shader.PropertyToID("_Crack"), ClipId = Shader.PropertyToID("_GroundClip");
        MaterialPropertyBlock mpb; static MaterialPropertyBlock enemyMpb;
        float flareV, crackV, active = 1, rubbleT = -1;
        readonly Vector3[] rubbleVel = new Vector3[8];
        static readonly List<Renderer> tmp = new List<Renderer>();

        void Awake() { mpb = new MaterialPropertyBlock(); if (rubble != null) foreach (var r in rubble) if (r) r.gameObject.SetActive(false); Push(); }

        public void SetActive(bool on) { active = on ? 1 : 0; Push(); }

        public void PlaySpawn(Transform enemy, float duration = .6f, Action onDone = null)
        {
            flareV = 1; crackV = 1; Push();
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
            if (flareV > 0) { flareV = Mathf.Max(0, flareV - dt * flareDecay); dirty = true; }
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
            ground.GetPropertyBlock(mpb); mpb.SetFloat(FlareId, flareV); mpb.SetFloat(ActiveId, active); mpb.SetFloat(CrackId, crackV); ground.SetPropertyBlock(mpb);
        }
    }
}
