namespace OsuAimAnalyzer;

public sealed class DiagnosticsEngine
{
    private readonly AnalyzerDatabase database;
    public DiagnosticsEngine(AnalyzerDatabase database) => this.database = database;

    private sealed record Sample(PlayRow Play, TransitionMetric T);
    private sealed record Band(string Name, double Min, double Max)
    {
        public bool Contains(double x) => x >= Min && x < Max;
    }

    public static readonly string[] PresetNames =
    {
        "All aim",
        "NM farm · jump aim",
        "DT farm · jump aim",
        "High-AR speed aim",
        "Wide low-BPM jump aim",
        "Dense farm / reading aim",
        "Fast compact / medium aim",
        "Cross-screen jump aim",
        "HR precision jump aim"
    };

    public static string PresetDescription(string name) => name switch
    {
        "NM farm · jump aim" => "No rate/difficulty/visibility mods; roughly AR 8.7–9.6, 160–229 BPM, and 130–239 px spacing. Conventional NM jump-farm aim.",
        "DT farm · jump aim" => "DT/NC plays around 220–329 BPM, effective AR 9.5+, and 110–249 px spacing. Fast farm snapping under short preview.",
        "High-AR speed aim" => "Effective AR 9.7+, 240+ BPM, and 100–239 px spacing. Fast decisions and fast cursor transfers under short preview.",
        "Wide low-BPM jump aim" => "120–219 BPM with 200+ px jumps. Large cursor travel and braking matter more than raw BPM.",
        "Dense farm / reading aim" => "At least ~4 visible objects, 170–299 BPM, and 100–229 px spacing. Visual load plus real aim demand.",
        "Fast compact / medium aim" => "260+ BPM with 80–179 px jumps. Fast, smaller-amplitude aim where timing and tension often matter more than reach.",
        "Cross-screen jump aim" => "260+ px CS4-normalized jumps. Very large cursor travel regardless of whether the map is conventionally farmy.",
        "HR precision jump aim" => "Hard Rock plays with effective AR 9.5+ and 90–219 px spacing. Smaller circles and reduced landing margin dominate.",
        _ => "All analyzed aim transitions in the selected history window. Use the cohorts to find weaknesses by BPM, spacing, density, AR and star rating."
    };

    public static string PresetGuideline(string name) => name switch
    {
        "NM farm · jump aim" => "Simple guideline: start with roughly 5.2–6.5★ NM maps, 160–230 BPM, AR 8.7–9.6, and medium-to-wide jumps (~1.8–3.2 CS4 circle diameters).",
        "DT farm · jump aim" => "Simple guideline: use DT/NC maps whose modded difficulty is roughly 5.8–7.5★, 220–330 BPM, AR 9.5+, and ~1.5–3.4 CS4 circle diameters. Keep most volume where control stays 725–900/1000.",
        "High-AR speed aim" => "Simple guideline: prioritize AR 9.7+, 240+ BPM maps with moderate spacing. Increase BPM or AR before adding huge jumps so you can tell whether the limiter is reading/tension or raw reach.",
        "Wide low-BPM jump aim" => "Simple guideline: 120–220 BPM, 200+ px spacing (~2.7+ CS4 circles). Choose stars that keep landing and braking controlled; do not chase star rating by adding unrelated stream difficulty.",
        "Dense farm / reading aim" => "Simple guideline: target ~4–6 visible objects, 170–300 BPM and medium/wide spacing. Favor maps whose difficulty comes from dense visible patterns rather than pure stream stamina.",
        "Fast compact / medium aim" => "Simple guideline: 260+ BPM with ~80–180 px jumps (~1.1–2.5 circles). Keep the hand relaxed; if tension rises before misses do, lower AR/BPM slightly and rebuild speed cleanly.",
        "Cross-screen jump aim" => "Simple guideline: use 260+ px jumps (~3.6+ circles) at a BPM where braking remains controlled. Increase BPM only after the cursor stops bouncing off targets.",
        "HR precision jump aim" => "Simple guideline: HR maps around AR 9.5+ with 90–220 px spacing. Prefer maps where the smaller circle size is the main difficulty rather than speed/stamina confounds.",
        _ => "Simple guideline: keep most training in the challenging-to-controlled range (roughly 725–900 control), with a smaller amount of mastered warm-up and very little breakdown-volume."
    };

