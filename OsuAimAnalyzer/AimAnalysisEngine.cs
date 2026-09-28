namespace OsuAimAnalyzer;

/// <summary>
/// Builds the uncapped radar profile from real replay transitions.
/// Unlike proficiency (execution quality on a 0-1000 scale), radar scores are
/// difficulty-adjusted capability scores. 1000 is a strong reference level, not a cap.
/// </summary>
public sealed class AimAnalysisEngine
{
    private readonly AnalyzerDatabase database;
    public AimAnalysisEngine(AnalyzerDatabase database) => this.database = database;

    private sealed record Sample(PlayRow Play, TransitionMetric T);
    private sealed record AxisResult(double Score, int Samples, int Plays, string Evidence);

    public AimAnalysisReport BuildReport(IEnumerable<PlayRow> playsSource, string preset, string historyLabel)
    {
        var plays = playsSource.OrderBy(p => p.TimestampUtc).ToList();
        var report = new AimAnalysisReport { Preset = preset, HistoryRange = historyLabel };
        if (plays.Count == 0)
        {
            report.Summary = "No plays match the selected history window yet.";
            return report;
        }

        var byId = plays.ToDictionary(p => p.Id);
        var samples = database.LoadTransitions(plays.Select(p => p.Id))
            .Where(t => byId.ContainsKey(t.PlayId))
            .Select(t => new Sample(byId[t.PlayId], t))
            .Where(s => DiagnosticsEngine.MatchesPreset(s.Play, s.T, preset))
            .ToList();
        if (samples.Count == 0)
        {
            report.Summary = "The selected skill preset did not find enough matching aim transitions in this history window.";
            return report;
        }

        var contributingIds = samples.Select(s => s.Play.Id).Distinct().ToHashSet();
        var contributingPlays = plays.Where(p => contributingIds.Contains(p.Id)).ToList();
        report.PlayCount = contributingPlays.Count;
        report.TransitionCount = samples.Count;
        report.AverageProficiency = samples.Average(s => s.T.Proficiency);
        report.AimRating = AimRatingUtils.Top100Average(contributingPlays);
        report.AverageTension = samples.Average(Tension);

        var axes = new (string Name, AxisResult Result, string Meaning)[]
        {
            ("Snap aim", SnapAim(samples), "fast target-to-target snapping under short timing/preview windows"),
            ("Flow aim", FlowAim(samples), "continuous, efficient cursor travel through flowing patterns"),
            ("Wide-angle aim", WideAngleAim(samples), "redirecting cleanly through large-angle and wide jumps"),
            ("Precision", Precision(samples), "centering accurately while difficulty, AR and map demand rise"),
            ("Smoothness", Smoothness(samples), "keeping the cursor efficient and low-jitter at real movement speed"),
            ("Braking", Braking(samples), "decelerating and settling after high-travel or high-velocity jumps"),
            ("Speed", Speed(samples), "maintaining useful execution quality as movement rate increases"),
            ("Tension control", TensionControl(samples), "staying mechanically relaxed while the replay demand rises"),
        };

        report.Aspects = axes.Select(x => new AimAspectScore
        {
            Name = x.Name,
            Score = x.Result.Score,
            Interpretation = Humanize.CapabilityTier(x.Result.Score),
            Note = $"{x.Meaning}. {x.Result.Evidence}"
        }).ToList();

        var top3 = report.Aspects.OrderByDescending(a => a.Score).Take(3).Select(a => a.Name).ToList();
        var bot3 = report.Aspects.OrderBy(a => a.Score).Take(3).Select(a => a.Name).ToList();

        bool snappy = Get(report.Aspects, "Snap aim") >= MedianScore(report.Aspects) + 55;
        bool smooth = Get(report.Aspects, "Smoothness") >= MedianScore(report.Aspects) + 45 || Get(report.Aspects, "Flow aim") >= MedianScore(report.Aspects) + 55;
        bool robotic = Get(report.Aspects, "Precision") >= MedianScore(report.Aspects) + 70 && Get(report.Aspects, "Smoothness") <= MedianScore(report.Aspects) - 45;
        bool tense = Get(report.Aspects, "Tension control") <= MedianScore(report.Aspects) - 70 || report.AverageTension >= 38;
        bool wide = Get(report.Aspects, "Wide-angle aim") >= MedianScore(report.Aspects) + 55;
        bool speedBiased = Get(report.Aspects, "Speed") >= MedianScore(report.Aspects) + 65;

        if (snappy) report.Traits.Add("snappy");
        if (smooth) report.Traits.Add("smooth");
        if (robotic) report.Traits.Add("robotic");
        if (wide) report.Traits.Add("wide-jump biased");
        if (speedBiased) report.Traits.Add("speed-biased");
        if (tense) report.Traits.Add("tension-prone");
        if (report.Traits.Count == 0) report.Traits.Add("balanced");

        string presetLead = preset == "All aim" ? "your overall aim" : preset.ToLowerInvariant();
        report.StyleText = $"Your replay profile in {presetLead} reads most strongly as {string.Join(", ", report.Traits)}.";
        report.StrengthText = $"Highest capability axes: {string.Join(", ", top3)}.";
        report.WeaknessText = $"Lowest relative axes: {string.Join(", ", bot3)}.";
        double maxAxis = report.Aspects.Max(a => a.Score);
        report.Summary = $"Across {report.PlayCount:N0} matching plays and {report.TransitionCount:N0} real replay aim transitions, average execution proficiency is {Humanize.Score(report.AverageProficiency)} and profile aim rating is {report.AimRating:0}. The radar is an uncapped difficulty-adjusted capability scale: 1000 is a reference ring, not a maximum. Your current highest axis is {maxAxis:0}. {report.StyleText} {report.StrengthText} {report.WeaknessText}";
        return report;
    }

