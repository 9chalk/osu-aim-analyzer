namespace OsuAimAnalyzer;

public sealed record AimCauseDiagnosis(
    TransitionMetric Metric,
    string Cause,
    double Confidence,
    double Severity,
    string Explanation);

public sealed record AimCauseStreak(string Cause, int Count, int StartObject, int EndObject, long StartTimeMs, long EndTimeMs);

public sealed class AimCauseSummary
{
    public List<AimCauseDiagnosis> Diagnoses { get; init; } = new();
    public Dictionary<string, int> CauseCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int ProblemCount { get; init; }
    public int TotalCount { get; init; }
    public string PrimaryCause { get; init; } = "Clean control";
    public string PrimaryExplanation { get; init; } = "No recurring movement failure stands out.";
    public double PrimaryShare { get; init; }
    public AimCauseStreak? LongestStreak { get; init; }

    public double ProblemRate => TotalCount <= 0 ? 0 : 100.0 * ProblemCount / TotalCount;
}

/// <summary>
/// Interprets the analyzer's stored trajectory-derived mechanics as likely movement causes.
/// This is deliberately a diagnosis layer, not a scoring layer: it does not alter proficiency.
/// Direction (under/over/lateral) stays separate from cause (shake-off, braking, timing, etc.).
/// </summary>
public static class AimErrorDiagnostics
{
    public const string Clean = "Clean control";
    public const string ShakeOff = "Shake-off";
    public const string BrakingOvershoot = "Braking overshoot";
    public const string StoppedShort = "Stopped short";
    public const string LateAcquisition = "Late acquisition";
    public const string CorrectionLoop = "Correction loop";
    public const string CurvedApproach = "Curved approach";
    public const string LateralDrift = "Lateral drift";
    public const string BrakingFailure = "Unstable braking";
    public const string GeneralImprecision = "General imprecision";

    public static readonly string[] OrderedCauses =
    {
        ShakeOff, BrakingOvershoot, StoppedShort, LateAcquisition, CorrectionLoop,
        CurvedApproach, LateralDrift, BrakingFailure, GeneralImprecision
    };

    public static AimCauseDiagnosis Diagnose(TransitionMetric t)
    {
        double radial = Math.Sqrt(t.AxialError * t.AxialError + t.LateralError * t.LateralError);
        if (!double.IsFinite(radial)) radial = 0;

        double landingBad = Bad(t.Landing, 800, 500);
        double arrivalBad = Bad(t.Arrival, 760, 520);
        double stabilityBad = Bad(t.Stability, 780, 520);
        double brakingBad = Bad(t.Deceleration, 760, 520);
        double straightBad = Bad(t.Straightness, 760, 520);
        double idealBad = Bad(ScoringConfig.EffectiveIdealPathMatch(t), 760, 520);
        double arrivalGood = Good(t.Arrival, 610, 900);
        double errorPressure = Math.Clamp((radial - .16) / .84, 0, 1);
        double axialOver = Math.Clamp((t.AxialError - .12) / .78, 0, 1);
        double axialUnder = Math.Clamp((-t.AxialError - .12) / .78, 0, 1);
        double lateral = Math.Clamp((Math.Abs(t.LateralError) - .14) / .72, 0, 1);
        bool correction = string.Equals(t.ErrorClass, "Correction", StringComparison.OrdinalIgnoreCase);

        // Do not diagnose normal tiny variation as a failure just because one component is middling.
        double overallProblem = new[] { landingBad, arrivalBad, stabilityBad, brakingBad, straightBad, idealBad, errorPressure }.Max();
        if (overallProblem < .18 && string.Equals(t.ErrorClass, "Clean", StringComparison.OrdinalIgnoreCase))
            return CleanDiagnosis(t);

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Reached/acquired the target in time, but control degraded during the final settling window.
        // This is the useful "I was on it, then shook off" case the old direction labels cannot express.
        scores[ShakeOff] = arrivalGood * stabilityBad * Math.Max(errorPressure, landingBad);
        if (t.Arrival >= 700 && t.Stability <= 620 && (radial >= .28 || t.Landing <= 720))
            scores[ShakeOff] += .38;

        scores[BrakingOvershoot] = axialOver * (.38 + .62 * brakingBad) * (.45 + .55 * Math.Max(errorPressure, landingBad));
        scores[StoppedShort] = axialUnder * (.42 + .38 * arrivalBad + .20 * brakingBad) * (.45 + .55 * Math.Max(errorPressure, landingBad));
        scores[LateAcquisition] = arrivalBad * (.36 + .64 * Math.Max(errorPressure, landingBad)) * (.72 + .28 * (1 - stabilityBad));
        scores[CorrectionLoop] = (correction ? 1.0 : 0.0) * (.38 + .34 * stabilityBad + .28 * brakingBad);
        scores[CurvedApproach] = straightBad * (.48 + .52 * idealBad) * (.34 + .66 * Math.Max(errorPressure, landingBad));
        scores[LateralDrift] = lateral * (.38 + .32 * straightBad + .30 * stabilityBad) * (.55 + .45 * Math.Max(errorPressure, landingBad));
        scores[BrakingFailure] = brakingBad * (.34 + .66 * Math.Max(errorPressure, landingBad)) * (1 - .38 * Math.Max(axialOver, axialUnder));
        double directionalSignature = Math.Max(Math.Max(axialOver, axialUnder), Math.Max(lateral, correction ? 1.0 : 0.0));
        scores[GeneralImprecision] = landingBad * (.42 + .58 * Math.Max(errorPressure, .25))
            * (1 - .30 * Math.Max(stabilityBad, arrivalBad))
            * (1 - .62 * directionalSignature);

        var ranked = scores.OrderByDescending(x => x.Value).ToList();
        var top = ranked[0];
        double second = ranked.Count > 1 ? ranked[1].Value : 0;

        if (top.Value < .20)
            return CleanDiagnosis(t);

        double severity = Math.Clamp(.48 * overallProblem + .32 * errorPressure + .20 * Math.Clamp(top.Value, 0, 1), 0, 1);
        double confidence = Math.Clamp(.48 + .34 * Math.Min(1, top.Value) + .24 * Math.Clamp(top.Value - second, 0, 1), .45, .96);
        return new AimCauseDiagnosis(t, top.Key, confidence, severity, Explain(top.Key, t, radial));
    }

