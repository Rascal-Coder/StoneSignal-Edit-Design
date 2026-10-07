using System;

namespace StoneSignal
{
    /// Rewarded-video ad provider. Show() pauses the game through TimeController while the ad runs and reports
    /// completion (true = reward granted). Swap AdServices.Current for a real SDK (WeChat rewarded video, etc.).
    public interface IAdService
    {
        bool IsReady { get; }
        void ShowRewarded(string placement, Action<bool> completed);
    }
    /// Placeholder: succeeds immediately on click (the TimeController ad pause is still applied and released).
    public sealed class PlaceholderAdService : IAdService
    {
        public bool IsReady => true;
        public void ShowRewarded(string placement, Action<bool> completed)
        {
            TimeController.SetAdPaused(true);
            try { completed?.Invoke(true); } finally { TimeController.SetAdPaused(false); }
        }
    }
    public static class AdServices { public static IAdService Current = new PlaceholderAdService(); }
}