    private static readonly Band[] BpmBands =
    {
        new("Low BPM", 0, 200), new("Mid BPM", 200, 240), new("High BPM", 240, 280), new("Very high BPM", 280, double.PositiveInfinity)
    };
    private static readonly Band[] SpacingBands =
    {
        new("Compact/short spacing", 0, 140), new("Medium spacing", 140, 180), new("Wide spacing", 180, 220), new("Very wide spacing", 220, double.PositiveInfinity)
    };
    private static readonly Band[] DensityBands =
    {
        new("Low density", 0, 2.5), new("Medium density", 2.5, 3.5), new("High density", 3.5, 4.5), new("Very high density", 4.5, double.PositiveInfinity)
    };
    private static readonly Band[] ArBands =
    {
        new("Lower AR", 0, 9.0), new("High AR", 9.0, 9.6), new("Very high AR", 9.6, 10.0), new("AR10+", 10.0, double.PositiveInfinity)
    };
    private static readonly Band[] StarBands =
    {
        new("<5.5★", 0, 5.5), new("5.5–6.4★", 5.5, 6.5), new("6.5–7.4★", 6.5, 7.5), new("7.5★+", 7.5, double.PositiveInfinity)
    };

    public List<DiagnosticCohort> BuildCohorts(IEnumerable<PlayRow> source, int minSamples = 40, string preset = "All aim")
    {
        var plays = source.ToList();
        var playById = plays.ToDictionary(p => p.Id);
        var allSamples = database.LoadTransitions(plays.Select(p => p.Id))
            .Where(t => playById.ContainsKey(t.PlayId))
            .Select(t => new Sample(playById[t.PlayId], t))
            .ToList();
        if (allSamples.Count == 0) return new();

        var baseline = ComponentAverages(allSamples);
        double baselineTension = allSamples.Average(TensionForSample);
        var samples = allSamples.Where(s => MatchesPreset(s.Play, s.T, preset)).ToList();
        if (samples.Count == 0) return new();

        var output = new List<DiagnosticCohort>();
        if (samples.Count >= Math.Max(10, minSamples / 2))
            output.Add(MakeCohort(samples, preset == "All aim" ? "Overall selected aim" : $"Preset overview · {preset}", baseline, baselineTension));

        AddSingleDimension(output, samples, BpmBands, s => s.T.Bpm, minSamples, baseline, baselineTension);
        AddSingleDimension(output, samples, SpacingBands, s => s.T.NormalizedSpacing, minSamples, baseline, baselineTension);
        AddSingleDimension(output, samples, DensityBands, s => s.T.Density, minSamples, baseline, baselineTension);
        AddSingleDimension(output, samples, ArBands, s => s.Play.EffectiveAr, minSamples, baseline, baselineTension);
        AddSingleDimension(output, samples, StarBands, s => s.Play.StarRating, minSamples, baseline, baselineTension);

        foreach (var bpm in BpmBands)
        foreach (var spacing in SpacingBands)
            AddIfUseful(output, samples.Where(s => bpm.Contains(s.T.Bpm) && spacing.Contains(s.T.NormalizedSpacing)).ToList(), $"{bpm.Name} · {spacing.Name}", minSamples, baseline, baselineTension);

        foreach (var bpm in BpmBands.Skip(1))
        foreach (var ar in ArBands.Skip(1))
        foreach (var spacing in SpacingBands.Skip(1))
            AddIfUseful(output, samples.Where(s => bpm.Contains(s.T.Bpm) && ar.Contains(s.Play.EffectiveAr) && spacing.Contains(s.T.NormalizedSpacing)).ToList(), $"{bpm.Name} · {ar.Name} · {spacing.Name}", Math.Max(minSamples, 30), baseline, baselineTension);

        foreach (var density in DensityBands.Skip(1))
        foreach (var spacing in SpacingBands.Skip(1))
            AddIfUseful(output, samples.Where(s => density.Contains(s.T.Density) && spacing.Contains(s.T.NormalizedSpacing)).ToList(), $"{density.Name} · {spacing.Name}", minSamples, baseline, baselineTension);

        return output
            .OrderBy(c => c.Label.StartsWith("Preset overview", StringComparison.OrdinalIgnoreCase) || c.Label == "Overall selected aim" ? 0 : 1)
            .ThenBy(c => c.Proficiency)
            .ThenByDescending(c => c.Count)
            .ToList();
    }

