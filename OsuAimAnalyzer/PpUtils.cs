namespace OsuAimAnalyzer;

// Lightweight local PP estimate for dashboard/trend use.
// This is deliberately labelled "PP estimate" in the UI. It is shaped to behave like
// osu!standard pp (stars, accuracy and misses) but is not a replacement for the live
// official performance calculator. Keeping it isolated makes it easy to swap in rosu-pp/
// osu!tools later without touching the dashboard/database code.
public static class PpUtils
{
    public static double Estimate(double starRating, double accuracyPercent, int misses, int mods)
    {
        if (starRating <= 0 || accuracyPercent <= 0) return 0;
        double s = Math.Max(.05, starRating - .50);
        double baseValue = 3.0 * Math.Pow(s, 2.60);
        double acc = Math.Clamp(accuracyPercent / 100.0, 0, 1);
        double accuracyFactor = Math.Pow(acc, 5.2);
        double missFactor = Math.Pow(.94, Math.Max(0, misses));
        double modFactor = 1.0;
        if ((mods & ModUtils.Hidden) != 0) modFactor *= 1.04;
        if ((mods & ModUtils.HardRock) != 0) modFactor *= 1.03;
        if ((mods & ModUtils.Flashlight) != 0) modFactor *= 1.08;
        if ((mods & ModUtils.NoFail) != 0) modFactor *= .90;
        return Math.Max(0, baseValue * accuracyFactor * missFactor * modFactor);
    }
}
