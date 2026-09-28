namespace OsuAimAnalyzer;

public static class ScoreUtils
{
    public static double Accuracy(int count300, int count100, int count50, int misses)
    {
        int total = count300 + count100 + count50 + misses;
        if (total <= 0) return 0;
        return 100.0 * (count300 * 300.0 + count100 * 100.0 + count50 * 50.0) / (300.0 * total);
    }

    // Legacy osu!standard grade rules used by stable replay data.
    // Hidden/Flashlight silver variants intentionally collapse into SS/S because the analyzer
    // cares about the performance grade, not the badge color.
    public static string Grade(int count300, int count100, int count50, int misses)
    {
        int total = count300 + count100 + count50 + misses;
        if (total <= 0) return "D";
        if (count300 == total) return "SS";

        double p300 = 100.0 * count300 / total;
        double p50 = 100.0 * count50 / total;
        if (p300 > 90.0 && p50 < 1.0 && misses == 0) return "S";
        if ((p300 > 80.0 && misses == 0) || p300 > 90.0) return "A";
        if ((p300 > 70.0 && misses == 0) || p300 > 80.0) return "B";
        if (p300 > 60.0) return "C";
        return "D";
    }
}
