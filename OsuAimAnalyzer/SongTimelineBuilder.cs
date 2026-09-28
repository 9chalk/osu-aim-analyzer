namespace OsuAimAnalyzer;

public sealed record SongTimelinePoint(
    double Seconds,
    double Proficiency,
    double AimPerformance,
    double Difficulty,
    int ObjectIndex,
    double Bpm,
    double Spacing,
    string ErrorClass);

/// <summary>
/// Builds aligned local curves for one replay.  The curves intentionally use a short centered
/// transition window so they describe what the player/map was doing at that point in the song,
/// rather than just re-plotting noisy per-object values.
/// </summary>
public static class SongTimelineBuilder
{
    private const int WindowRadius = 8;
    private const double WindowTimeRadiusMs = 3200.0;

    public static List<SongTimelinePoint> Build(PlayRow play, IReadOnlyList<TransitionMetric> source)
    {
        var transitions = source.OrderBy(t => t.TimeMs).ThenBy(t => t.ObjectIndex).ToList();
        if (transitions.Count == 0) return new List<SongTimelinePoint>();

        var c = ProductionScoring.Profile;
        double clockRate = Math.Max(.01, ModUtils.ClockRate(play.Mods));

        // Reconstruct the whole-play performance once.  We use its ratio between the final
        // SR-anchored score and the telemetry-only capability as a constant calibration factor.
        // Local shape still comes entirely from local geometry + local execution, while its units
        // remain comparable to the play's displayed Aim Performance number.
        var fullAnalysis = MakeAnalysis(play, transitions, play.MissCount, play.StarRating);
        var fullScores = transitions.Select(t => TuningScorer.Transition(t, c).Score).ToArray();
        var fullWeights = TuningScorer.DifficultyWeights(transitions, c);
        double fullRaw = TuningScorer.AggregateRaw(fullScores, fullWeights, c);
        var fullPerf = TuningScorer.Performance(fullAnalysis, fullRaw, c);
        double fullTelemetryDemand = Math.Pow(Math.Max(.05, fullPerf.BaseDemandIndex), Math.Clamp(c.PerformanceDemandCurve, .25, 4.0));
        double fullTelemetryCapability = c.PerformanceScale * fullTelemetryDemand * fullPerf.ExecutionMultiplier * fullPerf.ConsistencyMultiplier;
        double performanceCalibration = fullTelemetryCapability > 1 && play.RawAimRating > 0
            ? Math.Clamp(play.RawAimRating / fullTelemetryCapability, .25, 5.0)
            : 1.0;

        var points = new List<SongTimelinePoint>(transitions.Count);
        for (int i = 0; i < transitions.Count; i++)
        {
            long center = transitions[i].TimeMs;
            int lo = Math.Max(0, i - WindowRadius);
            int hi = Math.Min(transitions.Count - 1, i + WindowRadius);
            while (lo < i && (center - transitions[lo].TimeMs) / clockRate > WindowTimeRadiusMs) lo++;
            while (hi > i && (transitions[hi].TimeMs - center) / clockRate > WindowTimeRadiusMs) hi--;

            var window = transitions.GetRange(lo, hi - lo + 1);
            var localScores = window.Select(t => TuningScorer.Transition(t, c).Score).ToArray();
            var localWeights = TuningScorer.DifficultyWeights(window, c);
            double raw = TuningScorer.AggregateRaw(localScores, localWeights, c);
            double prof = TuningScorer.RemapProficiency(raw, c);

            // Ask the production performance model for local execution and telemetry demand,
            // but deliberately omit the whole-map SR floor here.  A constant calibration factor
            // above carries the final play's SR anchoring into the curve without flattening all
            // local difficulty variation to one star-rating floor.
            var localAnalysis = MakeAnalysis(play, window, 0, 0);
            var perf = TuningScorer.Performance(localAnalysis, raw, c);
            double telemetryDemand = Math.Pow(Math.Max(.05, perf.BaseDemandIndex), Math.Clamp(c.PerformanceDemandCurve, .25, 4.0));
            double localPerformance = c.PerformanceScale * telemetryDemand * perf.ExecutionMultiplier
                                    * fullPerf.ConsistencyMultiplier * performanceCalibration;

            var t = transitions[i];
            points.Add(new SongTimelinePoint(
                t.TimeMs / (1000.0 * clockRate),
                prof,
                Math.Max(0, localPerformance),
                telemetryDemand,
                t.ObjectIndex,
                t.Bpm,
                t.NormalizedSpacing,
                t.ErrorClass));
        }

        return Downsample(points, 700);
    }

    private static PlayAnalysis MakeAnalysis(PlayRow play, IReadOnlyList<TransitionMetric> transitions, int misses, double starRating)
    {
        var list = transitions.ToList();
        return new PlayAnalysis
        {
            Replay = new ReplayData { CountMiss = (ushort)Math.Clamp(misses, 0, ushort.MaxValue), Mods = play.Mods },
            StarRating = starRating,
            EffectiveAr = play.EffectiveAr,
            MeanBpm = list.Count > 0 ? list.Average(t => t.Bpm) : play.MeanBpm,
            SpacingP75 = Percentile(list.Select(t => t.NormalizedSpacing), .75, play.SpacingP75),
            DensityP90 = Percentile(list.Select(t => t.Density), .90, play.DensityP90),
            Transitions = list,
            TransitionCount = list.Count
        };
    }

    private static double Percentile(IEnumerable<double> values, double p, double fallback)
    {
        var a = values.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (a.Length == 0) return fallback;
        double pos = (a.Length - 1) * Math.Clamp(p, 0, 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return a[lo];
        double f = pos - lo;
        return a[lo] + (a[hi] - a[lo]) * f;
    }

    private static List<SongTimelinePoint> Downsample(List<SongTimelinePoint> input, int maxPoints)
    {
        if (input.Count <= maxPoints) return input;

        // Preserve the rough envelope rather than blindly taking every Nth point.  Each bucket
        // keeps its first point plus local proficiency/difficulty extrema; this keeps breakdowns
        // and difficulty spikes visible even on marathon maps.
        int bucket = Math.Max(2, (int)Math.Ceiling(input.Count / (double)(maxPoints / 3)));
        var output = new List<SongTimelinePoint>(maxPoints + 8);
        for (int start = 0; start < input.Count; start += bucket)
        {
            var slice = input.Skip(start).Take(Math.Min(bucket, input.Count - start)).ToList();
            foreach (var point in new[]
            {
                slice[0],
                slice.MinBy(x => x.Proficiency)!,
                slice.MaxBy(x => x.Difficulty)!
            }.Distinct().OrderBy(x => x.Seconds))
                output.Add(point);
        }
        if (output.Count == 0 || output[^1] != input[^1]) output.Add(input[^1]);
        return output.OrderBy(x => x.Seconds).ToList();
    }
}
