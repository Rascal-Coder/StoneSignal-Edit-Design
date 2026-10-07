using UnityEngine;

namespace StoneSignal.VFX
{
    /// Reward look/feel config (gold, shards, gems...). Create via Assets > Create > StoneSignal > Reward Type.
    [CreateAssetMenu(menuName = "StoneSignal/Reward Type", fileName = "RT_Gold")]
    public class RewardType : ScriptableObject
    {
        public string id = "gold";
        public Texture2D icon;                       // world billboard + UI icon (hand-painted sprite)
        public Material material;                    // particle material using icon (built by StylizedFxV14)
        public Color tint = Color.white, sparkle = new Color(1, .88f, .45f), trailHead = new Color(1, .9f, .5f), trailTail = new Color(1, .6f, .1f, 0);
        public float size = .42f;
        [Tooltip("Value per icon thresholds: <= a -> 1 icon, <= b -> 2, else 3")] public int twoAt = 6, threeAt = 16;
        public string sfxPop = "reward_pop", sfxArrive = "reward_arrive";
    }

}
