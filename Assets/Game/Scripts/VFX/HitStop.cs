using System.Collections;
using UnityEngine;

namespace StoneSignal.VFX
{
    /// Brief global time freeze for impact weight. HitStop.Trigger(0.06f) (crit), HitStop.Trigger(0.12f, 0f) (boss kill).
    /// Overlapping requests extend to the latest end time; restores the timeScale that was active before the first request.
    /// Only touches Time.timeScale - if gameplay owns timeScale (pause/fast-forward), call HitStop.Cancel() before changing it.
    public class HitStop : MonoBehaviour
    {
        static HitStop _runner; static float _endUnscaled, _startUnscaled, _restoreScale = 1f; static bool _active;
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
            if (!_active) { _restoreScale = Time.timeScale; _startUnscaled = Time.unscaledTime; _active = true; _runner.StartCoroutine(_runner.Run()); }
            // chained requests can never hold the freeze longer than MaxDuration in total
            _endUnscaled = Mathf.Min(Mathf.Max(_endUnscaled, Time.unscaledTime + Mathf.Min(duration, MaxDuration)), _startUnscaled + MaxDuration);
            Time.timeScale = Mathf.Min(Time.timeScale, timeScale); _frozen = Time.timeScale;
        }

        // Watchdog (regression guard for the smoke-test "enemy never dies" hang): a freeze left behind with no active hit-stop
        // (timeScale still equal to the value we applied) is restored to the last gameplay timeScale.
        static float _frozen = -1f, _lastNormal = 1f;
        void LateUpdate()
        {
            if (_active) return;
            if (_frozen >= 0 && Mathf.Approximately(Time.timeScale, _frozen) && _frozen < _lastNormal)
            { Debug.LogWarning("HITSTOP WATCHDOG: orphaned timeScale " + _frozen + " restored to " + _lastNormal); Time.timeScale = _lastNormal; }
            _frozen = -1f; _lastNormal = Time.timeScale;
        }
        public static void Cancel() { if (!_active) return; _active = false; Time.timeScale = _restoreScale; }

        IEnumerator Run()
        {
            while (_active && Time.unscaledTime < _endUnscaled) yield return null;
            if (_active) { _active = false; Time.timeScale = _restoreScale; }
        }
    }
}