    public static bool MatchesPreset(PlayRow play, TransitionMetric t, string preset)
    {
        const int visibilityOrDifficultyMods = ModUtils.Easy | ModUtils.Hidden | ModUtils.HardRock | ModUtils.DoubleTime | ModUtils.HalfTime | ModUtils.Nightcore | ModUtils.Flashlight;
        return preset switch
        {
            "NM farm · jump aim" => (play.Mods & visibilityOrDifficultyMods) == 0 && t.Bpm >= 160 && t.Bpm < 230 && play.EffectiveAr >= 8.7 && play.EffectiveAr < 9.7 && t.NormalizedSpacing >= 130 && t.NormalizedSpacing < 240 && t.Density < 4.8,
            "DT farm · jump aim" => (play.Mods & (ModUtils.DoubleTime | ModUtils.Nightcore)) != 0 && t.Bpm >= 220 && t.Bpm < 330 && play.EffectiveAr >= 9.5 && t.NormalizedSpacing >= 110 && t.NormalizedSpacing < 250 && t.Density < 5.5,
            "High-AR speed aim" => play.EffectiveAr >= 9.7 && t.Bpm >= 240 && t.NormalizedSpacing >= 100 && t.NormalizedSpacing < 240,
            "Wide low-BPM jump aim" => t.Bpm >= 120 && t.Bpm < 220 && t.NormalizedSpacing >= 200,
            "Dense farm / reading aim" => t.Density >= 4.0 && t.Bpm >= 170 && t.Bpm < 300 && t.NormalizedSpacing >= 100 && t.NormalizedSpacing < 230,
            "Fast compact / medium aim" => t.Bpm >= 260 && t.NormalizedSpacing >= 80 && t.NormalizedSpacing < 180,
            "Cross-screen jump aim" => t.NormalizedSpacing >= 260,
            "HR precision jump aim" => (play.Mods & ModUtils.HardRock) != 0 && play.EffectiveAr >= 9.5 && t.NormalizedSpacing >= 90 && t.NormalizedSpacing < 220,
            _ => true
        };
    }

    public static bool PlayMatchesPreset(PlayRow p, string preset)
    {
        var fake = new TransitionMetric { Bpm = p.MeanBpm, NormalizedSpacing = p.SpacingP75, Density = p.DensityP90 };
        return MatchesPreset(p, fake, preset);
    }

    private static double TensionForSample(Sample s) => ScoringConfig.InferredAimTension(
        s.T.Stability, s.T.Deceleration, s.T.Straightness, s.T.Landing, s.T.ErrorClass,
        s.T.Bpm, s.Play.EffectiveAr, s.T.NormalizedSpacing, s.T.Density);

    private static void AddSingleDimension(List<DiagnosticCohort> output, List<Sample> samples, Band[] bands,
        Func<Sample,double> selector, int minSamples, Dictionary<string,double> baseline, double baselineTension)
    {
        foreach (var band in bands)
            AddIfUseful(output, samples.Where(s => band.Contains(selector(s))).ToList(), band.Name, minSamples, baseline, baselineTension);
    }

    private static void AddIfUseful(List<DiagnosticCohort> output, List<Sample> group, string label, int minSamples, Dictionary<string,double> baseline, double baselineTension)
    {
        if (group.Count < minSamples) return;
        output.Add(MakeCohort(group, label, baseline, baselineTension));
    }

