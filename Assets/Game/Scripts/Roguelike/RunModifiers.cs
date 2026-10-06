namespace StoneSignal
{
    // Per-run values, never written back into ScriptableObject assets.
    public sealed class RunModifiers
    {
        public float AllRange = 1, EnemySpeed = 1, KillGold = 1, TowerCost = 1;
        public int ExtraDraw, NextDraw, WaveGold, WaveHeal;
        public BlockShapeData BonusSlotShape;
        public float Damage = 1;
        public float AttackSpeed = 1;
        public float ArrowRange = 1;
        public float CannonRadius = 1;
        public float NextWaveGold = 1;
        public float CurrentWaveGold = 1;
        public void BeginWave() { CurrentWaveGold = NextWaveGold; NextWaveGold = 1; }
        public float Range(TowerData data) => data.range * AllRange * (data.kind == TowerKind.Arrow ? ArrowRange : 1);
        public float Radius(TowerData data) => data.splashRadius * (data.kind == TowerKind.Cannon ? CannonRadius : 1);
    }
}