    private static double MedianScore(List<AimAspectScore> a)
    {
        var x = a.Select(v => v.Score).OrderBy(v => v).ToArray();
        if (x.Length == 0) return 0;
        return x.Length % 2 == 1 ? x[x.Length / 2] : (x[x.Length / 2 - 1] + x[x.Length / 2]) / 2;
    }

    private static double Get(List<AimAspectScore> aspects, string name) => aspects.First(a => a.Name == name).Score;

    private static double Tension(Sample s) => ScoringConfig.InferredAimTension(
        s.T.Stability, s.T.Deceleration, s.T.Straightness, s.T.Landing, s.T.ErrorClass,
        s.T.Bpm, s.Play.EffectiveAr, s.T.NormalizedSpacing, s.T.Density);

    private static double Blend(params (double value, double weight)[] parts)
    {
        double w = parts.Sum(p => p.weight);
        return w <= 0 ? 0 : parts.Sum(p => p.value * p.weight) / w;
    }

    private static double Geo(params (double value, double power)[] factors)
    {
        double total = 1;
        foreach (var f in factors)
            total *= Math.Pow(Math.Max(.18, f.value), f.power);
        return total;
    }

    /// <summary>
    /// Converts a 0-1000 execution-quality value into a steeper multiplier around the
    /// high-skill range. This intentionally creates separation between e.g. 720 and 900
    /// instead of keeping all radar axes packed together.
    /// </summary>
    private static double QualityMultiplier(double q)
    {
        double n = Math.Clamp(q / 900.0, .35, 1.22);
        return Math.Pow(n, 1.72);
    }

    private static double SustainedQuality(List<Sample> samples, Func<Sample, double> quality)
    {
        if (samples.Count == 0) return 0;
        var vals = samples.Select(quality).OrderBy(x => x).ToArray();
        double mean = vals.Average();
        int lowerCount = Math.Max(1, vals.Length / 3);
        double lower = vals.Take(lowerCount).Average();
        return .72 * mean + .28 * lower;
    }