    public static AimCauseSummary Analyze(IEnumerable<TransitionMetric>? transitions)
    {
        var ordered = (transitions ?? Enumerable.Empty<TransitionMetric>()).OrderBy(t => t.TimeMs).ToList();
        var diagnoses = ordered.Select(Diagnose).ToList();
        var problems = diagnoses.Where(d => !d.Cause.Equals(Clean, StringComparison.OrdinalIgnoreCase)).ToList();
        var counts = problems.GroupBy(d => d.Cause, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        string primary = Clean;
        double share = 0;
        string explanation = "No recurring movement failure stands out. The analyzed jumps are mostly controlled.";
        if (problems.Count > 0)
        {
            var top = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
            primary = top.Key;
            share = 100.0 * top.Value / problems.Count;
            var example = problems.Where(d => d.Cause.Equals(primary, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Confidence * (.6 + .4 * d.Severity)).First();
            explanation = CauseDescription(primary) + " " + example.Explanation;
        }

        AimCauseStreak? longest = FindLongestStreak(diagnoses);
        return new AimCauseSummary
        {
            Diagnoses = diagnoses,
            CauseCounts = counts,
            ProblemCount = problems.Count,
            TotalCount = diagnoses.Count,
            PrimaryCause = primary,
            PrimaryExplanation = explanation,
            PrimaryShare = share,
            LongestStreak = longest
        };
    }

    public static string CauseDescription(string cause) => cause switch
    {
        ShakeOff => "You are getting onto the object, but losing the cursor during the final settle before the hit.",
        BrakingOvershoot => "The cursor carries too much speed through the target and finishes beyond center.",
        StoppedShort => "The movement runs out of travel before reaching the target center.",
        LateAcquisition => "The cursor reaches a controlled target position too late in the object window.",
        CorrectionLoop => "The cursor crosses the target axis and has to reverse/correct again near the hit.",
        CurvedApproach => "The approach path bends or wanders instead of taking a direct efficient route.",
        LateralDrift => "The cursor is displaced sideways relative to the incoming jump direction.",
        BrakingFailure => "The final slowdown is unstable, with excess terminal speed or re-acceleration near the target.",
        GeneralImprecision => "The cursor finishes away from center without one stronger timing, stability, path, or braking signature.",
        _ => "The movement is controlled without a strong recurring failure signature."
    };

    public static string RepeatedMeaning(string cause) => cause switch
    {
        ShakeOff => "Repeated shake-off usually means target acquisition is keeping up, but settling control is failing under sustained demand. Lowering grip/tension or slightly reducing speed can separate this from raw aim limitation.",
        BrakingOvershoot => "Repeated overshoot points to speed control: you can cover the distance, but are not shedding velocity early enough before successive targets.",
        StoppedShort => "Repeated stop-short errors usually mean the movement amplitude is being cut early, often under speed pressure or from over-braking.",
        LateAcquisition => "A late-acquisition streak means reading/launch timing or movement speed is falling behind for several objects, not just one unlucky landing.",
        CorrectionLoop => "Repeated correction loops suggest the first movement is not trusted or controlled enough, forcing extra reversals close to the hit time.",
        CurvedApproach => "A curved-approach streak means path efficiency is breaking down across the pattern rather than on one isolated jump.",
        LateralDrift => "Repeated lateral drift can indicate a directional bias or difficulty keeping the movement axis stable through a pattern.",
        BrakingFailure => "Repeated unstable braking means the cursor is arriving with too much unresolved velocity across the section.",
        GeneralImprecision => "A run of general imprecision means center accuracy is deteriorating without one dominant mechanical signature.",
        _ => ""
    };

    private static AimCauseDiagnosis CleanDiagnosis(TransitionMetric t)
        => new(t, Clean, .85, 0, "No strong trajectory-derived failure signature on this jump.");

    private static string Explain(string cause, TransitionMetric t, double radial)
    {
        string pos = radial < .05 ? "near center" : $"{radial:0.00}R from center";
        return cause switch
        {
            ShakeOff => $"Arrival remained relatively strong ({t.Arrival:0}), but final stability fell to {t.Stability:0}; the hit finished {pos}.",
            BrakingOvershoot => $"The landing finished {Math.Max(0, t.AxialError):0.00}R beyond center while braking scored {t.Deceleration:0}.",
            StoppedShort => $"The landing finished {Math.Max(0, -t.AxialError):0.00}R short of center; arrival {t.Arrival:0}, braking {t.Deceleration:0}.",
            LateAcquisition => $"Arrival timing scored {t.Arrival:0} and the final landing was {pos}.",
            CorrectionLoop => $"The trajectory crossed both sides of the target axis near the hit; stability {t.Stability:0}, braking {t.Deceleration:0}.",
            CurvedApproach => $"Straightness was {t.Straightness:0} and ideal-path match {ScoringConfig.EffectiveIdealPathMatch(t):0}; the final landing was {pos}.",
            LateralDrift => $"The cursor finished {Math.Abs(t.LateralError):0.00}R sideways from the incoming jump axis; straightness {t.Straightness:0}.",
            BrakingFailure => $"Final braking scored {t.Deceleration:0} while the landing finished {pos}.",
            GeneralImprecision => $"Landing quality was {t.Landing:0} ({pos}) without a stronger single trajectory signature.",
            _ => ""
        };
    }

    private static AimCauseStreak? FindLongestStreak(IReadOnlyList<AimCauseDiagnosis> diagnoses)
    {
        AimCauseStreak? best = null;
        int start = 0;
        while (start < diagnoses.Count)
        {
            if (diagnoses[start].Cause.Equals(Clean, StringComparison.OrdinalIgnoreCase)) { start++; continue; }
            int end = start;
            while (end + 1 < diagnoses.Count &&
                   diagnoses[end + 1].Cause.Equals(diagnoses[start].Cause, StringComparison.OrdinalIgnoreCase) &&
                   diagnoses[end + 1].Metric.ObjectIndex - diagnoses[end].Metric.ObjectIndex <= 2 &&
                   diagnoses[end + 1].Metric.TimeMs - diagnoses[end].Metric.TimeMs <= 1200)
            {
                end++;
            }
            int count = end - start + 1;
            if (count >= 2 && (best == null || count > best.Count))
            {
                best = new AimCauseStreak(
                    diagnoses[start].Cause,
                    count,
                    diagnoses[start].Metric.ObjectIndex + 1,
                    diagnoses[end].Metric.ObjectIndex + 1,
                    diagnoses[start].Metric.TimeMs,
                    diagnoses[end].Metric.TimeMs);
            }
            start = end + 1;
        }
        return best;
    }

    private static double Bad(double score, double threshold, double span)
        => Math.Clamp((threshold - score) / Math.Max(1, span), 0, 1);

    private static double Good(double score, double low, double high)
        => Math.Clamp((score - low) / Math.Max(1, high - low), 0, 1);
}
