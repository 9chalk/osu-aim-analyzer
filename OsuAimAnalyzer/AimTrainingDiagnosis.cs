namespace OsuAimAnalyzer;

public sealed class AimCategoryVolume
{
    public string Category { get; init; } = "";
    public int TransitionCount { get; init; }
    public int PlayCount { get; init; }
    public double CoveragePercent { get; init; }
    public double ErrorRate { get; init; }
}

public sealed class AimDiagnosisFactor
{
    public string UnitKind { get; init; } = "";
    public double ObservedMedian { get; init; }
    public double ControlledMedian { get; init; }
    public double ControlledLowerQuartile { get; init; }
    public double ControlledUpperQuartile { get; init; }
    public string Metric { get; init; } = "";
    public double Confidence { get; init; }
    public string Direction { get; init; } = "";
    public string Observed { get; init; } = "—";
    public string ComfortBand { get; init; } = "—";
    public string Difference { get; init; } = "—";
    public double Lift { get; init; }
    public int ErrorSamples { get; init; }
    public int ComparisonSamples { get; init; }
    public string Explanation { get; init; } = "";
    public string Adjustment { get; init; } = "";
}

public sealed class AimTrainingRoute
{
    public string Title { get; init; } = "";
    public string Instruction { get; init; } = "";
    public string Why { get; init; } = "";
    public string Reference { get; init; } = "";
}

public sealed class AimCategoryDiagnosis
{
    public string Category { get; init; } = "";
    public string ErrorLabel { get; init; } = "";
    public int PlayCount { get; init; }
    public int TransitionCount { get; init; }
    public int ErrorCount { get; init; }
    public double ErrorRate { get; init; }
    public double MedianProficiency { get; init; }
    public string Status { get; init; } = "";
    public string Summary { get; init; } = "";
    public string MainVolume { get; init; } = "";
    public string LimitTesting { get; init; } = "";
    public string MasteryTarget { get; init; } = "";
    public List<AimDiagnosisFactor> Factors { get; init; } = new();
    public List<AimTrainingRoute> Routes { get; init; } = new();
}

public static class AimTrainingDiagnosisEngine
{
    private sealed record Row(TransitionMetric T, PlayRow Play, string Cause, string Direction);
    private sealed record MetricSpec(string Name, Func<Row, double> Value, Func<double, string> Format, string UnitKind);

