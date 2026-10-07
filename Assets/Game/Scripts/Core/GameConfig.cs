using UnityEngine;

namespace StoneSignal
{
    [CreateAssetMenu(menuName = "StoneSignal/Game Configuration")]
    public sealed class GameConfig : ScriptableObject
    {
        [Min(1)] public int baseHP = 30;
        [Min(0)] public int initialGold = 200;
        [Min(1)] public int blocksPerBuild = 3;
        public bool allowCombatBlocks = true;
        public BlockShapeData[] blocks;
        public TowerData[] towers;
        public WaveData[] waves;
        public RewardData[] rewards;
        [Tooltip("Rune tuning + drawn-block rune table (null = RuneConfig defaults).")]
        public RuneConfig runes;
        public VisualPalette palette;
        [Tooltip("Board size, spawn points and core footprint for the game scene (null = legacy grid fields).")]
        public BoardLayoutData layout;
    }
}
