namespace OsuAimAnalyzer;

public sealed class AimErrorContextMetric
{
    public string Metric { get; init; } = "";
    public string HotRange { get; init; } = "—";
    public double CauseRateInRange { get; init; }
    public double OverallCauseRate { get; init; }
    public double Lift { get; init; }
    public int CauseSamplesInRange { get; init; }
    public int TotalSamplesInRange { get; init; }
    public string CauseTypical { get; init; } = "—";
    public string BaselineTypical { get; init; } = "—";
}

public sealed class AimErrorContextMap
{
    public string Map { get; init; } = "";
    public string Mods { get; init; } = "NM";
    public int PlayCount { get; init; }
    public int CauseCount { get; init; }
    public int TransitionCount { get; init; }
    public double CauseRate { get; init; }
    public double Star { get; init; }
    public double Ar { get; init; }
    public double Bpm { get; init; }
    public double Spacing { get; init; }
}

public sealed class AimErrorContextResult
{
    public string Kind { get; init; } = "cause";
    public string Label { get; init; } = "";
    public string Cause { get; init; } = "";
    public int CauseCount { get; init; }
    public int TransitionCount { get; init; }
    public int AffectedMaps { get; init; }
    public double OverallCauseRate => TransitionCount <= 0 ? 0 : 100.0 * CauseCount / TransitionCount;
    public List<AimErrorContextMetric> Metrics { get; init; } = new();
    public List<AimErrorContextMap> Maps { get; init; } = new();
    public string Summary { get; init; } = "";
}

public static class AimErrorContextAnalyzer
{
    private sealed record Row(TransitionMetric Metric, PlayRow Play, string Cause, string Direction);
    private sealed record Bin(string Label, double Min, double Max);
    private sealed record MetricSpec(string Name, Func<Row, double> Value, Func<double, string> Format, IReadOnlyList<Bin> Bins);

