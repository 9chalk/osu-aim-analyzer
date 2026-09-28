namespace OsuAimAnalyzer;

public sealed record PlayInsightSections(string Overview, string Training, string Comparison, string Errors);

public static class PlayInsightBuilder
{
    public static PlayInsightSections BuildSections(PlayRow play, AnalyzerDatabase database, IReadOnlyList<PlayRow>? playHistory = null)
    {
        var transitions = database.LoadTransitions(play.Id);
        var all = playHistory?.ToList() ?? database.LoadPlays();
        bool SameDifficulty(PlayRow p) => !string.IsNullOrWhiteSpace(play.BeatmapHash)
            ? string.Equals(p.BeatmapHash, play.BeatmapHash, StringComparison.OrdinalIgnoreCase)
            : string.Equals(p.Map, play.Map, StringComparison.OrdinalIgnoreCase);

        var previous = all.Where(p => p.Id != play.Id && SameDifficulty(p) && p.TimestampUtc < play.TimestampUtc)
            .OrderByDescending(p => p.TimestampUtc).ToList();
        var sameMods = previous.Where(p => p.Mods == play.Mods).ToList();

        var components = new Dictionary<string, double>
        {
            ["path straightness"] = play.Straightness,
            ["landing / centering"] = play.Landing,
            ["arrival timing"] = play.Arrival,
            ["landing stability"] = play.Stability,
            ["braking / deceleration"] = play.Deceleration
        };
        var weakest = components.MinBy(x => x.Value);
        var strongest = components.MaxBy(x => x.Value);

        var errors = transitions.GroupBy(t => t.ErrorClass).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        int total = Math.Max(1, transitions.Count);
        var rankedErrors = errors.Where(kv => !string.Equals(kv.Key, "Clean", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value).Take(4).ToList();
        string errorText = rankedErrors.Count == 0
            ? "No single error type dominated; most analyzed transitions were classified clean."
            : string.Join(" · ", rankedErrors.Select(kv => $"{kv.Key.ToLowerInvariant()} {100.0 * kv.Value / total:0}%"));

        var causeSummary = AimErrorDiagnostics.Analyze(transitions);
        var landingErrors = transitions.Where(t => double.IsFinite(t.AxialError) && double.IsFinite(t.LateralError)).ToList();
        double avgCenterError = landingErrors.Count == 0 ? 0 : landingErrors.Average(t => Math.Sqrt(t.AxialError * t.AxialError + t.LateralError * t.LateralError));
        double meanAxialBias = landingErrors.Count == 0 ? 0 : landingErrors.Average(t => t.AxialError);
        double meanLateralBias = landingErrors.Count == 0 ? 0 : landingErrors.Average(t => t.LateralError);
        string biasText = Math.Abs(meanAxialBias) < .015 ? "no meaningful axial bias"
            : meanAxialBias > 0 ? $"{meanAxialBias:0.00}R overaim bias" : $"{Math.Abs(meanAxialBias):0.00}R underaim bias";
        if (Math.Abs(meanLateralBias) >= .015) biasText += $" · {Math.Abs(meanLateralBias):0.00}R lateral bias";

        string history;
        if (previous.Count == 0)
        {
            history = "First analyzed run on this exact difficulty. This run becomes the baseline for future comparisons.";
        }
        else
        {
            double avgProf = previous.Average(p => p.Proficiency);
            double avgAim = previous.Average(p => p.RawAimRating);
            double avgAcc = previous.Average(p => p.Accuracy);
            double avgTension = previous.Average(p => p.AimTension);
            var best = previous.MaxBy(p => p.RawAimRating)!;
            var immediate = previous[0];
            history =
                $"Versus the immediately previous attempt: proficiency {Delta(play.Proficiency - immediate.Proficiency, " pts")}, " +
                $"aim performance {Delta(play.RawAimRating - immediate.RawAimRating, "")}, accuracy {Delta(play.Accuracy - immediate.Accuracy, "%")}, " +
                $"tension {Humanize.TensionDifference(play.AimTension, immediate.AimTension)}.\r\n\r\n" +
                $"Versus all {previous.Count} earlier run{(previous.Count == 1 ? "" : "s")}: proficiency {Delta(play.Proficiency - avgProf, " pts")}, " +
                $"aim performance {Delta(play.RawAimRating - avgAim, "")}, accuracy {Delta(play.Accuracy - avgAcc, "%")}, and tension is {Humanize.TensionDifference(play.AimTension, avgTension)}.\r\n" +
                $"Previous best: {best.RawAimRating:0} aim performance / {Humanize.ProductionScoreShort(best.Proficiency)}. " +
                $"{sameMods.Count} prior run{(sameMods.Count == 1 ? "" : "s")} used the same mods ({play.ModsText}).";
        }

        string pp = play.PpEstimate > 0 ? $" · ~{play.PpEstimate:0} pp est." : "";
        string overview =
            $"{play.Grade} · {play.Accuracy:0.00}% · {play.MissCount} miss{(play.MissCount == 1 ? "" : "es")}\r\n" +
            $"Proficiency: {Humanize.ProductionScore(play.Proficiency)} · aim performance: {play.RawAimRating:0}{pp}\r\n" +
            $"Zone: {play.Zone} — {Humanize.ZoneExplanation(play.Zone)}\r\n\r\n" +
            $"DEMAND\r\n{play.StarRating:0.00}★ · {Humanize.Ar(play.EffectiveAr)} · {play.MeanBpm:0} BPM\r\n" +
            $"Spacing: {Humanize.Spacing(play.SpacingP75)} · {Humanize.Density(play.DensityP90)}\r\n\r\n" +
            $"Best mechanic: {strongest.Key} at {Humanize.Score(strongest.Value)}.\r\n" +
            $"Weakest mechanic: {weakest.Key} at {Humanize.Score(weakest.Value)}.\r\n" +
            $"Ideal computer-path match: {Humanize.Score(play.IdealPathMatch)}.";

        string training =
            $"{TrainingAssessment(play)}\r\n\r\n" +
            $"Aim tension: {Humanize.Tension(play.AimTension)}.";

        string comparison = history;

        string causeText = causeSummary.PrimaryCause == AimErrorDiagnostics.Clean
            ? "No recurring movement cause dominates this run."
            : $"Most likely movement cause: {causeSummary.PrimaryCause.ToLowerInvariant()} ({causeSummary.PrimaryShare:0}% of diagnosed problem jumps). {AimErrorDiagnostics.CauseDescription(causeSummary.PrimaryCause)}";
        string streakText = causeSummary.LongestStreak is { Count: >= 3 } streak
            ? $"\r\nRepeated pattern: {streak.Cause.ToLowerInvariant()} ×{streak.Count} in {streak.Location}. {AimErrorDiagnostics.RepeatedMeaning(streak.Cause)}"
            : "";

        string errorsText =
            $"WHY CONTROL BROKE\r\n{causeText}{streakText}\r\n\r\n" +
            $"WHERE THE CURSOR ENDED\r\nAverage center error: {avgCenterError:0.00}R from target center · mean bias: {biasText}.\r\n" +
            $"Direction mix: {errorText}.\r\n\r\n" +
            $"Cause is inferred from the stored landing, arrival, stability, braking, straightness, ideal-path and signed-error telemetry. Generate top errors reconstructs the actual cursor paths; Advanced diagnostics shows the likely cause object by object.";

        return new PlayInsightSections(overview, training, comparison, errorsText);
    }

    public static string Build(PlayRow play, AnalyzerDatabase database)
    {
        var s = BuildSections(play, database);
        return $"HOW THIS RUN WENT\r\n{s.Overview}\r\n\r\nTRAINING\r\n{s.Training}\r\n\r\nVS YOUR OTHER RUNS\r\n{s.Comparison}\r\n\r\nERRORS\r\n{s.Errors}";
    }

    public static string TrainingAssessment(PlayRow play)
    {
        bool highTension = play.AimTension >= 58;
        bool elevatedTension = play.AimTension >= 42;
        return play.Zone switch
        {
            "Mastered" => "Useful for warm-up, consistency, or low-stress volume. It is probably below your best main-overload range unless you are specifically polishing execution.",
            "Controlled" when !elevatedTension => "Very good main-volume training map. You are controlling the demand cleanly enough to accumulate repetitions while still having room to push.",
            "Controlled" => "Good training map, but your replay shows elevated tension. Keep it in the session if you can stay relaxed; don't automatically raise difficulty yet.",
            "Challenging" when !elevatedTension => "Good overload training map. Errors are appearing, but movement quality is still intact enough for productive practice.",
            "Challenging" when !highTension => "Useful overload map, but tension is becoming a limiter. Use moderate volume and prioritize clean repetitions over forcing passes.",
            "Challenging" => "Borderline for main volume: the difficulty is trainable, but over-tension is high enough that too much volume may reinforce messy movement.",
            _ when play.Accuracy >= 95 && !highTension => "Hard limit-testing map. Your score may still look decent, but cursor control is breaking down; use sparingly rather than as main volume.",
            _ => "Probably too hard for main training volume right now. Keep it for occasional limit testing and spend most repetitions one step easier."
        };
    }

    private static string Delta(double d, string suffix)
    {
        if (Math.Abs(d) < .05) return "about the same";
        return d > 0 ? $"+{d:0.0}{suffix}" : $"{d:0.0}{suffix}";
    }
}
