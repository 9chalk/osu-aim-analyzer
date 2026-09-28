namespace OsuAimAnalyzer;

public static class Humanize
{
    public static readonly double Cs4CircleDiameter = AimAnalyzer.CircleRadius(4) * 2.0;

    public static string Spacing(double px, bool compact = false)
    {
        double circles = px / Cs4CircleDiameter;
        double widthPct = 100.0 * px / 512.0;
        if (compact) return $"{px:0}px · {circles:0.0} circles";
        return $"{px:0}px ({circles:0.0} CS4 circle diameters; {widthPct:0}% of playfield width)";
    }

    public static string SpacingRange(double min, double max)
    {
        double mid = (min + max) / 2.0;
        return $"{min:0}–{max:0}px (about {mid / Cs4CircleDiameter:0.0} circle diameters at the midpoint)";
    }

    public static string SpacingFeel(double px) => px switch
    {
        < 110 => "compact",
        < 150 => "short",
        < 190 => "medium",
        < 230 => "wide",
        < 280 => "very wide",
        _ => "cross-screen / extreme"
    };

    public static string Density(double d)
    {
        int lo = Math.Max(1, (int)Math.Floor(d));
        int hi = Math.Max(lo, (int)Math.Ceiling(d));
        string visible = lo == hi ? $"about {lo} object{(lo == 1 ? "" : "s")}" : $"about {lo}–{hi} objects";
        return $"{d:0.0} visible ({visible} on screen at once)";
    }

    public static string Interval(double ms)
    {
        if (ms <= 0 || !double.IsFinite(ms)) return "unknown interval";
        return $"{ms:0} ms between objects ({1000.0 / ms:0.0} aim movements/sec)";
    }

    public static string Ar(double ar) => ar switch
    {
        >= 10.3 => $"AR {ar:0.0} (extremely little preview)",
        >= 9.7 => $"AR {ar:0.0} (very little preview)",
        >= 9.0 => $"AR {ar:0.0} (high AR)",
        >= 8.0 => $"AR {ar:0.0} (moderate preview)",
        _ => $"AR {ar:0.0} (longer preview)"
    };

    // Production scores use the tuner v9 resolution curve. Move the old semantic tier
    // boundaries through that same remap so labels retain their original mechanical meaning.
    public static string ProficiencyTier(double score) => LegacyProficiencyTier(score);

    public static string LegacyProficiencyTier(double score) => score switch
    {
        >= 975 => "near-perfect",
        >= 950 => "exceptional",
        >= 925 => "excellent",
        >= 900 => "very strong",
        >= 875 => "strong",
        >= 850 => "controlled",
        >= 825 => "solid",
        >= 800 => "developing",
        >= 775 => "strained",
        >= 750 => "inconsistent",
        >= 725 => "unstable",
        >= 700 => "near breakdown",
        _ => "breakdown"
    };

    public static string TunedProficiencyTier(double score, TuningConfig config)
    {
        var thresholds = new (double Raw, string Name)[]
        {
            (975, "near-perfect"), (950, "exceptional"), (925, "excellent"), (900, "very strong"),
            (875, "strong"), (850, "controlled"), (825, "solid"), (800, "developing"),
            (775, "strained"), (750, "inconsistent"), (725, "unstable"), (700, "near breakdown")
        };
        foreach (var t in thresholds)
            if (score >= TuningScorer.RemapProficiency(t.Raw, config)) return t.Name;
        return "breakdown";
    }

    public static string Score(double score) => $"{score:0}/1000 ({ProficiencyTier(score)})";
    public static string ScoreShort(double score) => $"{score:0} · {ProficiencyTier(score)}";
    public static string LegacyScore(double score) => Score(score);
    public static string LegacyScoreShort(double score) => ScoreShort(score);
    public static string ProductionProficiencyTier(double score) => TunedProficiencyTier(score, ProductionScoring.Profile);
    public static string ProductionScore(double score) => $"{score:0}/1000 ({ProductionProficiencyTier(score)})";
    public static string ProductionScoreShort(double score) => $"{score:0} · {ProductionProficiencyTier(score)}";
    public static string TunedScore(double score, TuningConfig config) => $"{score:0}/1000 ({TunedProficiencyTier(score, config)})";
    public static string TunedScoreShort(double score, TuningConfig config) => $"{score:0} · {TunedProficiencyTier(score, config)}";


    // Radar/capability scores are intentionally uncapped. 1000 is a reference level,
    // unlike Proficiency where 1000 means essentially perfect execution.
    public static string CapabilityTier(double score) => score switch
    {
        >= 1400 => "extreme",
        >= 1200 => "exceptional",
        >= 1050 => "elite-range",
        >= 950 => "advanced+",
        >= 850 => "advanced",
        >= 750 => "strong",
        >= 650 => "solid",
        >= 525 => "developing",
        _ => "foundation"
    };

    public static string CapabilityScore(double score) => $"{score:0} · {CapabilityTier(score)}";

    public static string Difference(double current, double baseline)
    {
        double delta = current - baseline;
        double pct = baseline <= 0 ? 0 : 100.0 * delta / baseline;
        return $"{delta:+0;-0;0} points ({pct:+0;-0;0}%) vs your baseline";
    }

    public static string PlayDifference(double delta, string unit = "")
        => Math.Abs(delta) < .05 ? "about the same" : delta > 0 ? $"{delta:0.0}{unit} points higher" : $"{Math.Abs(delta):0.0}{unit} points lower";


    private static string TensionWord(double tension) => tension switch
    {
        < 14 => "relaxed",
        < 27 => "normal",
        < 42 => "elevated",
        < 58 => "tense",
        _ => "over-tense"
    };

    public static string Tension(double tension) => $"{tension:0}/100 ({TensionWord(tension)}; inferred from replay movement)";
    public static string TensionShort(double tension) => $"{tension:0} · {TensionWord(tension)}";

    public static string TensionDifference(double current, double baseline)
    {
        double d = current - baseline;
        if (Math.Abs(d) < 1.5) return "about the same";
        return d < 0 ? $"{Math.Abs(d):0} lower (more relaxed)" : $"{d:0} higher (more tense)";
    }

    public static string ZoneExplanation(string zone) => zone switch
    {
        "Mastered" => "movement is consistently clean at this demand",
        "Controlled" => "you are in control, with room to add difficulty",
        "Challenging" => "good training difficulty: errors are appearing, but mechanics are still intact",
        "Breakdown" => "the demand is high enough that movement quality is collapsing",
        _ => ""
    };
}
