using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Coin drop + auto-collect (visual only, pooled, 3 particle systems = 3 draw calls max).
    /// Usage: CoinDropFx.Instance.Play(enemyPos, amount, goldCounterScreenPos, () => wallet.Add(...));
    /// Phases per coin: pop (bounce arc + sparkle) -> hold -> fly to UI target (trail) -> pickup flash + counter punch.
    /// Pooled manager: never destroys itself; particle budget per drop <= 8 (<=3 coins + 3 pop sparkles + 1 arrival sparkle + 1 flash).
    public class CoinDropFx : MonoBehaviour
    {
        public static CoinDropFx Instance { get; private set; }
        /// Fired when each coin arrives (after its own onArrive). Hook a UI punch here if you don't set counterPunch.
        public static event Action<int> CoinArrived;

        [Header("Refs (built by StylizedFxV14)")]
        public ParticleSystem coins;    // coin billboards + trail module
        public ParticleSystem sparkle;  // emit-only
        public ParticleSystem flash;    // emit-only, pickup flash
        [Tooltip("Optional: UI rect punched (scale) on each arrival, e.g. the gold counter pill.")]
        public RectTransform counterPunch;
        public Camera cam;

        [Header("Timing")]
        public float popTime = .55f, holdTime = .25f, flyTime = .45f, stagger = .07f;
        public float popHeight = 1.1f, popRadius = .7f, coinSize = .34f;
        [Tooltip("Max coins on screen (pool size)")] public int maxCoins = 48;

        struct Coin { public Vector3 start, land; public float t0; public Vector3 target; public bool screen; public Action cb; public int value; public bool last; }
        readonly List<Coin> live = new List<Coin>();
        ParticleSystem.Particle[] buf;
        float punch;
        float now;

        void Awake() { if (Instance == null) Instance = this; buf = new ParticleSystem.Particle[maxCoins]; if (!cam) cam = Camera.main; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// worldPos: enemy death position. amount: gold amount; 1-3 coins are spawned (split value). uiTarget: screen-space pixel
        /// position of the counter when targetIsScreen (default), otherwise a world position. onArrive is invoked once, when the LAST coin lands.
        public void Play(Vector3 worldPos, int amount, Vector3 uiTarget, Action onArrive = null, bool targetIsScreen = true)
        {
            int n = Mathf.Clamp(amount <= 5 ? 1 : amount <= 15 ? 2 : 3, 1, 3);
            for (int i = 0; i < n && live.Count < maxCoins; i++)
            {
                float a = (i + UnityEngine.Random.value * .6f) / n * Mathf.PI * 2;
                var land = worldPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * popRadius * UnityEngine.Random.Range(.5f, 1f);
                live.Add(new Coin { start = worldPos + Vector3.up * .3f, land = land, t0 = now + i * stagger, target = uiTarget, screen = targetIsScreen,
                                    cb = i == n - 1 ? onArrive : null, last = i == n - 1, value = i == n - 1 ? amount - (amount / n) * (n - 1) : amount / n });
            }
            if (sparkle) sparkle.Emit(new ParticleSystem.EmitParams { position = worldPos + Vector3.up * .4f, applyShapeToPosition = true }, 3);
        }

        /// Clear all in-flight coins and particles (pool return / level restart). Pending onArrive callbacks are dropped.
        public void Reset()
        {
            live.Clear(); punch = 0;
            if (coins) coins.Clear(); if (sparkle) sparkle.Clear(); if (flash) flash.Clear();
            if (counterPunch) counterPunch.localScale = Vector3.one;
        }
        public int InFlight => live.Count;

        /// Deterministic position of a coin at local time t (for previews / tests).
        public Vector3 Sample(Vector3 start, Vector3 land, Vector3 targetWorld, float t, out float scale)
        {
            scale = 1;
            if (t < popTime)
            {
                float u = t / popTime;                                                  // arc + 1 small bounce
                float h = u < .75f ? popHeight * 4 * (u / .75f) * (1 - u / .75f) : popHeight * .25f * 4 * ((u - .75f) / .25f) * (1 - (u - .75f) / .25f);
                var p = Vector3.Lerp(start, land, Mathf.Min(1, u / .75f)); p.y = Mathf.Lerp(start.y, land.y + .15f, Mathf.Min(1, u / .75f)) + h;
                scale = Mathf.Lerp(.4f, 1f, Mathf.Min(1, u * 3)); return p;
            }
            t -= popTime;
            var rest = land + Vector3.up * (.15f + .05f * Mathf.Sin(t * 12));
            if (t < holdTime) return rest;
            float k = Mathf.Clamp01((t - holdTime) / flyTime); k = k * k * (3 - 2 * k); k *= k;    // ease-in: slow lift then whoosh
            var mid = Vector3.Lerp(rest, targetWorld, .5f) + Vector3.up * 1.2f;
            scale = Mathf.Lerp(1, .6f, k);
            return Vector3.Lerp(Vector3.Lerp(rest, mid, k), Vector3.Lerp(mid, targetWorld, k), k);
        }

        Vector3 TargetWorld(in Coin c)
        {
            if (!c.screen || !cam) return c.target;
            float d = Vector3.Dot(c.land - cam.transform.position, cam.transform.forward) * .6f;   // in front of the board so trails read over it
            return cam.ScreenToWorldPoint(new Vector3(c.target.x, c.target.y, Mathf.Max(cam.nearClipPlane + .5f, d)));
        }

        void LateUpdate() => Tick(Time.deltaTime);
        /// Advance by dt (called from LateUpdate; public for previews/tests).
        public void Tick(float dt)
        {
            now += dt; if (buf == null || buf.Length != maxCoins) buf = new ParticleSystem.Particle[maxCoins];
            float total = popTime + holdTime + flyTime;
            int count = 0;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i]; float t = now - c.t0; if (t < 0) continue;
                var tw = TargetWorld(c);
                if (t >= total)
                {
                    if (c.cb != null || c.last) { if (flash) flash.Emit(new ParticleSystem.EmitParams { position = tw }, 1); }
                    else if (sparkle) sparkle.Emit(new ParticleSystem.EmitParams { position = tw, applyShapeToPosition = true }, 1);
                    punch = 1; c.cb?.Invoke(); CoinArrived?.Invoke(c.value);
                    live.RemoveAt(i); continue;
                }
                var p = Sample(c.start, c.land, tw, t, out float s);
                if (count < buf.Length)
                {
                    buf[count].position = p; buf[count].startSize = coinSize * s; buf[count].startColor = new Color32(255, 214, 70, 255);
                    buf[count].remainingLifetime = 10; buf[count].startLifetime = 10; buf[count].randomSeed = (uint)(c.t0 * 1000 + i);
                    buf[count].rotation3D = new Vector3(0, 0, 0);
                    count++;
                }
            }
            if (coins) coins.SetParticles(buf, count);
            if (counterPunch) { punch = Mathf.MoveTowards(punch, 0, dt * 6); counterPunch.localScale = Vector3.one * (1 + .22f * Mathf.Sin(punch * Mathf.PI)); }
        }
    }
}
