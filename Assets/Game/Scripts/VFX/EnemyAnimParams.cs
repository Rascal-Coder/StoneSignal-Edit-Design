namespace StoneSignal.VFX
{
    /// Animator parameter / state names used by the stylized enemy controllers (AC_Enemy_*.controller).
    /// Die trigger, Locomotion/Death states and SS_Move/SS_Death clips come from StoneSignal.EnemyVisualContract.
    public static class EnemyAnimParams
    {
        public const string MoveSpeed = "MoveSpeed"; // float, default 1, multiplies SS_Move playback speed
        public const string Hit = "Hit";             // trigger -> Hit state (SS_Hit), returns to Locomotion
        public const string HitState = "Hit";
        public const string HitClip = "SS_Hit";
    }
}
