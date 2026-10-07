using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Generic pooled reward fly: pop (squash) -> bounce -> short hold -> curved fly with trail -> absorb flash at the UI target.
    /// One instance per RewardType (prefabs PF_VFX_RewardFly_Gold/_Shard/_Gem; PF_VFX_CoinDrop = gold, CoinDropFx API kept).
    /// Particle budget per Play <= 8: <=3 icons + 3 pop sparkles + 1 per-icon absorb sparkle (non-last) + 1 absorb flash (last).
    /// Never destroys itself; Reset() clears in-flight rewards.
    public class RewardFlyFx : MonoBehaviour
    {
        static readonly Dictionary<string, RewardFlyFx> byId = new Dictionary<string, RewardFlyFx>();
        /// SFX hook: (sfxId, worldPos). Wire to your audio system.
        public static event Action<string, Vector3> Sfx;
        /// Fired per icon arrival: (type id, value carried by this icon).
        public static event Action<string, int> Arrived;

        public RewardType type;
        public ParticleSystem icons, sparkle, flash;
        [Tooltip("Runtime UI target (counter). Overrides screen/world targets passed to Play when set.")] public RectTransform target;
        public RewardCounterUI counter;
        public Camera cam;
        public float popTime = .5f, holdTime = .18f, flyTime = .5f, stagger = .08f, popHeight = 1.0f, popRadius = .65f;
        public int maxIcons = 48;

        struct Item { public Vector3 start, land; public float t0, spin; public Vector3 tgt; public bool screen, last; public Action cb; public int value; }
        readonly List<Item> live = new List<Item>();
        ParticleSystem.Particle[] buf; float now;

        public static RewardFlyFx Get(string typeId) => byId.TryGetValue(typeId, out var f) ? f : null;
        protected virtual void Awake() { if (type) byId[type.id] = this; if (!cam) cam = Camera.main; }
        protected virtual void OnDestroy() { if (type && byId.TryGetValue(type.id, out var f) && f == this) byId.Remove(type.id); }
        public void BindTarget(RectTransform t, RewardCounterUI c = null) { target = t; counter = c; }

        /// worldPos: source (enemy death). amount: value. uiTarget: screen px (targetIsScreen) or world pos; ignored when `target` is bound.
        /// onArrive: once, when the last icon lands.
        public void Play(Vector3 worldPos, int amount, Vector3 uiTarget, Action onArrive = null, bool targetIsScreen = true)
        {
            int tw = type ? type.twoAt : 6, th = type ? type.threeAt : 16;
            int n = amount <= tw ? 1 : amount <= th ? 2 : 3;
            for (int i = 0; i < n && live.Count < maxIcons; i++)
            {
                float a = (i + .15f + UnityEngine.Random.value * .5f) / n * Mathf.PI * 2;
                live.Add(new Item { start = worldPos + Vector3.up * .35f, land = worldPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * popRadius * UnityEngine.Random.Range(.6f, 1f),
                                    t0 = now + i * stagger, spin = UnityEngine.Random.Range(-1f, 1f), tgt = uiTarget, screen = targetIsScreen, last = i == n - 1,
                                    cb = i == n - 1 ? onArrive : null, value = i == n - 1 ? amount - (amount / n) * (n - 1) : amount / n });
            }
            if (sparkle) sparkle.Emit(new ParticleSystem.EmitParams { position = worldPos + Vector3.up * .45f, applyShapeToPosition = true }, 3);
            Sfx?.Invoke(type ? type.sfxPop : "reward_pop", worldPos);
        }
        public void Reset()
        {
            live.Clear(); if (icons) icons.Clear(); if (sparkle) sparkle.Clear(); if (flash) flash.Clear();
            if (counter) counter.ResetFx();
        }
        public int InFlight => live.Count;

        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }
        /// Deterministic sample: position + non-uniform scale (squash/stretch) at local time t.
        public Vector3 Sample(Vector3 start, Vector3 land, Vector3 tw, float t, out Vector2 scale)
        {
            scale = Vector2.one;
            if (t < popTime)
            {
                float u = t / popTime, a = Mathf.Min(1, u / .7f);
                float h = u < .7f ? popHeight * 4 * a * (1 - a) : popHeight * .22f * 4 * ((u - .7f) / .3f) * (1 - (u - .7f) / .3f);
                var p = Vector3.Lerp(start, land, Ease(a) * .4f + a * .6f); p.y = Mathf.Lerp(start.y, land.y + .2f, a) + h;
                if (u < .12f) scale = new Vector2(1.3f, .7f) * Mathf.Lerp(.5f, 1, u / .12f);              // pop squash
                else if (Mathf.Abs(u - .7f) < .06f) scale = new Vector2(1.25f, .75f);                     // landing squash
                else if (u < .7f) scale = new Vector2(.9f, 1.12f);                                       // stretch in air
                return p;
            }
            t -= popTime;
            var rest = land + Vector3.up * (.2f + .04f * Mathf.Sin(t * 14));
            if (t < holdTime) { scale = Vector2.one * (1 + .08f * Mathf.Sin(t / holdTime * Mathf.PI)); return rest; }
            float k = Ease((t - holdTime) / flyTime); k = k * k;                                       // anticipation then whoosh
            var side = Vector3.Cross((tw - rest).normalized, Vector3.up) * .9f;
            var c1 = rest + Vector3.up * 1.6f + side; var c2 = tw + Vector3.down * .8f + side * .4f;      // cubic bezier
            float o = 1 - k; var pos = o * o * o * rest + 3 * o * o * k * c1 + 3 * o * k * k * c2 + k * k * k * tw;
            scale = new Vector2(Mathf.Lerp(1, .55f, k) * (.75f + .25f * Mathf.Abs(Mathf.Cos(k * 9))), Mathf.Lerp(1, .55f, k));   // flip glint
            return pos;
        }

        Vector3 TargetWorld(in Item c)
        {
            if (target && cam)
            {
                var sp = RectTransformUtility.WorldToScreenPoint(target.GetComponentInParent<Canvas>()?.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam, target.position);
                return ScreenToWorld(sp, c.land);
            }
            return c.screen && cam ? ScreenToWorld(c.tgt, c.land) : c.tgt;
        }
        Vector3 ScreenToWorld(Vector3 sp, Vector3 refPos)
        {
            float d = Vector3.Dot(refPos - cam.transform.position, cam.transform.forward) * .6f;
            return cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, Mathf.Max(cam.nearClipPlane + .5f, d)));
        }

        void LateUpdate() => Tick(Time.deltaTime);
        public void Tick(float dt)
        {
            now += dt; if (buf == null || buf.Length != maxIcons) buf = new ParticleSystem.Particle[maxIcons];
            float total = popTime + holdTime + flyTime, sz = type ? type.size : .42f; int count = 0;
            var tint = type ? (Color32)type.tint : (Color32)Color.white;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i]; float t = now - c.t0; if (t < 0) continue;
                var tw = TargetWorld(c);
                if (t >= total)
                {
                    if (c.last) { if (flash) flash.Emit(new ParticleSystem.EmitParams { position = tw }, 1); }
                    else if (sparkle) sparkle.Emit(new ParticleSystem.EmitParams { position = tw, applyShapeToPosition = true }, 1);
                    Sfx?.Invoke(type ? type.sfxArrive : "reward_arrive", tw);
                    if (counter) counter.Add(c.value);
                    c.cb?.Invoke(); Arrived?.Invoke(type ? type.id : "gold", c.value);
                    live.RemoveAt(i); continue;
                }
                var p = Sample(c.start, c.land, tw, t, out var s);
                if (count < buf.Length)
                {
                    ref var q = ref buf[count++];
                    q.position = p; q.startSize3D = new Vector3(sz * s.x, sz * s.y, 1); q.startColor = tint;
                    q.rotation = c.spin * 12 * Mathf.Sin(t * 5); q.remainingLifetime = 10; q.startLifetime = 10; q.randomSeed = (uint)(i * 7919 + 1);
                }
            }
            if (icons) icons.SetParticles(buf, count);
        }
    }

}
