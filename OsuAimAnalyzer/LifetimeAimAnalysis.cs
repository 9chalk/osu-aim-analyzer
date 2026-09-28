namespace OsuAimAnalyzer;

public sealed class LifetimeAimSample
{
    public PlayRow Play { get; init; } = new();
    public double MeanCenterError { get; init; }
    public double MedianCenterError { get; init; }
    public double P90CenterError { get; init; }
    public double OveraimPercent { get; init; }
    public double UnderaimPercent { get; init; }
    public double LateralPercent { get; init; }
    public double CorrectionPercent { get; init; }
    public double PlainErrorPercent { get; init; }
    public double CleanPercent { get; init; }
    public int ErrorSamples { get; init; }
    public Dictionary<string, double> CausePercents { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int CauseSamples { get; init; }
}

public sealed class LifetimeAimAnalysisData
{
    public List<LifetimeAimSample> Samples { get; init; } = new();
    public List<TransitionMetric> Transitions { get; init; } = new();
    public int TransitionCount { get; init; }

    public double AverageCenterError => WeightedAverage(x => x.MeanCenterError, x => x.ErrorSamples);
    public double AverageProficiency => Samples.Count == 0 ? 0 : Samples.Average(x => x.Play.Proficiency);
    public double AverageAimPerformance => Samples.Count == 0 ? 0 : Samples.Average(x => x.Play.RawAimRating);


    public string DominantCause
    {
        get
        {
            var summary = AimErrorDiagnostics.Analyze(Transitions);
            return summary.PrimaryCause == AimErrorDiagnostics.Clean
                ? "Clean"
                : $"{summary.PrimaryCause}\n{summary.PrimaryShare:0}%";
        }
    }

    public string DominantError
    {
        get
        {
            if (Samples.Count == 0) return "—";
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Overaim"] = WeightedAverage(x => x.OveraimPercent, x => x.ErrorSamples),
                ["Underaim"] = WeightedAverage(x => x.UnderaimPercent, x => x.ErrorSamples),
                ["Lateral"] = WeightedAverage(x => x.LateralPercent, x => x.ErrorSamples),
                ["Correction"] = WeightedAverage(x => x.CorrectionPercent, x => x.ErrorSamples),
                ["Plain error"] = WeightedAverage(x => x.PlainErrorPercent, x => x.ErrorSamples)
            };
            var best = totals.MaxBy(x => x.Value);
            return best.Value > 0 ? $"{best.Key}\n{best.Value:0}%" : "Clean";
        }
    }

    private double WeightedAverage(Func<LifetimeAimSample, double> value, Func<LifetimeAimSample, int> weight)
    {
        double sum = 0, weights = 0;
        foreach (var s in Samples)
        {
            int w = Math.Max(0, weight(s));
            if (w == 0) continue;
            sum += value(s) * w;
            weights += w;
        }
        return weights > 0 ? sum / weights : 0;
    }
}

public static class LifetimeAimAnalysisBuilder
{
    public static LifetimeAimAnalysisData Build(AnalyzerDatabase database, IReadOnlyList<PlayRow> plays)
    {
        if (plays.Count == 0) return new LifetimeAimAnalysisData();

        var transitions = database.LoadTransitions(plays.Select(p => p.Id));
        var byPlay = transitions.GroupBy(t => t.PlayId).ToDictionary(g => g.Key, g => g.ToList());
        var samples = new List<LifetimeAimSample>(plays.Count);

        foreach (var play in plays.OrderBy(p => p.TimestampUtc))
        {
            var rows = byPlay.GetValueOrDefault(play.Id) ?? new List<TransitionMetric>();
            var valid = rows.Where(t => double.IsFinite(t.AxialError) && double.IsFinite(t.LateralError)).ToList();
            var radial = valid.Select(t => Math.Sqrt(t.AxialError * t.AxialError + t.LateralError * t.LateralError))
                              .Where(double.IsFinite).OrderBy(x => x).ToArray();
            int n = Math.Max(1, valid.Count);
            double Pct(string name) => valid.Count(t => string.Equals(t.ErrorClass, name, StringComparison.OrdinalIgnoreCase)) * 100.0 / n;
            var causeSummary = AimErrorDiagnostics.Analyze(rows);
            int causeN = Math.Max(1, rows.Count);
            var causePercents = AimErrorDiagnostics.OrderedCauses.ToDictionary(
                c => c,
                c => 100.0 * causeSummary.CauseCounts.GetValueOrDefault(c) / causeN,
                StringComparer.OrdinalIgnoreCase);

            samples.Add(new LifetimeAimSample
            {
                Play = play,
                MeanCenterError = radial.Length > 0 ? radial.Average() : 0,
                MedianCenterError = Percentile(radial, .50),
                P90CenterError = Percentile(radial, .90),
                OveraimPercent = Pct("Overaim"),
                UnderaimPercent = Pct("Underaim"),
                LateralPercent = Pct("Lateral"),
                CorrectionPercent = Pct("Correction"),
                PlainErrorPercent = Pct("Plain error"),
                CleanPercent = Pct("Clean"),
                ErrorSamples = valid.Count,
                CausePercents = causePercents,
                CauseSamples = rows.Count
            });
        }

        return new LifetimeAimAnalysisData
        {
            Samples = samples,
            Transitions = transitions,
            TransitionCount = transitions.Count
        };
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double pos = (sorted.Count - 1) * Math.Clamp(p, 0, 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        double f = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * f;
    }
}
