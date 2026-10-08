using UnityEngine;

namespace StoneSignal.VFX
{
    /// v17 flyer motion (visual only, gameplay position untouched). Sits on the enemy VISUAL root (child of the logical enemy):
    /// lifts the model heightOffset above the tile top, sine bob, banks (roll) into turns from the parent's yaw rate, slight nose-down when moving.
    /// Wing flap = the model's SS_Move clip (2 flaps / 0.6 s). Auto-added by EnemyGroundFx.SetFlying(true), or add it on PF_Enemy_Flyer.
    [DisallowMultipleComponent]
    public class FlyingMotion : MonoBehaviour
    {
        [Tooltip("Metres above the tile top")] public float heightOffset = 1.2f;
        public float bobAmp = .12f, bobFreq = .9f;
        [Tooltip("Max roll (deg) when turning (art v17 spec: 22 deg into a turn; the preview's -14 deg is the same rule turning the other way, gentler)")] public float bankMax = 22f;
        [Tooltip("Roll degrees per (deg/s) of yaw rate")] public float bankPerYawRate = .12f;
        public float pitchForward = 8f, smoothing = 6f;
        [Tooltip("Seconds to climb to heightOffset after (re)spawn")] public float takeOff = .5f;

        /// Art v17 spec: SS_Move = 2 wing flaps per 0.6 s. SM_Enemy_Flyer_01's SS_Move take is 18 frames at 24 fps (0.75 s), so Enemy scales the
        /// locomotion speed by clipLength / FlapLoopSeconds for flyers (x1.25) instead of re-timing the art clip.
        public const float FlapLoopSeconds = .6f;
        Vector3 baseLocal; Quaternion baseRot; float lastYaw, roll, pitch, t0, phase; Vector3 lastPos; bool init;
        public float CurrentHeight { get; private set; }
        public float Roll => roll; public float Pitch => pitch;   // diagnostics (-flyershot)

        void OnEnable()
        {
            // v17.2: never stack lifts (a 2nd FlyingMotion on an inner model child doubled the height to ~2.4 m)
            for (var p = transform.parent; p != null; p = p.parent) if (p.GetComponent<FlyingMotion>()) { enabled = false; return; }
            ResetBase();
        }
        public void ResetBase()
        {
            if (!init) { baseLocal = transform.localPosition; baseRot = transform.localRotation; init = true; }
            t0 = Time.time; phase = Random.value * 6.283f; roll = pitch = 0;
            var p = transform.parent; lastYaw = p ? p.eulerAngles.y : 0; lastPos = p ? p.position : transform.position;
        }

        void LateUpdate()
        {
            var p = transform.parent; float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float k = takeOff > 0 ? Mathf.Clamp01((Time.time - t0) / takeOff) : 1; k = 1 - (1 - k) * (1 - k);   // ease-out climb
            float bob = Mathf.Sin((Time.time * bobFreq) * 6.283f + phase) * bobAmp;
            CurrentHeight = heightOffset * k + bob * k;
            float targetRoll = 0, targetPitch = 0;
            if (p)
            {
                float yaw = p.eulerAngles.y, rate = Mathf.DeltaAngle(lastYaw, yaw) / dt; lastYaw = yaw;
                targetRoll = Mathf.Clamp(-rate * bankPerYawRate, -bankMax, bankMax);
                float speed = (p.position - lastPos).magnitude / dt; lastPos = p.position;
                targetPitch = Mathf.Clamp01(speed / 2f) * pitchForward;
            }
            float a = 1 - Mathf.Exp(-smoothing * dt);
            roll = Mathf.Lerp(roll, targetRoll, a); pitch = Mathf.Lerp(pitch, targetPitch, a);
            transform.localPosition = baseLocal + Vector3.up * CurrentHeight;
            transform.localRotation = baseRot * Quaternion.Euler(pitch, 0, roll);
        }

        void OnDisable() { if (init) { transform.localPosition = baseLocal; transform.localRotation = baseRot; } }
    }
}
