using System.Collections.Generic;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Spawner for FX_* prefabs in Assets/Game/VFX/Stylized. In play mode every instance is pooled per prefab
    /// (WeChat mini-game: no per-hit Instantiate/Destroy); one-shots return to the pool after their particle lifetime,
    /// looping bodies (projectile trails) are returned by their owner through Release. Edit mode instantiates as before.
    public static class StylizedVfx
    {
        static readonly Dictionary<GameObject, Stack<PooledVfx>> pools = new Dictionary<GameObject, Stack<PooledVfx>>();
        static Transform root;
        public static int Created { get; private set; }

        public static GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null, Transform parent = null)
        {
            if (!prefab) return null;
            var rot = rotation ?? prefab.transform.rotation;
            if (!Application.isPlaying)
            {
                var edit = Object.Instantiate(prefab, position, rot, parent);
                PlayRoot(edit); return edit;
            }
            if (!pools.TryGetValue(prefab, out var stack)) pools[prefab] = stack = new Stack<PooledVfx>();
            PooledVfx item = null;
            while (stack.Count > 0 && item == null) item = stack.Pop(); // destroyed entries (scene reload) are skipped
            if (item == null)
            {
                var go = Object.Instantiate(prefab, position, rot, parent);
                item = go.AddComponent<PooledVfx>(); item.Setup(prefab); Created++;
            }
            else
            {
                item.transform.SetParent(parent, false);
                item.transform.SetPositionAndRotation(position, rot);
                item.transform.localScale = prefab.transform.localScale;
                item.gameObject.SetActive(true);
            }
            item.Begin();
            PlayRoot(item.gameObject);
            return item.gameObject;
        }

        static void PlayRoot(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) if (ps.transform == go.transform || ps.transform.parent == go.transform) { ps.Play(true); break; }
        }

        /// Return a pooled instance early (owner-managed looping VFX). Non-pooled objects are destroyed.
        public static void Release(GameObject go)
        {
            if (!go) return;
            var item = go.GetComponent<PooledVfx>();
            if (item == null || !Application.isPlaying) { Object.Destroy(go); return; }
            if (!item.gameObject.activeSelf && item.InPool) return;
            if (root == null) { root = new GameObject("[VFX pool]").transform; }
            foreach (var ps in item.Systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var t in item.Trails) t.Clear();
            item.gameObject.SetActive(false);
            item.transform.SetParent(root, false);
            item.InPool = true;
            if (!pools.TryGetValue(item.Prefab, out var stack)) pools[item.Prefab] = stack = new Stack<PooledVfx>();
            stack.Push(item);
        }
    }

    /// Pool bookkeeping added at runtime; disables the prefab's stopAction=Destroy so instances survive for reuse.
    public sealed class PooledVfx : MonoBehaviour
    {
        public GameObject Prefab { get; private set; }
        public bool InPool { get; set; }
        public ParticleSystem[] Systems { get; private set; }
        public TrailRenderer[] Trails { get; private set; }
        float lifetime, age; bool looping;

        public void Setup(GameObject prefab)
        {
            Prefab = prefab;
            Systems = GetComponentsInChildren<ParticleSystem>(true);
            Trails = GetComponentsInChildren<TrailRenderer>(true);
            lifetime = 0; looping = false;
            foreach (var ps in Systems)
            {
                var main = ps.main;
                if (main.stopAction == ParticleSystemStopAction.Destroy) main.stopAction = ParticleSystemStopAction.None;
                if (main.loop) looping = true;
                lifetime = Mathf.Max(lifetime, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }
            var arc = GetComponent<TeslaArc>(); if (arc != null) { lifetime = Mathf.Max(lifetime, arc.lifetime); if (arc.lifetime > 0) looping = false; }
            if (lifetime <= 0) lifetime = 2;
        }
        public void Begin() { age = 0; InPool = false; foreach (var t in Trails) t.Clear(); }
        void Update()
        {
            if (looping) return; // owner releases
            age += Time.deltaTime;
            if (age >= lifetime) StylizedVfx.Release(gameObject);
        }
    }
}