    private static DiagnosticCohort MakeCohort(List<Sample> g, string label, Dictionary<string,double> baseline, double baselineTension)
    {
        var vals = ComponentAverages(g);
        var deficits = vals.ToDictionary(kv => kv.Key, kv => kv.Value - baseline[kv.Key]);
        var weakestRelative = deficits.OrderBy(kv => kv.Value).First();
        double prof = g.Average(x => x.T.Proficiency);
        double weakScore = vals[weakestRelative.Key];
        double weakBase = baseline[weakestRelative.Key];
        double tension = g.Average(TensionForSample);
        double tensionDelta = tension - baselineTension;

        double bpmMid = Median(g.Select(x => x.T.Bpm));
        double spacingMid = Median(g.Select(x => x.T.NormalizedSpacing));
        double densityMid = Median(g.Select(x => x.T.Density));
        double arMid = Median(g.Select(x => x.Play.EffectiveAr));
        double starMid = Median(g.Where(x => x.Play.StarRating > 0).Select(x => x.Play.StarRating));
        double intervalMid = Median(g.Select(x => x.T.IntervalMs));

        string weaknessText = HumanMechanic(weakestRelative.Key);
        string insight;
        if (weakestRelative.Value <= -55)
            insight = $"This map type exposes {weaknessText}. You average {Humanize.Score(weakScore)} here versus {weakBase:0}/1000 normally ({Humanize.Difference(weakScore, weakBase)}). ";
        else if (prof >= 900)
            insight = $"This looks like a strength. Overall control is {Humanize.Score(prof)}, and no single mechanic is much worse than your normal baseline. ";
        else
            insight = $"Overall control here is {Humanize.Score(prof)}. The main limiter is {weaknessText} at {Humanize.Score(weakScore)}. ";

        if (tension >= 45 || tensionDelta >= 8)
            insight += $"Inferred aim tension is {Humanize.Tension(tension)}, {tensionDelta:+0;-0;0} versus baseline. At {bpmMid:0} BPM / AR {arMid:0.0}, shake or late correction is being treated more strictly. ";
        else if (tension <= 25 && prof >= 850)
            insight += $"Aim tension stays low at {Humanize.Tension(tension)}, which is a good sign that you are not forcing the movement. ";

        insight += $"Typical demand: {(starMid > 0 ? $"{starMid:0.00}★, " : "")}{Humanize.Spacing(spacingMid)}, {Humanize.Interval(intervalMid)}, {Humanize.Ar(arMid)}, and {Humanize.Density(densityMid)}.";

        var distinctPlays = g.Select(x => x.Play).DistinctBy(x => x.Id).ToList();
        var stars = distinctPlays.Where(x => x.StarRating > 0).Select(x => x.StarRating).ToList();
        return new DiagnosticCohort
        {
            Label = label,
            Count = g.Count,
            BpmMin = g.Min(x => x.T.Bpm), BpmMax = g.Max(x => x.T.Bpm), BpmMedian = bpmMid,
            SpacingMin = g.Min(x => x.T.NormalizedSpacing), SpacingMax = g.Max(x => x.T.NormalizedSpacing), SpacingMedian = spacingMid,
            DensityMin = g.Min(x => x.T.Density), DensityMax = g.Max(x => x.T.Density), DensityMedian = densityMid,
            StarMin = stars.Count > 0 ? stars.Min() : 0, StarMax = stars.Count > 0 ? stars.Max() : 0, StarMedian = starMid,
            ArMin = g.Min(x => x.Play.EffectiveAr), ArMax = g.Max(x => x.Play.EffectiveAr), ArMedian = arMid,
            IntervalMedian = intervalMid,
            Proficiency = prof,
            Straightness = vals["Straightness"], Landing = vals["Landing"], Arrival = vals["Arrival"], Stability = vals["Stability"], Deceleration = vals["Deceleration"],
            AimTension = tension, TensionBaseline = baselineTension, TensionDelta = tensionDelta,
            Accuracy = distinctPlays.Average(x => x.Accuracy),
            PeakAimRating = distinctPlays.Max(x => x.RawAimRating),
            Weakest = weaknessText,
            WeakestScore = weakScore,
            WeakestBaseline = weakBase,
            WeakestDelta = weakestRelative.Value,
            Insight = insight
        };
    }

    private static Dictionary<string,double> ComponentAverages(IEnumerable<Sample> g)
    {
        var a = g.ToList();
        return new Dictionary<string,double>
        {
            ["Straightness"] = a.Average(x => x.T.Straightness),
            ["Landing"] = a.Average(x => x.T.Landing),
            ["Arrival"] = a.Average(x => x.T.Arrival),
            ["Stability"] = a.Average(x => x.T.Stability),
            ["Deceleration"] = a.Average(x => x.T.Deceleration)
        };
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    public static string HumanMechanic(string key) => key switch
    {
        "Straightness" => "cursor-path straightness",
        "Landing" => "landing accuracy / centering",
        "Arrival" => "arrival timing",
        "Stability" => "post-landing stability (shake)",
        "Deceleration" => "braking / deceleration",
        _ => key.ToLowerInvariant()
    };
}