    public static AimErrorContextResult Build(LifetimeAimAnalysisData data, string selectionKey)
    {
        bool directionMode = selectionKey.StartsWith("direction:", StringComparison.OrdinalIgnoreCase);
        string label = selectionKey.Contains(':') ? selectionKey[(selectionKey.IndexOf(':') + 1)..] : selectionKey;
        string kind = directionMode ? "direction" : "cause";

        var playById = data.Samples.Select(x => x.Play).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<Row>(data.Transitions.Count);
        foreach (var t in data.Transitions)
        {
            if (!playById.TryGetValue(t.PlayId, out var play)) continue;
            rows.Add(new Row(t, play, AimErrorDiagnostics.Diagnose(t).Cause,
                string.IsNullOrWhiteSpace(t.ErrorClass) ? "Plain error" : t.ErrorClass));
        }

        bool IsSelected(Row x) => directionMode
            ? x.Direction.Equals(label, StringComparison.OrdinalIgnoreCase)
            : x.Cause.Equals(label, StringComparison.OrdinalIgnoreCase);
        var selected = rows.Where(IsSelected).ToList();
        var specs = new[]
        {
            new MetricSpec("Star rating", r => r.Play.StarRating, v => v > 0 ? $"{v:0.00}★" : "—", new[]
            {
                new Bin("<5★", 0, 5), new Bin("5–6★", 5, 6), new Bin("6–7★", 6, 7), new Bin("7–8★", 7, 8),
                new Bin("8–9★", 8, 9), new Bin("9–10★", 9, 10), new Bin("10★+", 10, double.PositiveInfinity)
            }),
            new MetricSpec("BPM", r => r.Metric.Bpm, v => v > 0 ? $"{v:0} BPM" : "—", new[]
            {
                new Bin("<180 BPM", 0, 180), new Bin("180–220", 180, 220), new Bin("220–250", 220, 250),
                new Bin("250–280", 250, 280), new Bin("280–320", 280, 320), new Bin("320+", 320, double.PositiveInfinity)
            }),
            new MetricSpec("Approach rate", r => r.Play.EffectiveAr, v => v > 0 ? $"AR {v:0.0}" : "—", new[]
            {
                new Bin("<AR9", 0, 9), new Bin("AR9–9.5", 9, 9.5), new Bin("AR9.5–10", 9.5, 10),
                new Bin("AR10–10.3", 10, 10.3), new Bin("AR10.3+", 10.3, double.PositiveInfinity)
            }),
            new MetricSpec("Jump spacing", r => r.Metric.NormalizedSpacing > 0 ? r.Metric.NormalizedSpacing : r.Metric.Spacing, v => v > 0 ? $"{v:0}px" : "—", new[]
            {
                new Bin("<120px", 0, 120), new Bin("120–180px", 120, 180), new Bin("180–240px", 180, 240),
                new Bin("240–300px", 240, 300), new Bin("300–360px", 300, 360), new Bin("360px+", 360, double.PositiveInfinity)
            }),
            new MetricSpec("Visible density", r => r.Metric.Density, v => v > 0 ? $"{v:0.0} objs" : "—", new[]
            {
                new Bin("<2 visible", 0, 2), new Bin("2–3", 2, 3), new Bin("3–4", 3, 4), new Bin("4–5", 4, 5),
                new Bin("5+ visible", 5, double.PositiveInfinity)
            }),
            new MetricSpec("Jump angle", r => r.Metric.ObjectIndex < 2 ? double.NaN : Math.Max(0.0001, r.Metric.Angle), v => v >= 0 ? $"{v:0}°" : "—", new[]
            {
                new Bin("0–45°", 0, 45), new Bin("45–90°", 45, 90), new Bin("90–135°", 90, 135),
                new Bin("135–165°", 135, 165), new Bin("165–180°", 165, 181)
            }),
            new MetricSpec("Local aim demand", r => r.Metric.Challenge, v => v > 0 ? $"{v:0}" : "—", new[]
            {
                new Bin("<70", 0, 70), new Bin("70–100", 70, 100), new Bin("100–130", 100, 130),
                new Bin("130–170", 130, 170), new Bin("170+", 170, double.PositiveInfinity)
            })
        };

        var metricRows = specs.Select(s => AnalyzeMetric(s, rows, selected)).ToList();

        var mapCandidates = rows.GroupBy(r => MapKey(r.Play), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First().Play;
                int causeCount = g.Count(IsSelected);
                int total = g.Count();
                int plays = g.Select(x => x.Play.Id).Distinct().Count();
                double avgBpm = g.Where(x => x.Metric.Bpm > 0).Select(x => x.Metric.Bpm).DefaultIfEmpty(first.MeanBpm).Average();
                double avgSpacing = g.Select(x => x.Metric.NormalizedSpacing > 0 ? x.Metric.NormalizedSpacing : x.Metric.Spacing)
                    .Where(x => x > 0).DefaultIfEmpty(first.SpacingP75).Average();
                return new AimErrorContextMap
                {
                    Map = first.Map,
                    Mods = first.ModsText,
                    PlayCount = plays,
                    CauseCount = causeCount,
                    TransitionCount = total,
                    CauseRate = total <= 0 ? 0 : 100.0 * causeCount / total,
                    Star = first.StarRating,
                    Ar = first.EffectiveAr,
                    Bpm = avgBpm,
                    Spacing = avgSpacing
                };
            })
            .Where(x => x.CauseCount > 0)
            .ToList();
        int affectedMaps = mapCandidates.Count;
        var maps = mapCandidates
            .OrderByDescending(x => x.CauseCount)
            .ThenByDescending(x => x.CauseRate)
            .Take(30)
            .ToList();

        var strongest = metricRows.Where(x => x.Lift > 1.05 && x.TotalSamplesInRange >= 8)
            .OrderByDescending(x => x.Lift)
            .ThenByDescending(x => x.CauseSamplesInRange)
            .Take(3)
            .ToList();

