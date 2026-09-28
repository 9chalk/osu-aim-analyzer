namespace OsuAimAnalyzer;

public static class AimRatingUtils
{
    // Keep the pp-like "one best score per beatmap difficulty" behavior, but normalize
    // the profile number back onto the same scale as an individual play rating.
    // The profile Aim Rating is simply the arithmetic mean of the best 100 unique-map
    // aim performances (or however many unique maps are available when there are <100).
    public static double Top100Average(IEnumerable<PlayRow> plays)
    {
        var ratings = BestUniqueMapRatings(plays).Take(ScoringConfig.AimRatingTopPlayCount).ToArray();
        return ratings.Length == 0 ? 0 : ratings.Average();
    }

    public static IReadOnlyList<(int Rank, double Rating)> Breakdown(IEnumerable<PlayRow> plays)
    {
        var ratings = BestUniqueMapRatings(plays).Take(ScoringConfig.AimRatingTopPlayCount).ToArray();
        return ratings.Select((rating, i) => (i + 1, rating)).ToArray();
    }

    public static int ContributingMapCount(IEnumerable<PlayRow> plays)
        => BestUniqueMapRatings(plays).Take(ScoringConfig.AimRatingTopPlayCount).Count();

    private static IEnumerable<double> BestUniqueMapRatings(IEnumerable<PlayRow> plays)
        => plays.Where(p => double.IsFinite(p.RawAimRating) && p.RawAimRating > 0)
            .GroupBy(p => string.IsNullOrWhiteSpace(p.BeatmapHash) ? p.Map : p.BeatmapHash, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Max(p => p.RawAimRating))
            .OrderByDescending(x => x);
}
