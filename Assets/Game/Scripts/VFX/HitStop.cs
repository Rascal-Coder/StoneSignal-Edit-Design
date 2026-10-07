using System.Collections;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Brief global time freeze for impact weight. HitStop.Trigger(0.06f) (crit), HitStop.Trigger(0.12f, 0f) (boss kill).
    /// Overlapping requests extend to the latest end time; restores the timeScale that was active before the first request.
    /// Only touches Time.timeScale - if gameplay owns timeScale (pause/fast-forward), call HitStop.Cancel() before changing it.
    public class HitStop : MonoBehaviour
    {
        static HitStop _runner; static float _endUnscaled, _restoreScale = 1f; static bool _active;
        public static bool Active => _active;
        public static float MaxDuration = 0.25f;

        public static void Trigger(float duration = 0.05f, float timeScale = 0.02f)
        {
            if (!Application.isPlaying || duration <= 0) return;
            if (_runner == null)
            {
                var go = new GameObject("[HitStop]") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go); _runner = go.AddComponent<HitStop>();
            }
            if (!_active) { _restoreScale = Time.timeScale; _active = true; _runner.StartCoroutine(_runner.Run()); }
            _endUnscaled = Mathf.Max(_endUnscaled, Time.unscaledTime + Mathf.Min(duration, MaxDuration));
            Time.timeScale = Mathf.Min(Time.timeScale, timeScale);
        }

        public static void Cancel() { if (!_active) return; _active = false; Time.timeScale = _restoreScale; }

        IEnumerator Run()
        {
            while (_active && Time.unscaledTime < _endUnscaled) yield return null;
            if (_active) { _active = false; Time.timeScale = _restoreScale; }
        }
    }
}