    private static AxisResult Capability(
        List<Sample> source,
        Func<Sample, bool> predicate,
        Func<List<Sample>, double> quality,
        Func<List<Sample>, double> demand,
        string demandLabel)
    {
        var selected = source.Where(predicate).ToList();
        // If the history has very little evidence for an axis, broaden to all samples rather
        // than returning an arbitrary zero. The evidence text makes this visible to the user.
        bool broadened = selected.Count < 20;
        if (broadened) selected = source.ToList();

        var playScores = new List<(double Score, int Count, PlayRow Play, double Demand, double Quality)>();
        foreach (var g in selected.GroupBy(s => s.Play.Id))
        {
            var rows = g.ToList();
            if (rows.Count < 5) continue;
            double q = quality(rows);
            double d = demand(rows);
            double raw = 1000.0 * QualityMultiplier(q) * d;
            // A play with collapsing raw execution should not become a huge capability score
            // merely because it contains one absurdly difficult jump.
            double prof = rows.Average(s => s.T.Proficiency);
            if (prof < 700) raw *= Math.Clamp(.72 + (prof - 550) / 500.0, .55, .98);
            playScores.Add((raw, rows.Count, rows[0].Play, d, q));
        }

        if (playScores.Count == 0)
            return new AxisResult(0, selected.Count, 0, "Not enough replay evidence yet.");

        // Capability is intentionally peak-biased but not a single lucky object. We use the
        // best several full-play slices with a light decay, so repeating one good pattern
        // cannot dominate the profile while genuinely harder successful plays can push >1000.
        var best = playScores.OrderByDescending(x => x.Score).Take(12).ToList();
        double sum = 0, weights = 0;
        for (int i = 0; i < best.Count; i++)
        {
            double w = Math.Pow(.92, i);
            sum += best[i].Score * w;
            weights += w;
        }
        double score = sum / Math.Max(.001, weights);
        var peak = best[0];
        string broaden = broadened ? " Evidence was sparse for the strict axis filter, so the analyzer broadened the sample." : "";
        string evidence = $"Built from {selected.Count:N0} replay transitions across {playScores.Count:N0} qualifying plays; best sustained evidence: {peak.Play.ModsText}, {peak.Play.StarRating:0.00}★, AR {peak.Play.EffectiveAr:0.0}, {peak.Play.MeanBpm:0} BPM. {demandLabel}: {peak.Demand:0.00}× reference demand; execution quality {peak.Quality:0}/1000.{broaden}";
        return new AxisResult(score, selected.Count, playScores.Count, evidence);
    }

    private static AxisResult SnapAim(List<Sample> s) => Capability(
        s,
        x => x.T.Bpm >= 220 && x.T.IntervalMs <= 175 && x.T.NormalizedSpacing >= 95,
        rows => SustainedQuality(rows, x => Blend((x.T.Arrival, .34), (x.T.Landing, .27), (x.T.Proficiency, .22), (x.T.IdealPathMatch, .17))),
        rows => Geo(
            (rows.Average(x => x.T.Bpm) / 280.0, .52),
            (rows.Average(x => x.T.NormalizedSpacing) / 220.0, .25),
            (1 + Math.Max(0, rows.Average(x => x.Play.EffectiveAr) - 9.5) * .08, .55)),
        "snap demand");

    private static AxisResult FlowAim(List<Sample> s) => Capability(
        s,
        x => x.T.Angle >= 18 && x.T.Angle < 100 && x.T.NormalizedSpacing >= 90,
        rows => SustainedQuality(rows, x => Blend((x.T.Straightness, .31), (x.T.IdealPathMatch, .27), (x.T.Stability, .22), (x.T.Deceleration, .20))),
        rows => Geo(
            (rows.Average(x => x.T.Velocity) / 1900.0, .42),
            (rows.Average(x => x.T.Bpm) / 245.0, .28),
            (rows.Average(x => x.T.NormalizedSpacing) / 190.0, .16)),
        "flow demand");

