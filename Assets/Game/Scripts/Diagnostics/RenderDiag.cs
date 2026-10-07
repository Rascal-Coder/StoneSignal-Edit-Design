using UnityEngine;

namespace StoneSignal
{
    // Diagnostics log: instanced board batches (tiles, walls) and spawn portal visibility.
    public static class RenderDiag
    {
        static string TexName(Material m)
        {
            foreach (var p in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(p)) { var t = m.GetTexture(p); return t ? t.name : "-"; }
            return "(no main tex property)";
        }
        public static void Log(string tag)
        {
            var sb = new System.Text.StringBuilder("RENDER DIAG [" + tag + "]");
            foreach (var b in InstancedBatch.All) if (b != null) sb.Append("\n").Append(b.Describe());
            foreach (var p in Object.FindObjectsOfType<StoneSignal.VFX.SpawnPortal>(true))
            {
                int r = 0, re = 0, ps = 0, psPlaying = 0;
                foreach (var x in p.GetComponentsInChildren<Renderer>(true)) { r++; if (x.enabled && x.gameObject.activeInHierarchy) re++; }
                foreach (var x in p.GetComponentsInChildren<ParticleSystem>(true)) { ps++; if (x.isPlaying) psPlaying++; }
                sb.Append("\nPORTAL ").Append(p.name).Append(" active=").Append(p.gameObject.activeInHierarchy).Append(" pos=").Append(p.transform.position.ToString("F2"))
                  .Append(" scale=").Append(p.transform.lossyScale.ToString("F2")).Append(" renderers=").Append(re).Append("/").Append(r)
                  .Append(" particles playing=").Append(psPlaying).Append("/").Append(ps);
                foreach (var x in p.GetComponentsInChildren<Renderer>(true))
                    sb.Append("\n    ").Append(x.name).Append(" ").Append(x.GetType().Name).Append(" enabled=").Append(x.enabled && x.gameObject.activeInHierarchy)
                      .Append(" mat=").Append(x.sharedMaterial ? x.sharedMaterial.name + " q" + x.sharedMaterial.renderQueue + " shader=" + x.sharedMaterial.shader.name + " tex=" + TexName(x.sharedMaterial) : "-")
                      .Append(" bounds=").Append(x.bounds.size.ToString("F2")).Append(" lossyScale=").Append(x.transform.lossyScale.ToString("F2"))
                      .Append(x is ParticleSystemRenderer ? " alive=" + x.GetComponent<ParticleSystem>().particleCount : "").Append(" y=").Append(x.bounds.center.y.ToString("F2"));
                // every other renderer whose bounds reach into the decal disc (r 1.3) above the island
                foreach (var x in Object.FindObjectsOfType<Renderer>())
                {
                    if (!x.enabled || x.transform.IsChildOf(p.transform)) continue; var b = x.bounds; var c = p.transform.position;
                    var q = new Vector3(Mathf.Clamp(c.x, b.min.x, b.max.x), c.y, Mathf.Clamp(c.z, b.min.z, b.max.z));
                    if ((q - c).sqrMagnitude > 1.69f || b.max.y < c.y - .3f || b.min.y > c.y + 2f) continue;
                    sb.Append("\n    near: ").Append(x.name).Append(" mat=").Append(x.sharedMaterial ? x.sharedMaterial.name + " q" + x.sharedMaterial.renderQueue : "-").Append(" bounds=").Append(b.size.ToString("F1"));
                }
            }
            Debug.Log(sb.ToString());
        }
    }
}
