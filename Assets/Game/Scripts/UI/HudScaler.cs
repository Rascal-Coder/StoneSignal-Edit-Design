using UnityEngine;
using UnityEngine.UI;

namespace StoneSignal
{
    /// ui_layout_v14: reference 1920x1080; match height when aspect >= 16:9, width below (tablets);
    /// resulting scale factor clamped to 0.6-1.6; children live under a SafeAreaRoot fitted to Screen.safeArea.
    public sealed class HudScaler : MonoBehaviour
    {
        CanvasScaler scaler; RectTransform safe; Rect lastSafe; Vector2Int lastSize;
        /// Diagnostics / device simulation: overrides Screen.safeArea (e.g. a landscape notch) when set.
        public static Rect? SimulatedSafeArea;
        public static Rect SafeArea => SimulatedSafeArea ?? Screen.safeArea;
        public void Init(CanvasScaler s, RectTransform safeRoot) { scaler = s; safe = safeRoot; scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; Apply(); }
        public static float ScaleFor(int w, int h)
        {
            float match = (float)w / Mathf.Max(1, h) >= 1.77f ? 1 : 0;
            float f = Mathf.Lerp(w / 1920f, h / 1080f, match);
            return Mathf.Clamp(f, .6f, 1.6f);
        }
        void Apply()
        {
            lastSize = new Vector2Int(Screen.width, Screen.height); lastSafe = SafeArea;
            scaler.scaleFactor = ScaleFor(Screen.width, Screen.height);
            var a = lastSafe; Vector2 size = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            safe.anchorMin = a.position / size; safe.anchorMax = (a.position + a.size) / size; safe.offsetMin = safe.offsetMax = Vector2.zero;
        }
        void Update() { if (Screen.width != lastSize.x || Screen.height != lastSize.y || SafeArea != lastSafe) Apply(); }
    }
}
