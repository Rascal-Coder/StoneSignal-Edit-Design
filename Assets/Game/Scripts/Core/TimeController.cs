using UnityEngine;

namespace StoneSignal
{
    /// The single owner of Time.timeScale. Every system requests a layer here instead of writing timeScale:
    ///   speed (x1/x2/x3, smoke-test fast-forward), user pause, ad pause, hit-stop (short freeze, unscaled-time expiry).
    /// timeScale = paused || adPaused ? 0 : (hitStopActive ? min(speed, hitStopScale) : speed)
    /// Nothing is "restored" from a captured value, so no layer can leak another layer's scale (root cause of the old
    /// HitStop bug: its coroutine ran its first step synchronously inside StartCoroutine, before the end time was set,
    /// so it ended at once and the 0.05 written right after it was orphaned and then captured as the next "restore" value).
    public sealed class TimeController : MonoBehaviour
    {
        static TimeController runner;
        public static float Speed { get; private set; } = 1;
        public static bool Paused { get; private set; }
        public static bool AdPaused { get; private set; }
        public static bool HitStopActive => hitStopEnd > Time.unscaledTime;
        public static float MaxHitStop = .25f;
        static float hitStopEnd, hitStopStart, hitStopScale = 1;

        public static void SetSpeed(float speed) { Speed = Mathf.Max(0, speed); Apply(); }
        public static void SetPaused(bool paused) { Paused = paused; Apply(); }
        public static void SetAdPaused(bool paused) { AdPaused = paused; Apply(); }
        public static void HitStop(float duration, float scale)
        {
            if (!Application.isPlaying || duration <= 0) return;
            Ensure();
            float now = Time.unscaledTime;
            if (!HitStopActive) { hitStopStart = now; hitStopScale = scale; } else hitStopScale = Mathf.Min(hitStopScale, scale);
            // chained requests never hold the freeze longer than MaxHitStop in total
            hitStopEnd = Mathf.Min(Mathf.Max(hitStopEnd, now + Mathf.Min(duration, MaxHitStop)), hitStopStart + MaxHitStop);
            Apply();
        }
        public static void CancelHitStop() { hitStopEnd = 0; Apply(); }
        /// Back to x1, unpaused, no freeze (scene restart, tests).
        public static void ResetAll() { Speed = 1; Paused = AdPaused = false; hitStopEnd = 0; Apply(); }
        public static float Evaluate(float speed, bool paused, bool adPaused, bool hitStop, float hitScale)
            => paused || adPaused ? 0 : hitStop ? Mathf.Min(speed, hitScale) : speed;
        static void Apply() { Time.timeScale = Evaluate(Speed, Paused, AdPaused, HitStopActive, hitStopScale); }
        static void Ensure()
        {
            if (runner != null) return;
            var go = new GameObject("[TimeController]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go); runner = go.AddComponent<TimeController>();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { Speed = 1; Paused = AdPaused = false; hitStopEnd = 0; Ensure(); Apply(); }
        // expiry is evaluated every frame from unscaled time; re-applying also overrides any stray direct write
        void Update() { Apply(); }
    }
}