        string summary;
        if (rows.Count == 0 || selected.Count == 0)
        {
            summary = $"No {label.ToLowerInvariant()} samples exist in this history window.";
        }
        else if (strongest.Count == 0)
        {
            summary = $"{label} appears on {100.0 * selected.Count / rows.Count:0.0}% of analyzed jumps. No single SR/BPM/AR/spacing range is strongly overrepresented yet; this currently looks broadly distributed rather than tied to one map condition.";
        }
        else
        {
            string associations = string.Join("; ", strongest.Select(x => $"{x.HotRange} ({x.Lift:0.0}× your baseline rate)"));
            summary = $"{label} appears on {100.0 * selected.Count / rows.Count:0.0}% of analyzed jumps. The strongest associations in this window are {associations}. These are correlations in your replay history, not proof that the map condition itself caused the error.";
        }

        return new AimErrorContextResult
        {
            Kind = kind,
            Label = label,
            Cause = label,
            CauseCount = selected.Count,
            TransitionCount = rows.Count,
            AffectedMaps = affectedMaps,
            Metrics = metricRows,
            Maps = maps,
            Summary = summary
        };
    }

    private static AimErrorContextMetric AnalyzeMetric(MetricSpec spec, IReadOnlyList<Row> allRows, IReadOnlyList<Row> causeRows)
    {
        var all = allRows.Where(r => IsUsable(spec.Value(r))).ToList();
        var selected = causeRows.Where(r => IsUsable(spec.Value(r))).ToList();
        double metricOverallRate = all.Count == 0 ? 0 : selected.Count / (double)all.Count;

        var candidates = new List<(Bin Bin, int Cause, int Total, double Rate, double Lift, double Score)>();
        foreach (var bin in spec.Bins)
        {
            bool InBin(Row r)
            {
                double v = spec.Value(r);
                return v >= bin.Min && v < bin.Max;
            }
            int total = all.Count(InBin);
            int cause = selected.Count(InBin);
            double rate = total <= 0 ? 0 : cause / (double)total;
            double lift = metricOverallRate <= 0 ? 0 : rate / metricOverallRate;
            double score = lift * Math.Sqrt(Math.Max(1, cause));
            candidates.Add((bin, cause, total, rate, lift, score));
        }

        int minTotal = Math.Max(8, (int)Math.Ceiling(all.Count * .0075));
        var hot = candidates.Where(x => x.Cause >= 2 && x.Total >= minTotal)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Cause)
            .FirstOrDefault();
        if (hot.Bin == null)
            hot = candidates.OrderByDescending(x => x.Cause).ThenByDescending(x => x.Total).FirstOrDefault();

        double causeMedian = Median(selected.Select(spec.Value));
        double baseMedian = Median(all.Select(spec.Value));
        return new AimErrorContextMetric
        {
            Metric = spec.Name,
            HotRange = hot.Bin?.Label ?? "—",
            CauseRateInRange = hot.Rate * 100.0,
            OverallCauseRate = metricOverallRate * 100.0,
            Lift = hot.Lift,
            CauseSamplesInRange = hot.Cause,
            TotalSamplesInRange = hot.Total,
            CauseTypical = selected.Count == 0 ? "—" : spec.Format(causeMedian),
            BaselineTypical = all.Count == 0 ? "—" : spec.Format(baseMedian)
        };
    }

    private static string MapKey(PlayRow play)
    {
        string map = !string.IsNullOrWhiteSpace(play.BeatmapHash) ? play.BeatmapHash : play.Map;
        return $"{map}|{play.ModsText}";
    }

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;

    private static double Median(IEnumerable<double> values)
    {
        var a = values.Where(IsUsable).OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        int mid = a.Length / 2;
        return a.Length % 2 == 1 ? a[mid] : (a[mid - 1] + a[mid]) / 2.0;
    }
}
