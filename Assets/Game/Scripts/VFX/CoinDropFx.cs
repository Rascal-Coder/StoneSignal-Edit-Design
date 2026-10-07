using UnityEngine;

namespace StoneSignal.VFX
{
    /// Back-compat gold API (PF_VFX_CoinDrop). Same Play(worldPos, amount, uiTarget, onArrive) signature as v14.
    public class CoinDropFx : RewardFlyFx
    {
        public static CoinDropFx Instance { get; private set; }
        protected override void Awake() { base.Awake(); if (Instance == null) Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }
    }
}
