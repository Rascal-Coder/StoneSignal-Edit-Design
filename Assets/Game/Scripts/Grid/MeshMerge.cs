using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StoneSignal
{
    // Draw-call reduction for static board pieces (tiles, placed walls). In play mode every MeshRenderer under the root is
    // replaced by GPU-instanced draws grouped by (mesh, sub-mesh, material) - works with the art pipeline's non-readable meshes.
    // Edit mode (screenshot tool) keeps plain renderers. Re-run Rebuild after the set of pieces changes.
    public static class MeshMerge
    {
        public static int Rebuild(Transform root, string batchName, ShadowCastingMode shadows)
        {
            if (!Application.isPlaying) return 0;
            var batch = root.GetComponent<InstancedBatch>(); if (batch == null) batch = root.gameObject.AddComponent<InstancedBatch>();
            return batch.Rebuild(shadows);
        }
    }
    public sealed class InstancedBatch : MonoBehaviour
    {
        sealed class Group { public Mesh mesh; public int sub; public Material mat; public readonly List<Matrix4x4> m = new List<Matrix4x4>(); public Matrix4x4[][] chunks; }
        readonly List<Group> groups = new List<Group>();
        readonly List<MeshRenderer> fallback = new List<MeshRenderer>();
        ShadowCastingMode shadows;
        public int Groups => groups.Count;
        public int Rebuild(ShadowCastingMode castShadows)
        {
            shadows = castShadows; groups.Clear();
            var map = new Dictionary<(Mesh, int, Material), Group>();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>(); var mesh = mf.sharedMesh;
                if (mr == null || mesh == null || !mf.gameObject.activeInHierarchy) continue;
                var mats = mr.sharedMaterials; bool ok = true;
                foreach (var mat in mats) if (mat == null || !mat.enableInstancing) ok = false;
                // per-renderer MaterialPropertyBlock (RuneInlay / WallHighlight) must stay a real renderer: instanced draws share one block
                if (mr.HasPropertyBlock()) ok = false;
                if (!ok) { mr.enabled = true; mr.shadowCastingMode = castShadows; continue; } // non-instanced material: stays a renderer
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    var key = (mesh, s, mats[s]);
                    if (!map.TryGetValue(key, out var g)) { g = new Group { mesh = mesh, sub = s, mat = mats[s] }; map[key] = g; groups.Add(g); }
                    g.m.Add(mf.transform.localToWorldMatrix);
                }
                mr.enabled = false;
            }
            foreach (var g in groups)
            {
                int n = (g.m.Count + 1022) / 1023; g.chunks = new Matrix4x4[n][];
                for (int c = 0; c < n; c++) g.chunks[c] = g.m.GetRange(c * 1023, Mathf.Min(1023, g.m.Count - c * 1023)).ToArray();
            }
            return groups.Count;
        }
        void LateUpdate() => Submit();
        static readonly List<InstancedBatch> all = new List<InstancedBatch>();
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);
        public static IReadOnlyList<InstancedBatch> All => all;
        /// Re-queue every batch's instanced draws for cameras rendered after this call (e.g. an Update-time SubmitRenderRequest capture,
        /// which runs before LateUpdate queues them and so would otherwise miss tiles and walls).
        public static void SubmitAll() { foreach (var b in all) if (b != null && b.isActiveAndEnabled) b.Submit(); }
        public string Describe()
        {
            var sb = new System.Text.StringBuilder(); int fb = 0;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true)) if (mr.enabled && mr.gameObject.activeInHierarchy) fb++;
            sb.Append(name).Append(": groups=").Append(groups.Count).Append(" enabledRenderers=").Append(fb);
            foreach (var g in groups)
                sb.Append("\n  mesh=").Append(g.mesh != null ? g.mesh.name : "null").Append(" sub=").Append(g.sub).Append(" n=").Append(g.m.Count)
                  .Append(" mat=").Append(g.mat != null ? g.mat.name : "null").Append(" shader=").Append(g.mat != null ? g.mat.shader.name : "-")
                  .Append(" supported=").Append(g.mat != null && g.mat.shader.isSupported).Append(" instancing=").Append(g.mat != null && g.mat.enableInstancing)
                  .Append(" keywords=[").Append(g.mat != null ? string.Join(",", g.mat.shaderKeywords) : "").Append("]");
            return sb.ToString();
        }
        public void Submit()
        {
            foreach (var g in groups)
                foreach (var chunk in g.chunks)
                    Graphics.DrawMeshInstanced(g.mesh, g.sub, g.mat, chunk, chunk.Length, null, shadows, true, gameObject.layer);
        }
    }
}
