using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// v16.2 enemy death: painted toon "poof" (4x4 flipbook T_FX_DeathPuff_4x4, 1 particle per death + up to 3 sparkle puffs, <=8 total)
    /// as the enemy vanishes, then a fixed cute skull (ui_fx_skull, world billboard) pops with overshoot + small hop, holds 0.5 s, fades;
    /// onDone fires at the fade start so the coin drop (CoinDropFx) follows. Pooled; 1-2 DC (shared puff material + shared sprite material).
    /// Put PF_VFX_EnemyDeath once in the scene (or it is created from Resources-free defaults at first Play and needs `puff`/`skullSprite` set).
    public class EnemyDeathFx : MonoBehaviour
    {
        public ParticleSystem puff;            // shared, world space, texture sheet 4x4, maxParticles 8
        public Sprite skullSprite;             // ui_fx_skull
        public Material skullMaterial;         // Sprites-Default (shared)
        public float skullSize = .55f, skullHeight = .9f, hold = .5f, fade = .25f;
        static EnemyDeathFx inst;
        readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();

        void Awake() { inst = this; if (puff) { var m = puff.main; m.maxParticles = 8; } }

        /// Play at the enemy's position (hide/despawn the enemy yourself on the same frame). scale ~ enemy size.
        public static void Play(Vector3 pos, float scale = 1f, Action onDone = null)
        {
            if (!inst) inst = FindObjectOfType<EnemyDeathFx>();
            if (!inst) { onDone?.Invoke(); return; }
            inst.PlayInternal(pos, scale, onDone);
        }

        void PlayInternal(Vector3 pos, float scale, Action onDone)
        {
            if (puff && puff.particleCount < 8)
            {
                var ep = new ParticleSystem.EmitParams { position = pos + Vector3.up * .35f * scale, startSize = 1.4f * scale, applyShapeToPosition = false };
                puff.Emit(ep, 1);
            }
            StartCoroutine(Skull(pos, scale, onDone));
        }

        IEnumerator Skull(Vector3 pos, float scale, Action onDone)
        {
            yield return new WaitForSeconds(.18f);   // let the poof cover the vanish first
            var sr = pool.Count > 0 ? pool.Pop() : NewSkull(); sr.gameObject.SetActive(true); sr.color = Color.white;
            var cam = Camera.main; Vector3 basePos = pos + Vector3.up * skullHeight * scale;
            float t = 0, pop = .28f;
            while (t < pop + hold + fade)
            {
                t += Time.deltaTime;
                float s;
                if (t < pop) { float k = t / pop; s = Mathf.Sin(k * Mathf.PI * .5f) * (1 + .25f * Mathf.Sin(k * Mathf.PI)); }   // overshoot ~1.18
                else s = 1 + .04f * Mathf.Sin((t - pop) * 9f) * Mathf.Exp(-(t - pop) * 6f);                                  // settle wobble
                float hop = t < pop ? Mathf.Sin(t / pop * Mathf.PI) * .18f : 0;
                sr.transform.position = basePos + Vector3.up * (hop + .08f * Mathf.Min(1, t / (pop + hold)));
                sr.transform.localScale = Vector3.one * skullSize * scale * s;
                if (cam) sr.transform.rotation = cam.transform.rotation;
                if (t > pop + hold) { if (onDone != null) { onDone(); onDone = null; } sr.color = new Color(1, 1, 1, 1 - (t - pop - hold) / fade); }
                yield return null;
            }
            onDone?.Invoke();
            sr.gameObject.SetActive(false); pool.Push(sr);
        }

        SpriteRenderer NewSkull()
        {
            var go = new GameObject("Skull"); go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = skullSprite; if (skullMaterial) sr.sharedMaterial = skullMaterial; sr.sortingOrder = 60; return sr;
        }
    }
}
