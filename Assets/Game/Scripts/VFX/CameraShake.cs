using UnityEngine;

namespace StoneSignal.VFX
{
    /// Additive positional/rotational camera shake (Perlin, trauma-style falloff). Put on the camera (or let Shake() add it to Camera.main).
    /// CameraShake.Shake(0.15f, 0.25f); or CameraShake.Shake(CameraShake.Preset.Heavy);
    /// Uses unscaled time so it keeps playing during HitStop.
    public class CameraShake : MonoBehaviour
    {
        public enum Preset { Light, Medium, Heavy, Boss }
        public static CameraShake Instance { get; private set; }
        public float rotationScale = 1.5f;   // degrees per unit amplitude
        public float frequency = 22f;

        float _amp, _dur, _t; Vector3 _lastPos; Quaternion _lastRot = Quaternion.identity; float _seed;

        void OnEnable() { Instance = this; _seed = StoneSignal.GameRng.Vfx.Value() * 100f; }
        void OnDisable() { if (Instance == this) Instance = null; Remove(); }

        public static void Shake(Preset p)
        {
            switch (p)
            {
                case Preset.Light: Shake(0.05f, 0.12f); break;   // gatling / small hits
                case Preset.Medium: Shake(0.12f, 0.22f); break;  // cannon, enemy death
                case Preset.Heavy: Shake(0.25f, 0.35f); break;   // mortar / HE explosion
                default: Shake(0.45f, 0.6f); break;              // boss death / core hit
            }
        }

        /// amplitude in metres (camera local), duration in seconds. Stronger shakes override weaker ones.
        public static void Shake(float amplitude, float duration)
        {
            var s = Instance;
            if (!s && Camera.main) { s = Camera.main.GetComponent<CameraShake>(); if (!s) s = Camera.main.gameObject.AddComponent<CameraShake>(); }
            if (!s) return;
            float remaining = s._dur > 0 ? s._amp * (1 - s._t / s._dur) : 0;
            if (amplitude >= remaining) { s._amp = amplitude; s._dur = Mathf.Max(0.01f, duration); s._t = 0; }
        }

        void Remove() { transform.localPosition -= _lastPos; transform.localRotation *= Quaternion.Inverse(_lastRot); _lastPos = Vector3.zero; _lastRot = Quaternion.identity; }

        void LateUpdate()
        {
            Remove();
            if (_dur <= 0) return;
            _t += Time.unscaledDeltaTime;
            if (_t >= _dur) { _dur = 0; return; }
            float k = 1 - _t / _dur; float a = _amp * k * k;
            float tt = Time.unscaledTime * frequency + _seed;
            Vector3 n = new Vector3(Mathf.PerlinNoise(tt, 0) - .5f, Mathf.PerlinNoise(0, tt) - .5f, Mathf.PerlinNoise(tt, tt) - .5f) * 2f;
            _lastPos = n * a; _lastRot = Quaternion.Euler(n.y * a * rotationScale * 10f, n.x * a * rotationScale * 10f, n.z * a * rotationScale * 10f);
            transform.localPosition += _lastPos; transform.localRotation *= _lastRot;
        }
    }
}
