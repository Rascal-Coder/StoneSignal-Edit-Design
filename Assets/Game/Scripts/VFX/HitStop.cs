using UnityEngine;

namespace StoneSignal.VFX
{
    /// Brief global time freeze for impact weight. HitStop.Trigger(0.06f) (crit), HitStop.Trigger(0.12f, 0f) (boss kill).
    /// Thin facade: TimeController owns Time.timeScale (speed, pause, ads, hit-stop), so a freeze can never outlive its window.
    public static class HitStop
    {
        public static bool Active => StoneSignal.TimeController.HitStopActive;
        public static float MaxDuration { get => StoneSignal.TimeController.MaxHitStop; set => StoneSignal.TimeController.MaxHitStop = value; }
        public static void Trigger(float duration = 0.05f, float timeScale = 0.02f) => StoneSignal.TimeController.HitStop(duration, timeScale);
        public static void Cancel() => StoneSignal.TimeController.CancelHitStop();
    }
}
