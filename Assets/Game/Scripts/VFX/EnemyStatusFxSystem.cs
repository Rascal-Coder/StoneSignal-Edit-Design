using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Global pools: 5 particle systems (one per status, max 48 particles each, world space) + icon sprite pool.
    /// Create once per scene (auto-created on first Apply). Assign sprites/materials from ArtCatalog or the prefab PF_VFX_StatusFx.
    public class EnemyStatusFxSystem : MonoBehaviour
    {
        public const int CapPerStatus = 48;
        public Sprite[] icons = new Sprite[5];          // ui_status_poison, slow, burn, shock, frost (StatusId order)
        public ParticleSystem[] systems = new ParticleSystem[5];
        public float[] ratePerSec = { 5, 4, 10, 7, 5 };
        public Material iconMaterial;                   // Sprites-Default or UI atlas material
        static EnemyStatusFxSystem inst;
        static readonly HashSet<EnemyStatusFx> active = new HashSet<EnemyStatusFx>(); static readonly List<EnemyStatusFx> drop = new List<EnemyStatusFx>();
        static readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
        static readonly float[] acc = new float[5];
        public static int ActiveCount => active.Count;

        static EnemyStatusFxSystem I { get { if (!inst) inst = FindObjectOfType<EnemyStatusFxSystem>() ?? new GameObject("[EnemyStatusFx]").AddComponent<EnemyStatusFxSystem>(); return inst; } }
        void Awake() { inst = this; foreach (var s in systems) if (s) { var m = s.main; m.maxParticles = CapPerStatus; m.simulationSpace = ParticleSystemSimulationSpace.World; } }
        public static void Register(EnemyStatusFx fx) { I.enabled = true; active.Add(fx); }
        public static Sprite Icon(StatusId id) => I.icons != null && (int)id < I.icons.Length ? I.icons[(int)id] : null;
        public static SpriteRenderer RentIcon()
        {
            if (pool.Count > 0) { var p = pool.Pop(); if (p) { p.gameObject.SetActive(true); return p; } }
            var go = new GameObject("StatusIcon"); go.transform.SetParent(I.transform, false); go.transform.localScale = Vector3.one * .32f;
            var sr = go.AddComponent<SpriteRenderer>(); if (I.iconMaterial) sr.sharedMaterial = I.iconMaterial; sr.sortingOrder = 50; return sr;
        }
        public static void ReturnIcon(SpriteRenderer sr) { if (!sr) return; sr.gameObject.SetActive(false); pool.Push(sr); }
        internal static void EmitFor(StatusId id, Vector3 pos, float radius, float head, float dt)
        {
            var s = I.systems[(int)id]; if (!s) return;
            int i = (int)id; float n = I.ratePerSec[i] * dt + Random.value * .01f; int c = Mathf.FloorToInt(n + Random.value);
            if (c <= 0 || s.particleCount >= CapPerStatus) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int k = 0; k < c; k++)
            {
                var o = Random.insideUnitCircle * radius;
                float y = id == StatusId.Slow ? .05f : id == StatusId.Poison ? Random.Range(.2f, head * .7f) : Random.Range(.25f, head * .85f);
                ep.position = pos + new Vector3(o.x, y, o.y); s.Emit(ep, 1);
            }
        }
        void LateUpdate()
        {
            drop.Clear(); float dt = Time.deltaTime;
            foreach (var fx in active) if (!fx || !fx.isActiveAndEnabled || !fx.Tick(dt)) drop.Add(fx);
            foreach (var fx in drop) active.Remove(fx);
        }
    }
}
