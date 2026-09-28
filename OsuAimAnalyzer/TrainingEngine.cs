namespace OsuAimAnalyzer;

public sealed class TrainingEngine
{
    private readonly AnalyzerDatabase database;
    public TrainingEngine(AnalyzerDatabase database) => this.database = database;

    private sealed record Sample(PlayRow Play, TransitionMetric T);
    private sealed record RangeDef(string Label, double Min, double Max)
    {
        public bool Contains(double x) => x >= Min && x < Max;
    }

    public List<TrainingRecommendation> Build(IEnumerable<PlayRow> source, string dimension, string preset = "All aim", int minSamples = 30, int leniency = 5)
    {
        leniency = Math.Clamp(leniency, 1, 10);
        var plays = source.ToList();
        var playById = plays.ToDictionary(p => p.Id);
        var samples = database.LoadTransitions(plays.Select(p => p.Id))
            .Where(t => playById.ContainsKey(t.PlayId))
            .Select(t => new Sample(playById[t.PlayId], t))
            .Where(s => DiagnosticsEngine.MatchesPreset(s.Play, s.T, preset))
            .ToList();
        if (samples.Count == 0) return new();

        RangeDef[] ranges = dimension switch
        {
            "BPM" => new[] { new RangeDef("<200 BPM", 0, 200), new RangeDef("200–239 BPM", 200, 240), new RangeDef("240–279 BPM", 240, 280), new RangeDef("280–319 BPM", 280, 320), new RangeDef("320+ BPM", 320, double.PositiveInfinity) },
            "Density" => new[] { new RangeDef("<2.5 visible objects", 0, 2.5), new RangeDef("2.5–3.4 visible objects", 2.5, 3.5), new RangeDef("3.5–4.4 visible objects", 3.5, 4.5), new RangeDef("4.5+ visible objects", 4.5, double.PositiveInfinity) },
            _ => new[] { new RangeDef("<140 px · compact/short", 0, 140), new RangeDef("140–179 px · medium", 140, 180), new RangeDef("180–219 px · wide", 180, 220), new RangeDef("220–259 px · very wide", 220, 260), new RangeDef("260+ px · cross-screen", 260, double.PositiveInfinity) }
        };

        double Sel(Sample s) => dimension switch { "BPM" => s.T.Bpm, "Density" => s.T.Density, _ => s.T.NormalizedSpacing };
        var output = new List<TrainingRecommendation>();

        foreach (var range in ranges)
        {
            var g = samples.Where(s => range.Contains(Sel(s))).ToList();
            if (g.Count < minSamples) continue;

            double prof = g.Average(x => x.T.Proficiency);
            double tension = g.Average(TensionForSample);
            var components = new Dictionary<string,double>
            {
                ["Straightness"] = g.Average(x => x.T.Straightness), ["Landing"] = g.Average(x => x.T.Landing),
                ["Arrival"] = g.Average(x => x.T.Arrival), ["Stability"] = g.Average(x => x.T.Stability), ["Deceleration"] = g.Average(x => x.T.Deceleration)
            };
            var weakest = components.MinBy(kv => kv.Value);
            string focus = DiagnosticsEngine.HumanMechanic(weakest.Key);

            double lowBand = 735 - (leniency - 5) * 8;
            double highBand = 875 + (leniency - 5) * 7;
            double maxTension = 58 + (leniency - 5) * 2;
            var target = g.Where(x => x.T.Proficiency >= lowBand && x.T.Proficiency <= highBand && TensionForSample(x) <= maxTension).ToList();
            if (target.Count < Math.Min(12, Math.Max(1, g.Count / 6))) target = g;

            double bpm = Median(target.Select(x => x.T.Bpm));
            double spacing = Median(target.Select(x => x.T.NormalizedSpacing));
            double density = Median(target.Select(x => x.T.Density));
            double ar = Median(target.Select(x => x.Play.EffectiveAr));
            double star = Median(target.Where(x => x.Play.StarRating > 0).Select(x => x.Play.StarRating));
            double interval = Median(target.Select(x => x.T.IntervalMs));

            double scale = prof switch { >= 920 => 1.08, >= 870 => 1.04, >= 760 => 1.00, _ => .92 };
            if (tension >= 52) scale = Math.Min(scale, .96);
            if (dimension != "BPM") bpm *= scale;
            if (dimension != "Spacing") spacing *= scale;
            density = Math.Max(1, density + (prof >= 920 && tension < 40 ? .35 : prof < 720 || tension >= 55 ? -.35 : 0));
            ar = Math.Clamp(ar + (prof >= 920 && tension < 40 ? .15 : prof < 720 || tension >= 55 ? -.20 : 0), 0, 11);
            if (star > 0) star += prof >= 920 && tension < 40 ? .20 : prof < 720 || tension >= 55 ? -.20 : 0;

            double width = .035 + (leniency - 1) * .007;
            double bpmLo = Math.Max(1, bpm * (1 - width)), bpmHi = bpm * (1 + width);
            double spacingLo = Math.Max(1, spacing * (1 - width)), spacingHi = spacing * (1 + width);
            double densityWidth = .18 + leniency * .035;
            double densityLo = Math.Max(1, density - densityWidth), densityHi = density + densityWidth;
            double arWidth = .10 + leniency * .025;
            double arLo = Math.Max(0, ar - arWidth), arHi = Math.Min(11, ar + arWidth);
            double starWidth = .18 + leniency * .045;
            double starLo = Math.Max(0, star - starWidth), starHi = star + starWidth;

            string status = tension >= 60 ? "Over-tense — lower demand slightly" : prof switch
            {
                >= 920 => "Too easy now — increase demand",
                >= 850 => "Controlled — add a little difficulty",
                >= 725 => "Good training zone",
                _ => "Too hard — back off"
            };

            string action = $"Aim for {(star > 0 ? $"~{star:0.00}★, " : "")}about {bpm:0} BPM, {Humanize.Spacing(spacing)}, {Humanize.Density(density)}, and {Humanize.Ar(ar)}.";
            string tensionSentence = tension >= 45
                ? $" Inferred aim tension is {Humanize.Tension(tension)} here, so do not add difficulty until shake/late corrections are coming down."
                : $" Inferred aim tension is {Humanize.Tension(tension)}, so tension is not currently the main reason to back off.";
            string rationale = prof switch
            {
                >= 920 => $"You are already {Humanize.Score(prof)} in this range. Increase one secondary demand at a time until control settles around 800–880. Watch {focus} first ({weakest.Value:0}/1000).",
                >= 850 => $"You are {Humanize.Score(prof)} here. A modest overload should be productive. If {focus} falls below roughly 750/1000, you pushed too far.",
                >= 725 => $"This is already near the useful adaptation zone at {Humanize.Score(prof)}. Accumulate volume around these settings instead of immediately making it harder. Main limiter: {focus} ({weakest.Value:0}/1000).",
                _ => $"Movement quality is breaking down at {Humanize.Score(prof)}. Reduce speed, spacing, density, AR, or stars until control returns above roughly 725/1000. Biggest limiter: {focus} ({weakest.Value:0}/1000)."
            } + tensionSentence;

            var examples = BuildReferenceMaps(g, bpm, spacing, density, ar, star, leniency, 3);

            output.Add(new TrainingRecommendation
            {
                Range = range.Label, Status = status, Samples = g.Count, CurrentProficiency = prof,
                TargetBpm = $"{bpmLo:0}–{bpmHi:0} BPM",
                TargetSpacing = $"{spacingLo:0}–{spacingHi:0}px · ~{spacing / Humanize.Cs4CircleDiameter:0.0} circles",
                TargetDensity = $"{densityLo:0.0}–{densityHi:0.0} visible",
                TargetAr = $"AR {arLo:0.0}–{arHi:0.0}",
                TargetStar = star > 0 ? $"{starLo:0.00}–{starHi:0.00}★" : "—",
                Focus = focus,
                TargetBpmMid = bpm, TargetSpacingMid = spacing, TargetDensityMid = density, TargetArMid = ar, TargetStarMid = star,
                TargetSummary = action + $" Historical transitions around this target were typically {Humanize.Interval(interval)}.",
                Rationale = rationale,
                CurrentTension = tension,
                ExampleMaps = examples
            });
        }
        return output;
    }