    private static readonly string[] Categories = DiagnosticsEngine.PresetNames.Where(x => !x.Equals("All aim", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static readonly MetricSpec[] Metrics =
    {
        new("Star rating", r => r.Play.StarRating, v => v > 0 ? $"{v:0.00}★" : "—", "star"),
        new("BPM", r => r.T.Bpm, v => v > 0 ? $"{v:0} BPM" : "—", "bpm"),
        new("Approach rate", r => r.Play.EffectiveAr, v => v > 0 ? $"AR {v:0.0}" : "—", "ar"),
        new("Jump spacing", r => r.T.NormalizedSpacing > 0 ? r.T.NormalizedSpacing : r.T.Spacing, v => v > 0 ? $"{v:0}px" : "—", "spacing"),
        new("Visible density", r => r.T.Density, v => v > 0 ? $"{v:0.0} visible" : "—", "density"),
        new("Local aim demand", r => r.T.Challenge, v => v > 0 ? $"{v:0}" : "—", "demand")
    };

    public static List<AimCategoryVolume> RankCategories(LifetimeAimAnalysisData data, string selectionKey)
    {
        var rows = BuildRows(data);
        if (rows.Count == 0) return new();
        var result = new List<AimCategoryVolume>();
        foreach (string category in Categories)
        {
            var categoryRows = rows.Where(r => DiagnosticsEngine.MatchesPreset(r.Play, r.T, category)).ToList();
            if (categoryRows.Count == 0) continue;
            int errors = categoryRows.Count(r => Selected(r, selectionKey));
            result.Add(new AimCategoryVolume
            {
                Category = category,
                TransitionCount = categoryRows.Count,
                PlayCount = categoryRows.Select(x => x.Play.Id).Distinct().Count(),
                CoveragePercent = 100.0 * categoryRows.Count / rows.Count,
                ErrorRate = 100.0 * errors / categoryRows.Count
            });
        }
        return result.OrderByDescending(x => x.PlayCount).ThenByDescending(x => x.TransitionCount).ToList();
    }

    public static AimCategoryDiagnosis BuildCategory(LifetimeAimAnalysisData data, string selectionKey, string category)
    {
        var rows = BuildRows(data).Where(r => DiagnosticsEngine.MatchesPreset(r.Play, r.T, category)).ToList();
        return BuildDiagnosis(rows, selectionKey, category, null);
    }

    public static string BuildRunDiagnosis(PlayRow play, IReadOnlyList<TransitionMetric> currentTransitions, AnalyzerDatabase database, IReadOnlyList<PlayRow> history)
        => BuildRunDiagnosisData(play, currentTransitions, database, history).DisplayText;

    public static RunDiagnosisResult BuildRunDiagnosisData(PlayRow play, IReadOnlyList<TransitionMetric> currentTransitions, AnalyzerDatabase database, IReadOnlyList<PlayRow> history)
        => BuildRunDiagnosisCore(play, currentTransitions, history, ids => database.LoadTransitions(ids));

    /// <summary>Uses already-loaded history; shares all classification and formatting with the database path.</summary>
    public static RunDiagnosisResult BuildRunDiagnosisData(PlayRow play, IReadOnlyList<TransitionMetric> currentTransitions,
        IReadOnlyList<PlayRow> history, IReadOnlyList<TransitionMetric> historyTransitions)
        => BuildRunDiagnosisCore(play, currentTransitions, history, ids =>
        {
            var selectedIds = ids.ToHashSet();
            return historyTransitions.Where(t => selectedIds.Contains(t.PlayId)).ToList();
        });

    private static RunDiagnosisResult BuildRunDiagnosisCore(PlayRow play, IReadOnlyList<TransitionMetric> currentTransitions,
        IReadOnlyList<PlayRow> history, Func<IEnumerable<long>, List<TransitionMetric>> loadTransitions)
    {
        if (currentTransitions.Count == 0)
            return new RunDiagnosisResult(play.Id, false, "", "", null, Array.Empty<string>(), "No transition telemetry is available for this run.");

        string category = Categories
            .Select(c => (Category: c, Count: currentTransitions.Count(t => DiagnosticsEngine.MatchesPreset(play, t, c))))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .Select(x => x.Category)
            .FirstOrDefault() ?? "All aim";

        var causeSummary = AimErrorDiagnostics.Analyze(currentTransitions);
        string cause = causeSummary.PrimaryCause == AimErrorDiagnostics.Clean
            ? currentTransitions.GroupBy(t => string.IsNullOrWhiteSpace(t.ErrorClass) ? "Plain error" : t.ErrorClass, StringComparer.OrdinalIgnoreCase)
                .Where(g => !g.Key.Equals("Clean", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "Clean"
            : causeSummary.PrimaryCause;
        string selectionKey = causeSummary.PrimaryCause == AimErrorDiagnostics.Clean ? "direction:" + cause : "cause:" + cause;

        var comparisonPlays = history.Where(p => p.Id != play.Id && (category == "All aim" || DiagnosticsEngine.PlayMatchesPreset(p, category))).ToList();
        var comparisonTransitions = comparisonPlays.Count == 0 ? new List<TransitionMetric>() : loadTransitions(comparisonPlays.Select(p => p.Id));
        var playById = comparisonPlays.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<Row>();
        foreach (var t in comparisonTransitions)
            if (playById.TryGetValue(t.PlayId, out var p) && (category == "All aim" || DiagnosticsEngine.MatchesPreset(p, t, category)))
                rows.Add(ToRow(t, p));
        foreach (var t in currentTransitions)
            if (category == "All aim" || DiagnosticsEngine.MatchesPreset(play, t, category))
                rows.Add(ToRow(t, play));

        var diagnosis = BuildDiagnosis(rows, selectionKey, category, play.Id);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"THIS RUN · {category.ToUpperInvariant()}");
        sb.AppendLine($"Primary issue: {diagnosis.ErrorLabel} · {diagnosis.ErrorRate:0.0}% of comparable jumps · {Humanize.ProductionScore(play.Proficiency)} overall control");
        sb.AppendLine();
        if (diagnosis.Factors.Count == 0)
        {
            sb.AppendLine("No demand variable is strongly separated from your controlled history on similar maps. This run looks more like execution variance than one obvious map-demand mismatch.");
        }
        else
        {
            sb.AppendLine("LIKELY CONTRIBUTORS");
            foreach (var f in diagnosis.Factors.Take(4))
                sb.AppendLine($"• {f.Confidence:0}% evidence confidence · {f.Metric}: {f.Explanation}");
        }
        string runMain = play.Proficiency >= ProductionScoring.ChallengingThreshold && play.Proficiency < ProductionScoring.MasteredThreshold
            ? $"This exact map is inside your useful main-volume control band ({Humanize.ProductionScore(play.Proficiency)}). Keep maps like this in rotation while the diagnosed error rate falls and the top limiter improves."
            : play.Proficiency >= ProductionScoring.MasteredThreshold
                ? $"This exact map is already in your mastered range ({Humanize.ProductionScore(play.Proficiency)}). It is useful as warm-up/confirmation, but main volume should be a slightly harder version of the same aim type."
                : $"This exact map is below your clean main-volume threshold ({Humanize.ProductionScore(play.Proficiency)}). For volume, back off the leading demand mismatch first. " + (diagnosis.Factors.FirstOrDefault()?.Adjustment ?? diagnosis.MainVolume);
        string runLimit = play.Proficiency < ProductionScoring.ChallengingThreshold
            ? $"This exact map is appropriately difficult for limit testing: it is outside clean control, so use it to probe capacity rather than to judge normal consistency."
            : play.Proficiency < ProductionScoring.ControlledThreshold
                ? "This map is challenging but still trainable. It can be occasional limit work, but it is also close enough to controlled play to keep in main volume."
                : "This map is too controlled to be a meaningful limit test. For limit work, raise one demand at a time while keeping the same aim type.";

        sb.AppendLine();
        sb.AppendLine("MAIN TRAINING VOLUME");
        sb.AppendLine(runMain);
        sb.AppendLine();
        sb.AppendLine("LIMIT TESTING");
        sb.AppendLine(runLimit);
        sb.AppendLine();
        sb.AppendLine("NEXT OBJECTIVE");
        sb.AppendLine(diagnosis.MasteryTarget);
        var response = PlayerInsightsEngine.TrainingResponseForRun(play, history);
        if (response.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RECENT TRAINING RESPONSE");
            foreach (var line in response) sb.AppendLine("• " + line);
            sb.AppendLine("These are sequence associations: they show what preceded the change, not guaranteed causality.");
        }
        sb.AppendLine();
        sb.Append("Confidence values are evidence scores from your own history (effect size + separation from controlled maps + sample support), not literal causal probabilities.");
        return new RunDiagnosisResult(play.Id, true, category, selectionKey, diagnosis, response, sb.ToString());
    }

    private static AimCategoryDiagnosis BuildDiagnosis(List<Row> rows, string selectionKey, string category, long? focusPlayId)
    {
        var selected = rows.Where(r => Selected(r, selectionKey) && (!focusPlayId.HasValue || r.Play.Id == focusPlayId.Value)).ToList();
        var categoryRowsForRate = focusPlayId.HasValue ? rows.Where(r => r.Play.Id == focusPlayId.Value).ToList() : rows;
        if (!focusPlayId.HasValue) selected = rows.Where(r => Selected(r, selectionKey)).ToList();

        string label = selectionKey.Contains(':') ? selectionKey[(selectionKey.IndexOf(':') + 1)..] : selectionKey;
        var controlled = rows.Where(r => r.Play.Proficiency >= ProductionScoring.ControlledThreshold).ToList();
        if (controlled.Count < 40)
        {
            double cutoff = Percentile(rows.Select(r => r.Play.Proficiency), .65);
            controlled = rows.Where(r => r.Play.Proficiency >= cutoff).ToList();
        }

        var factors = Metrics.Select(m => AnalyzeFactor(m, rows, selected, controlled, selectionKey)).Where(x => x != null)
            .Cast<AimDiagnosisFactor>().OrderByDescending(x => x.Confidence).ThenByDescending(x => x.Lift).Take(5).ToList();

        var plays = rows.Select(r => r.Play).GroupBy(p => p.Id).Select(g => g.First()).ToList();
        double medianProf = Median(plays.Select(p => p.Proficiency));
        string status = medianProf >= ProductionScoring.MasteredThreshold ? "Mastered"
            : medianProf >= ProductionScoring.ControlledThreshold ? "Controlled"
            : medianProf >= ProductionScoring.ChallengingThreshold ? "Main-volume challenge"
            : "Limit / breakdown";

        var mainVolumePlays = plays.Where(p => p.Proficiency >= ProductionScoring.ChallengingThreshold && p.Proficiency < ProductionScoring.MasteredThreshold).ToList();
        var limitPlays = plays.Where(p => p.Proficiency < ProductionScoring.ChallengingThreshold && p.Proficiency >= 400).ToList();
        string mainRange = DescribePlayRange(mainVolumePlays.Count >= 3 ? mainVolumePlays : plays);
        string limitRange = DescribePlayRange(limitPlays.Count >= 2 ? limitPlays : plays.OrderBy(p => p.Proficiency).Take(Math.Max(1, plays.Count / 3)).ToList());

        string mainVolume = status == "Mastered"
            ? $"You have already mastered most of this category at your current history level. Move the main volume slightly above {mainRange}, changing one demand at a time."
            : $"Keep most volume near {mainRange}. That is where your history places this category in the challenging-to-controlled band ({ProductionScoring.ChallengingThreshold:0}–{ProductionScoring.MasteredThreshold:0} proficiency).";
        if (factors.Count > 0 && !string.IsNullOrWhiteSpace(factors[0].Adjustment))
            mainVolume += " " + factors[0].Adjustment;

        string limitTesting = $"For limit tests, {limitRange} is representative of the edge you have actually attempted. Treat maps below {ProductionScoring.ChallengingThreshold:0} proficiency as limit work, not main volume; a hard play is useful without pretending it is controlled.";
        string mastery = $"Call this category controlled when a recent block of similar maps averages at least {ProductionScoring.ControlledThreshold:0} proficiency and the {label.ToLowerInvariant()} rate no longer rises materially above your category baseline. Mastered begins around {ProductionScoring.MasteredThreshold:0}.";

        string summary = factors.Count == 0
            ? $"{label} is present, but no single tracked map-demand variable separates it strongly from your controlled {category} history yet."
            : $"The strongest measurable contributor is {factors[0].Metric.ToLowerInvariant()} ({factors[0].Confidence:0}% evidence confidence). {factors[0].Explanation}";

        var routes = BuildTrainingRoutes(plays, controlled.Select(r => r.Play).GroupBy(p => p.Id).Select(g => g.First()).ToList(), factors, status);

        return new AimCategoryDiagnosis
        {
            Category = category,
            ErrorLabel = label,
            PlayCount = plays.Count,
            TransitionCount = categoryRowsForRate.Count,
            ErrorCount = selected.Count,
            ErrorRate = categoryRowsForRate.Count == 0 ? 0 : 100.0 * selected.Count / categoryRowsForRate.Count,
            MedianProficiency = medianProf,
            Status = status,
            Summary = summary,
            MainVolume = mainVolume,
            LimitTesting = limitTesting,
            MasteryTarget = mastery,
            Factors = factors,
            Routes = routes
        };
    }

    private static AimDiagnosisFactor? AnalyzeFactor(MetricSpec spec, IReadOnlyList<Row> all, IReadOnlyList<Row> selected, IReadOnlyList<Row> controlled, string selectionKey)
    {
        var a = all.Where(r => Usable(spec.Value(r))).ToList();
        var s = selected.Where(r => Usable(spec.Value(r))).ToList();
        var c = controlled.Where(r => Usable(spec.Value(r))).ToList();
        if (a.Count < 20 || s.Count < 3 || c.Count < 10) return null;

        double selectedMedian = Median(s.Select(spec.Value));
        double comfortMedian = Median(c.Select(spec.Value));
        double q25 = Percentile(c.Select(spec.Value), .25);
        double q75 = Percentile(c.Select(spec.Value), .75);
        if (!Usable(selectedMedian) || !Usable(comfortMedian) || !Usable(q25) || !Usable(q75)) return null;

        bool high = selectedMedian >= comfortMedian;
        double threshold = high ? q75 : q25;
        Func<Row, bool> extreme = high ? r => spec.Value(r) >= threshold : r => spec.Value(r) <= threshold;
        int extremeTotal = a.Count(extreme);
        int extremeSelected = s.Count(extreme);
        double baseRate = s.Count / (double)a.Count;
        double extremeRate = extremeTotal <= 0 ? 0 : extremeSelected / (double)extremeTotal;
        double lift = baseRate <= 0 ? 0 : extremeRate / baseRate;
        double signedDelta = comfortMedian == 0 ? 0 : (selectedMedian - comfortMedian) / Math.Abs(comfortMedian);
        double outside = high ? Math.Max(0, selectedMedian - q75) / Math.Max(Math.Abs(q75), 1e-6)
                              : Math.Max(0, q25 - selectedMedian) / Math.Max(Math.Abs(q25), 1e-6);

        double liftStrength = Math.Clamp((lift - 1.0) / .70, 0, 1);
        double separationStrength = Math.Clamp(Math.Abs(signedDelta) / .20 + outside / .15, 0, 1);
        double sampleStrength = 1.0 - Math.Exp(-s.Count / 35.0);
        double confidence = 100.0 * (.45 * liftStrength + .35 * separationStrength + .20 * sampleStrength);
        if (lift < 1.03 && outside < .025 && Math.Abs(signedDelta) < .04) return null;
        confidence = Math.Clamp(confidence, 8, 94);

        string direction = high ? "above" : "below";
        string diff = Math.Abs(signedDelta) < .005 ? "about the same" : $"{Math.Abs(signedDelta) * 100:0}% {(signedDelta > 0 ? "higher" : "lower")}";
        string rateEvidence = lift > 0 ? $"the error occurs {lift:0.00}× as often in that {direction}-comfort slice" : "the rate difference is still weak";
        string explanation = $"typical error jumps are {spec.Format(selectedMedian)}, {diff} than your controlled-map median ({spec.Format(comfortMedian)}); {rateEvidence}.";
        string adjustment = BuildAdjustment(spec, selectedMedian, q25, q75, high);

        return new AimDiagnosisFactor
        {
            UnitKind = spec.UnitKind,
            ObservedMedian = selectedMedian,
            ControlledMedian = comfortMedian,
            ControlledLowerQuartile = q25,
            ControlledUpperQuartile = q75,
            Metric = spec.Name,
            Confidence = confidence,
            Direction = direction,
            Observed = spec.Format(selectedMedian),
            ComfortBand = $"{spec.Format(q25)} – {spec.Format(q75)}",
            Difference = diff,
            Lift = lift,
            ErrorSamples = s.Count,
            ComparisonSamples = c.Count,
            Explanation = explanation,
            Adjustment = adjustment
        };
    }

    private static string BuildAdjustment(MetricSpec spec, double observed, double q25, double q75, bool high)
    {
        double target = high ? q75 : q25;
        if (!Usable(target) || !Usable(observed)) return "";
        return spec.UnitKind switch
        {
            "bpm" when high => $"For main volume, try roughly {Math.Max(5, observed - target):0} BPM slower before changing spacing/AR.",
            "bpm" => $"For main volume, try roughly {Math.Max(5, target - observed):0} BPM faster only if the lower-speed pattern is causing over-waiting; otherwise hold BPM and adjust another variable.",
            "spacing" when high => $"For main volume, reduce CS4-normalized spacing by about {Math.Clamp(100 * (observed - target) / Math.Max(1, observed), 3, 35):0}% toward {target:0}px.",
            "spacing" => $"For main volume, use spacing closer to {target:0}px before adding more speed.",
            "ar" when high => $"For main volume, use roughly AR {target:0.0} before increasing preview pressure again.",
            "ar" => $"For main volume, stay near AR {target:0.0} while testing whether the issue persists.",
            "star" when high => $"For main volume, step down toward about {target:0.00}★ while keeping the same aim type.",
            "density" when high => $"For main volume, reduce visible density toward about {target:0.0} objects before adding more speed/spacing.",
            "demand" when high => $"For main volume, choose a similar pattern with local demand nearer {target:0} before pushing the same pattern harder.",
            _ => "Change this variable alone first; keep the other map demands similar so the next comparison is interpretable."
        };
    }

    private static List<AimTrainingRoute> BuildTrainingRoutes(IReadOnlyList<PlayRow> plays, IReadOnlyList<PlayRow> controlled, IReadOnlyList<AimDiagnosisFactor> factors, string status)
    {
        var routes = new List<AimTrainingRoute>();
        if (plays.Count == 0) return routes;
        var target = plays.OrderByDescending(p => p.TimestampUtc).First();
        var candidates = controlled.Count > 0 ? controlled : plays.Where(p => p.Proficiency >= ProductionScoring.ChallengingThreshold).ToList();
        PlayRow? Closest(Func<PlayRow, double>? penalty = null) => candidates
            .OrderBy(p => SimilarityDistance(target, p) + (penalty?.Invoke(p) ?? 0)).FirstOrDefault();
        string Ref(PlayRow? p) => p == null ? "No proven reference map in this history window yet." : $"Reference you already control: {p.Map} [{p.ModsText}] · {p.Proficiency:0} prof · {p.MeanBpm:0} BPM · {p.SpacingP75:0}px · AR {p.EffectiveAr:0.0}.";

        var nearest = Closest();
        routes.Add(new AimTrainingRoute
        {
            Title = "1 · Nearest proven stepping stone",
            Instruction = status == "Mastered" ? "Use the nearest slightly harder map of the same aim type." : "Prioritize the most similar map you already control, then return to the problem map.",
            Why = "This changes the fewest variables at once, so transfer is more likely and the next comparison stays interpretable.",
            Reference = Ref(nearest)
        });

        foreach (var f in factors.Take(2))
        {
            PlayRow? reference = f.Metric switch
            {
                "Jump spacing" => Closest(p => Math.Abs(p.MeanBpm-target.MeanBpm)/25.0 + (p.SpacingP75 >= target.SpacingP75 ? 2.0 : 0)),
                "BPM" => Closest(p => Math.Abs(p.SpacingP75-target.SpacingP75)/45.0 + (p.MeanBpm >= target.MeanBpm ? 2.0 : 0)),
                "Approach rate" => Closest(p => Math.Abs(p.MeanBpm-target.MeanBpm)/30.0 + Math.Abs(p.SpacingP75-target.SpacingP75)/55.0 + (p.EffectiveAr >= target.EffectiveAr ? 1.5 : 0)),
                _ => Closest()
            };
            string title = f.Metric switch
            {
                "Jump spacing" => "Hold speed · reduce spacing",
                "BPM" => "Hold spacing · reduce speed",
                "Approach rate" => "Hold pattern demand · reduce preview pressure",
                _ => $"Isolate {f.Metric.ToLowerInvariant()}"
            };
            routes.Add(new AimTrainingRoute { Title = title, Instruction = f.Adjustment, Why = $"{f.Confidence:0}% evidence confidence identifies {f.Metric.ToLowerInvariant()} as a separable contributor. Change this one variable while keeping the rest close.", Reference = Ref(reference) });
        }

        routes.Add(new AimTrainingRoute
        {
            Title = "Limit-test route",
            Instruction = "Keep the difficult version in a small dose after main volume. Use it to measure capacity, not as your default training difficulty.",
            Why = "Limit attempts are useful evidence, but repeated breakdown reps should not dominate the session.",
            Reference = $"Target map currently sits at {target.Proficiency:0} proficiency ({Humanize.ProductionScoreShort(target.Proficiency)})."
        });
        return routes.Take(4).ToList();
    }

    private static double SimilarityDistance(PlayRow a, PlayRow b)
    {
        double star = Math.Abs(a.StarRating-b.StarRating)/.8;
        double bpm = Math.Abs(a.MeanBpm-b.MeanBpm)/Math.Max(25, Math.Max(a.MeanBpm,b.MeanBpm)*.12);
        double spacing = Math.Abs(a.SpacingP75-b.SpacingP75)/Math.Max(40, Math.Max(a.SpacingP75,b.SpacingP75)*.16);
        double ar = Math.Abs(a.EffectiveAr-b.EffectiveAr)/.7;
        return Math.Sqrt(star*star+bpm*bpm+spacing*spacing+ar*ar);
    }

    private static string DescribePlayRange(IReadOnlyList<PlayRow> plays)
    {
        if (plays.Count == 0) return "your current category baseline";
        string bpm = Range(plays.Select(p => p.MeanBpm), v => $"{v:0}");
        string spacing = Range(plays.Select(p => p.SpacingP75), v => $"{v:0}px");
        string star = Range(plays.Where(p => p.StarRating > 0).Select(p => p.StarRating), v => $"{v:0.00}★");
        string ar = Range(plays.Where(p => p.EffectiveAr > 0).Select(p => p.EffectiveAr), v => $"AR {v:0.0}");
        return $"{star}, {bpm} BPM, {spacing}, {ar}";
    }

    private static string Range(IEnumerable<double> values, Func<double, string> format)
    {
        var a = values.Where(Usable).OrderBy(x => x).ToArray();
        if (a.Length == 0) return "—";
        double lo = Percentile(a, .25), hi = Percentile(a, .75);
        if (Math.Abs(hi - lo) < 1e-6) return format(lo);
        return $"{format(lo)}–{format(hi)}";
    }

    private static List<Row> BuildRows(LifetimeAimAnalysisData data)
    {
        var playById = data.Samples.Select(x => x.Play).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<Row>(data.Transitions.Count);
        foreach (var t in data.Transitions)
            if (playById.TryGetValue(t.PlayId, out var play)) rows.Add(ToRow(t, play));
        return rows;
    }

    private static Row ToRow(TransitionMetric t, PlayRow play) => new(
        t, play, AimErrorDiagnostics.Diagnose(t).Cause,
        string.IsNullOrWhiteSpace(t.ErrorClass) ? "Plain error" : t.ErrorClass);

    private static bool Selected(Row row, string selectionKey)
    {
        bool direction = selectionKey.StartsWith("direction:", StringComparison.OrdinalIgnoreCase);
        string label = selectionKey.Contains(':') ? selectionKey[(selectionKey.IndexOf(':') + 1)..] : selectionKey;
        return direction ? row.Direction.Equals(label, StringComparison.OrdinalIgnoreCase)
                         : row.Cause.Equals(label, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Usable(double value) => double.IsFinite(value) && value > 0;

    private static double Median(IEnumerable<double> values) => Percentile(values, .5);

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var a = values.Where(Usable).OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        double pos = (a.Length - 1) * Math.Clamp(p, 0, 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return a[lo];
        double f = pos - lo;
        return a[lo] + (a[hi] - a[lo]) * f;
    }
}
