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
        public static bool Explicit { get; private set; }
        /// Explicit seed (smoke test / EditMode tests / repro). Sticks: BeginRun will not replace it.
        public static void SetSeed(int seed) { Explicit = true; Apply(seed); UnityEngine.Debug.Log("RUN SEED: " + seed + " (explicit SetSeed: smoke/test) - replay with -seed " + seed); }
        private static void Apply(int seed)
        {
            Seed = seed; Gameplay.Reseed(seed); Draw.Reseed(seed * 31 + 1); Rewards.Reseed(seed * 31 + 2); Vfx.Reseed(seed * 31 + 3);
        }
        /// v18 (P0): called once at run start (GameBootstrap.Awake) before the deck/hand/rewards roll anything. Real player runs get a fresh
        /// seed every run; "-seed N" on the command line replays a logged run; batch mode (tests) and diagnostic captures
        /// (-stonesignal-*) stay on 37 so screenshots/probes are reproducible (the smoke test re-seeds itself from -seed afterwards).
        public static int BeginRun()
        {
            string src;
            if (Explicit) src = "explicit";
            else
            {
                string[] args = System.Environment.GetCommandLineArgs(); int seed = 0;
                bool diag = UnityEngine.Application.isBatchMode; foreach (var a in args) if (a.StartsWith("-stonesignal-")) diag = true;
                if (diag) { seed = 37; src = "diagnostic/test default"; }
                else
                {
                    int i = System.Array.IndexOf(args, "-seed");
                    if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out seed)) src = "command line";
                    else { seed = System.Guid.NewGuid().GetHashCode() ^ System.Environment.TickCount; src = "fresh"; }
                }
                Apply(seed);
            }
            UnityEngine.Debug.Log("RUN SEED: " + Seed + " (" + src + ") - replay with -seed " + Seed);
            return Seed;
        }
        public static int ToPermille(float probability) => UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(probability * 1000f), 0, 1000);
    }
}
