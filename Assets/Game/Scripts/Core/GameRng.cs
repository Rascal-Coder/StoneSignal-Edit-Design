namespace StoneSignal
{
    // Deterministic, independently seeded random streams. Gameplay outcomes (crits), deck draws and reward offers each own a
    // stream, so purely cosmetic VFX randomness (shake, arcs, damage-number drift) can never shift a run's results.
    // Probabilities are integer budgets in permille (0..1000) to avoid float drift across platforms.
    public sealed class RngStream
    {
        private ulong state;
        public RngStream(int seed) { Reseed(seed); }
        public void Reseed(int seed) { state = 0x9E3779B97F4A7C15UL ^ (ulong)(uint)seed * 0xBF58476D1CE4E5B9UL; if (state == 0) state = 1; Next(); }
        // xorshift64*
        public ulong Next() { state ^= state >> 12; state ^= state << 25; state ^= state >> 27; return state * 0x2545F4914F6CDD1DUL; }
        public int Range(int minInclusive, int maxExclusive) => maxExclusive <= minInclusive ? minInclusive : minInclusive + (int)(Next() % (ulong)(maxExclusive - minInclusive));
        public bool Permille(int chance) => chance > 0 && Range(0, 1000) < chance;
        public float Value() => (Next() >> 40) / (float)(1 << 24); // [0,1) - VFX only
        public float Range(float min, float max) => min + (max - min) * Value();
    }
    public static class GameRng
    {
        public static int Seed { get; private set; } = 37;
        public static readonly RngStream Gameplay = new RngStream(37), Draw = new RngStream(37 * 31 + 1), Rewards = new RngStream(37 * 31 + 2), Vfx = new RngStream(37 * 31 + 3);
        public static void SetSeed(int seed)
        {
            Seed = seed; Gameplay.Reseed(seed); Draw.Reseed(seed * 31 + 1); Rewards.Reseed(seed * 31 + 2); Vfx.Reseed(seed * 31 + 3);
        }
        public static int ToPermille(float probability) => UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(probability * 1000f), 0, 1000);
    }
}