    public List<TrainingMapExample> BuildMapRecommendations(IEnumerable<PlayRow> source, string preset, int leniency = 5, int maxResults = 40)
    {
        leniency = Math.Clamp(leniency, 1, 10);
        var plays = source.Where(p => DiagnosticsEngine.PlayMatchesPreset(p, preset)).ToList();
        if (plays.Count == 0) return new();
        var playById = plays.ToDictionary(p => p.Id);
        var samples = database.LoadTransitions(plays.Select(p => p.Id))
            .Where(t => playById.ContainsKey(t.PlayId))
            .Select(t => new Sample(playById[t.PlayId], t))
            .Where(s => DiagnosticsEngine.MatchesPreset(s.Play, s.T, preset))
            .ToList();
        if (samples.Count == 0) return new();

        // A recommendation is useful when it is challenging/controlled, not effortless or total breakdown.
        double targetProf = 805;
        double profTolerance = 45 + leniency * 18;
        double tensionTarget = 38;
        double tensionTolerance = 8 + leniency * 4;

        return samples.GroupBy(x => x.Play.Id).Select(g =>
        {
            var p = g.First().Play;
            double control = g.Average(x => x.T.Proficiency);
            double tension = g.Average(TensionForSample);
            double bpm = Median(g.Select(x => x.T.Bpm));
            double spacing = Median(g.Select(x => x.T.NormalizedSpacing));
            double density = Median(g.Select(x => x.T.Density));
            double fit = Math.Abs(control - targetProf) / profTolerance + Math.Max(0, tension - tensionTarget) / tensionTolerance;
            if (p.MissCount > 3) fit += Math.Min(1.2, p.MissCount * .12);
            return new TrainingMapExample
            {
                Map = p.Map, Mods = p.ModsText, Accuracy = p.Accuracy, Proficiency = control, AimPerformance = p.RawAimRating,
                Bpm = bpm, Spacing = spacing, Density = density, Ar = p.EffectiveAr, Star = p.StarRating, FitScore = fit, AimTension = tension
            };
        })
        .OrderBy(x => x.FitScore)
        .ThenByDescending(x => x.Proficiency)
        .GroupBy(x => $"{x.Map}|{x.Mods}", StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .Take(maxResults)
        .ToList();
    }

    private static double TensionForSample(Sample s) => ScoringConfig.InferredAimTension(
        s.T.Stability, s.T.Deceleration, s.T.Straightness, s.T.Landing, s.T.ErrorClass,
        s.T.Bpm, s.Play.EffectiveAr, s.T.NormalizedSpacing, s.T.Density);

    private static List<TrainingMapExample> BuildReferenceMaps(List<Sample> group, double targetBpm, double targetSpacing, double targetDensity, double targetAr, double targetStar, int leniency, int count)
    {
        double loose = 0.75 + leniency * .09;
        var candidates = group.GroupBy(x => x.Play.Id).Select(g =>
        {
            var p = g.First().Play;
            double bpm = Median(g.Select(x => x.T.Bpm));
            double spacing = Median(g.Select(x => x.T.NormalizedSpacing));
            double density = Median(g.Select(x => x.T.Density));
            double ar = p.EffectiveAr;
            double control = g.Average(x => x.T.Proficiency);
            double distance = Math.Abs(bpm - targetBpm) / Math.Max(30, targetBpm * .15)
                            + Math.Abs(spacing - targetSpacing) / Math.Max(40, targetSpacing * .20)
                            + Math.Abs(density - targetDensity) / 1.25
                            + Math.Abs(ar - targetAr) / .8;
            if (targetStar > 0 && p.StarRating > 0) distance += Math.Abs(p.StarRating - targetStar) / loose;
            if (control > 930) distance += (control - 930) / 100.0;
            if (control < 700) distance += (700 - control) / 75.0;
            return new { Play = p, Bpm = bpm, Spacing = spacing, Density = density, Ar = ar, Control = control, Distance = distance, Count = g.Count() };
        })
        .Where(x => x.Count >= 4)
        .OrderBy(x => x.Distance)
        .GroupBy(x => string.IsNullOrWhiteSpace(x.Play.BeatmapHash) ? x.Play.Map : x.Play.BeatmapHash, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .Take(count)
        .ToList();

        return candidates.Select(x => new TrainingMapExample
        {
            Map = x.Play.Map, Mods = x.Play.ModsText, Accuracy = x.Play.Accuracy, Proficiency = x.Control,
            AimPerformance = x.Play.RawAimRating, Bpm = x.Bpm, Spacing = x.Spacing, Density = x.Density, Ar = x.Ar,
            Star = x.Play.StarRating, FitScore = x.Distance, AimTension = group.Where(z => z.Play.Id == x.Play.Id).Average(TensionForSample)
        }).ToList();
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length/2] : (a[a.Length/2-1] + a[a.Length/2]) / 2;
    }
}
