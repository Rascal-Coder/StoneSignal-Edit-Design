using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Priority order = enum order (higher wins the body tint). Icons show for every active status (max 3, highest first).
    public enum StatusId { Poison = 0, Slow = 1, Burn = 2, Shock = 3, Frost = 4 }

    /// v16.1 enemy status visuals. Put on the enemy root (PF_Enemy_*). Visual only: gameplay owns durations/effects, call
    ///   Apply(StatusId, duration)  (refreshes to the longer remaining time), Remove(id), ClearAll() (on death / pool return).
    /// Body: toon shader _StatusTint/_StatusRim (instancing buffer / MPB, no per-enemy materials).
    /// Particles: EnemyStatusFxSystem - ONE global ParticleSystem per status (cap 48), emitted at each affected enemy.
    /// Icons: pooled world-space SpriteRenderers (HUD atlas sprites ui_status_*) above the head, camera-facing.
    public class EnemyStatusFx : MonoBehaviour
    {
        public float headHeight = 1.2f;       // icon row height above root (set per enemy size)
        public float radius = .35f;           // emission radius around body
        static readonly int TintId = Shader.PropertyToID("_StatusTint"), RimId = Shader.PropertyToID("_StatusRim");
        static readonly Color[] Tint = { new Color(.45f, 1f, .35f, .35f), new Color(.6f, .45f, .95f, .25f), new Color(.35f, .2f, .15f, .3f), new Color(1f, .95f, .6f, .2f), new Color(.75f, .92f, 1f, .55f) };
        static readonly Color[] Rim  = { new Color(.4f, 1.4f, .3f, .6f), new Color(1f, .6f, 1.8f, .6f), new Color(2f, .8f, .2f, .8f), new Color(1.6f, 1.6f, .6f, 1f), new Color(.4f, 1.2f, 2.2f, 1f) };

        readonly float[] until = new float[5];
        Renderer[] bodies; MaterialPropertyBlock mpb; int shownTint = -2;
        readonly List<SpriteRenderer> icons = new List<SpriteRenderer>(3);
        public bool Has(StatusId id) => until[(int)id] > Time.time;
        public bool Any { get { for (int i = 0; i < 5; i++) if (until[i] > Time.time) return true; return false; } }

        void Awake()
        {
            var all = GetComponentsInChildren<Renderer>(true); var l = new List<Renderer>();
            foreach (var r in all) if (!(r is ParticleSystemRenderer) && !(r is SpriteRenderer)) l.Add(r);
            bodies = l.ToArray(); mpb = new MaterialPropertyBlock();
        }
        void OnDisable() => ClearAll();

        public void Apply(StatusId id, float duration)
        {
            until[(int)id] = Mathf.Max(until[(int)id], Time.time + Mathf.Max(0, duration));
            EnemyStatusFxSystem.Register(this); Refresh();
        }
        public void Remove(StatusId id) { until[(int)id] = 0; Refresh(); }
        public void ClearAll() { for (int i = 0; i < 5; i++) until[i] = 0; Refresh(); }

        /// Called by EnemyStatusFxSystem each frame while registered. Returns false when nothing is active (unregister).
        internal bool Tick(float dt)
        {
            bool any = false; float now = Time.time;
            for (int i = 0; i < 5; i++)
            {
                if (until[i] <= now) continue; any = true;
                EnemyStatusFxSystem.EmitFor((StatusId)i, transform.position, radius, headHeight, dt);
            }
            Refresh();
            var cam = Camera.main;
            for (int k = 0; k < icons.Count; k++)
            {
                var ic = icons[k]; float x = (k - (icons.Count - 1) * .5f) * .36f;
                Vector3 right = cam ? cam.transform.right : Vector3.right;
                ic.transform.position = transform.position + Vector3.up * headHeight + right * x;
                if (cam) ic.transform.rotation = cam.transform.rotation;
            }
            return any;
        }

        void Refresh()
        {
            float now = Time.time; int top = -1; int n = 0;
            for (int i = 4; i >= 0; i--) if (until[i] > now) { if (top < 0) top = i; n++; }
            if (top != shownTint && bodies != null)
            {
                shownTint = top;
                Color t = top < 0 ? Color.clear : Tint[top], r = top < 0 ? Color.clear : Rim[top];
                foreach (var b in bodies) { if (!b) continue; b.GetPropertyBlock(mpb); mpb.SetColor(TintId, t); mpb.SetColor(RimId, r); b.SetPropertyBlock(mpb); }
            }
            // icons: highest priority first, up to 3
            int want = Mathf.Min(3, n), k = 0;
            while (icons.Count < want) icons.Add(EnemyStatusFxSystem.RentIcon());
            while (icons.Count > want) { EnemyStatusFxSystem.ReturnIcon(icons[icons.Count - 1]); icons.RemoveAt(icons.Count - 1); }
            for (int i = 4; i >= 0 && k < want; i--) if (until[i] > now) icons[k++].sprite = EnemyStatusFxSystem.Icon((StatusId)i);
        }
    }

}
