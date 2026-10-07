using System;
namespace StoneSignal
{
    /// v16.2 integration gate for step-by-step art comparison shots: "-artstep N" enables steps 1..N
    /// (1 footprints, 2 spawn portal, 3 water ripple, 4 core enclosure, 5 reward pick UI). Default: all.
    public static class ArtSteps
    {
        static int max = -1;
        public static int Max { get { if (max < 0) { max = 99; var a = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-artstep" && int.TryParse(a[i + 1], out int n)) max = n; } return max; } }
        public static bool On(int step) => Max >= step;
    }
}
