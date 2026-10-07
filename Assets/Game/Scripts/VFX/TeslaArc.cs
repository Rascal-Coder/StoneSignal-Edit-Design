using UnityEngine;

namespace StoneSignal.VFX
{
    /// Jagged lightning arc on a LineRenderer (FX_Proj_Tesla_Arc). SetEndpoints(from, to) each frame or once; re-jitters at `rate` Hz.
    [RequireComponent(typeof(LineRenderer))]
    public class TeslaArc : MonoBehaviour
    {
        public Vector3 from, to = Vector3.forward * 3;
        public int segments = 12; public float jitter = 0.18f; public float rate = 30f; public float lifetime = 0.25f;
        LineRenderer _lr; float _next, _age;
        public void SetEndpoints(Vector3 a, Vector3 b) { from = a; to = b; Rebuild(); }
        void OnEnable() { _age = 0; _next = 0; }
        void Awake() { _lr = GetComponent<LineRenderer>(); _lr.useWorldSpace = true; Rebuild(); }
        public void Rebuild()
        {
            if (!_lr) _lr = GetComponent<LineRenderer>();
            _lr.positionCount = segments + 1;
            Vector3 d = to - from, side = Vector3.Cross(d.normalized, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            Vector3 up = Vector3.Cross(side, d.normalized);
            for (int i = 0; i <= segments; i++)
            {
                float k = i / (float)segments, env = Mathf.Sin(k * Mathf.PI);
                Vector3 off = (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * jitter * env;
                _lr.SetPosition(i, from + d * k + off);
            }
        }
        void Update()
        {
            _age += Time.deltaTime;
            if (Time.time >= _next) { _next = Time.time + 1f / rate; Rebuild(); }
            if (lifetime > 0 && _age > lifetime) { if (GetComponent<PooledVfx>()) _age = float.NegativeInfinity; else Destroy(gameObject); } // pooled: PooledVfx returns it
        }
    }
}
