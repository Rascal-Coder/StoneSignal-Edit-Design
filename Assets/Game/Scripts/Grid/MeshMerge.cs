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
        void LateUpdate()
        {
            foreach (var g in groups)
                foreach (var chunk in g.chunks)
                    Graphics.DrawMeshInstanced(g.mesh, g.sub, g.mat, chunk, chunk.Length, null, shadows, true, gameObject.layer);
        }
    }
}