    private static AxisResult WideAngleAim(List<Sample> s) => Capability(
        s,
        x => x.T.Angle >= 95 && x.T.NormalizedSpacing >= 145,
        rows => SustainedQuality(rows, x => Blend((x.T.Landing, .30), (x.T.Deceleration, .27), (x.T.Arrival, .19), (x.T.Proficiency, .24))),
        rows => Geo(
            (rows.Average(x => x.T.NormalizedSpacing) / 260.0, .43),
            (rows.Average(x => x.T.Bpm) / 235.0, .28),
            (rows.Average(x => Math.Clamp(x.T.Angle, 90, 170)) / 120.0, .16)),
        "wide-angle demand");

    private static AxisResult Precision(List<Sample> s) => Capability(
        s,
        x => true,
        rows => SustainedQuality(rows, x => Blend((x.T.Landing, .52), (x.T.Arrival, .17), (x.T.IdealPathMatch, .17), (x.T.Stability, .14))),
        rows => Geo(
            ((rows[0].Play.StarRating > 0 ? rows[0].Play.StarRating : 5.5) / 6.6, .52),
            (1 + Math.Max(0, rows[0].Play.EffectiveAr - 9.2) * .065, .65),
            (((rows[0].Play.Mods & ModUtils.HardRock) != 0) ? 1.10 : 1.0, .55)),
        "precision demand");

    private static AxisResult Smoothness(List<Sample> s) => Capability(
        s,
        x => x.T.Velocity >= 900,
        rows => SustainedQuality(rows, x => Blend((x.T.Straightness, .34), (x.T.IdealPathMatch, .30), (x.T.Stability, .24), (x.T.Deceleration, .12))),
        rows => Geo(
            (rows.Average(x => x.T.Velocity) / 1900.0, .48),
            (rows.Average(x => x.T.Bpm) / 250.0, .22),
            (rows.Average(x => x.T.NormalizedSpacing) / 200.0, .18)),
        "movement demand");

    private static AxisResult Braking(List<Sample> s) => Capability(
        s,
        x => x.T.NormalizedSpacing >= 160 || x.T.Velocity >= 1700,
        rows => SustainedQuality(rows, x => Blend((x.T.Deceleration, .50), (x.T.Landing, .23), (x.T.Stability, .17), (x.T.IdealPathMatch, .10))),
        rows => Geo(
            (rows.Average(x => x.T.NormalizedSpacing) / 230.0, .38),
            (rows.Average(x => x.T.Velocity) / 1850.0, .38),
            (rows.Average(x => x.T.Bpm) / 240.0, .12)),
        "braking demand");

    private static AxisResult Speed(List<Sample> s) => Capability(
        s,
        x => x.T.Bpm >= 230 || x.T.IntervalMs <= 160,
        rows => SustainedQuality(rows, x => Blend((x.T.Proficiency, .35), (x.T.Arrival, .25), (x.T.Landing, .21), (x.T.Deceleration, .19))),
        rows => Geo(
            (rows.Average(x => x.T.Bpm) / 290.0, .58),
            ((130.0 / Math.Max(70, rows.Average(x => x.T.IntervalMs))), .28),
            (rows.Average(x => x.T.NormalizedSpacing) / 180.0, .12)),
        "speed demand");

    private static AxisResult TensionControl(List<Sample> s) => Capability(
        s,
        x => x.T.Bpm >= 210 || x.T.NormalizedSpacing >= 150,
        rows =>
        {
            double tension = rows.Average(Tension);
            double relaxed = Math.Clamp(1000 - tension * 11.5, 300, 1000);
            double mechanics = rows.Average(x => Blend((x.T.Stability, .42), (x.T.Deceleration, .28), (x.T.Straightness, .18), (x.T.Landing, .12)));
            return .62 * relaxed + .38 * mechanics;
        },
        rows => Geo(
            (rows.Average(x => x.T.Bpm) / 270.0, .33),
            (rows.Average(x => x.T.NormalizedSpacing) / 210.0, .23),
            (1 + Math.Max(0, rows.Average(x => x.Play.EffectiveAr) - 9.4) * .075, .55),
            (rows.Average(x => x.T.Density) / 3.5, .12)),
        "tension demand");
}
