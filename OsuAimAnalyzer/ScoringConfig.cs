namespace OsuAimAnalyzer;

public static class ScoringConfig
{
    // Bump this whenever the interpretation of the stored transition components changes.
    // AnalyzerDatabase rescales existing history from stored telemetry when possible.
    public const int CurrentScoringVersion = 6;

    // Baseline weights around ordinary NM aim. The movement-quality family (straightness,
    // braking, stability and ideal-path match) deliberately owns most of the score so a hit
    // that is visibly shaky/inefficient does not look as clean as a controlled hit.
    public const double WeightLanding = 0.21;
    public const double WeightArrival = 0.14;
    public const double WeightStraightness = 0.19;
    public const double WeightDeceleration = 0.17;
    public const double WeightStability = 0.17;
    public const double WeightIdealPath = 0.12;

    public const double AggregateMeanWeight = 0.65;
    public const double AggregateLowerQuartileWeight = 0.25;
    public const double AggregateWorstDecileWeight = 0.10;

    public const double MasteredThreshold = 920;
    public const double ControlledThreshold = 850;
    public const double ChallengingThreshold = 725;
    public const double AimRatingReferenceProficiency = 850;
    public const int AimRatingTopPlayCount = 100;

    public readonly record struct ProficiencyWeights(
        double Landing,
        double Arrival,
        double Straightness,
        double Deceleration,
        double Stability,
        double IdealPath)
    {
        public double Sum => Landing + Arrival + Straightness + Deceleration + Stability + IdealPath;
    }

    /// <summary>
    /// Context-aware proficiency weights. Adjustments stay deliberately modest: fast aim gets
    /// a little less settling/path-template weight, while wide aim puts more emphasis on landing
    /// and braking. The difficulty itself is not removed from the score.
    /// </summary>
    public static ProficiencyWeights GetProficiencyWeights(double bpm, double normalizedSpacing)
    {
        double landing = WeightLanding;
        double arrival = WeightArrival;
        double straight = WeightStraightness;
        double decel = WeightDeceleration;
        double stability = WeightStability;
        double ideal = WeightIdealPath;

        // Speed pressure is 0 around <=180 BPM and reaches 1 around 300 BPM.
        // High BPM naturally has less time to settle, but visible shake still matters.
        double speed = Math.Clamp((bpm - 180.0) / 120.0, 0, 1);
        landing += .016 * speed;
        arrival += .020 * speed;
        straight += .010 * speed;
        decel -= .008 * speed;
        stability -= .026 * speed;
        ideal -= .012 * speed;

        // Wide jumps make centering/braking more important. Ideal-path match gets a tiny bump
        // because large geometric inefficiency is especially meaningful on wide aim.
        double wide = Math.Clamp((normalizedSpacing - 145.0) / 150.0, 0, 1);
        landing += .023 * wide;
        decel += .017 * wide;
        ideal += .006 * wide;
        straight -= .004 * wide;
        arrival -= .010 * wide;
        stability -= .009 * wide;

        // Compact aim has lots of geometric margin; wobble/path noise therefore matters more.
        double compact = Math.Clamp((110.0 - normalizedSpacing) / 70.0, 0, 1);
        stability += .018 * compact;
        straight += .012 * compact;
        ideal += .006 * compact;
        landing -= .020 * compact;
        arrival -= .010 * compact;
        decel -= .006 * compact;

        double sum = landing + arrival + straight + decel + stability + ideal;
        if (sum <= 0)
            return new ProficiencyWeights(WeightLanding, WeightArrival, WeightStraightness, WeightDeceleration, WeightStability, WeightIdealPath);
        return new ProficiencyWeights(landing / sum, arrival / sum, straight / sum, decel / sum, stability / sum, ideal / sum);
    }

    /// <summary>
    /// Converts the stored cursor-at-object error into a slightly stricter proficiency reference
    /// for low-CS / large-circle maps. Actual hit geometry still uses the real circle radius;
    /// this only prevents huge circles from inflating centering proficiency.
    /// </summary>
    private static double StrictLandingRatio(TransitionMetric x, out double actualToCs4RadiusRatio)
    {
        double r = Math.Sqrt(x.AxialError * x.AxialError + x.LateralError * x.LateralError);
        actualToCs4RadiusRatio = 1.0;
        if (x.Spacing > 0.001 && x.NormalizedSpacing > 0.001)
        {
            // normalizedSpacing = rawSpacing * CS4Radius / actualRadius
            actualToCs4RadiusRatio = Math.Clamp(x.Spacing / x.NormalizedSpacing, 0.55, 1.60);
        }

        // Only tighten large circles (ratio > 1). Small circles remain judged against their
        // already-strict true radius rather than receiving a free boost.
        double largeCircleExtra = Math.Max(0, actualToCs4RadiusRatio - 1.0);
        double strictness = 1.0 + Math.Min(.35, largeCircleExtra * .65);
        return r * strictness;
    }

    public static double EffectiveIdealPathMatch(TransitionMetric x)
    {
        if (x.IdealPathMatch > 0.01 && double.IsFinite(x.IdealPathMatch))
            return Clamp1000(x.IdealPathMatch);

        // Historical rows created before ideal-path telemetry existed cannot be reconstructed
        // without reopening the replay. Use a conservative proxy so old history stays comparable;
        // newly analyzed/re-analyzed plays store the real synthetic-path comparison.
        double proxy = .31 * x.Straightness
                     + .18 * x.Landing
                     + .14 * x.Arrival
                     + .18 * x.Deceleration
                     + .19 * x.Stability;
        return Clamp1000(proxy);
    }

    public static double TransitionProficiency(TransitionMetric x)
    {
        var w = GetProficiencyWeights(x.Bpm, x.NormalizedSpacing);

        double strictR = StrictLandingRatio(x, out double radiusRatio);
        double strictLanding = 1000 * Math.Exp(-0.72 * strictR * strictR);
        // Never make landing more lenient than the original metric; only large-circle maps can
        // become stricter here.
        double landing = Math.Min(x.Landing, strictLanding);
        double decel = x.Deceleration;

        // Underaim is still less important than a full miss when the cursor actually reaches the
        // target area, but the forgiveness now scales down on low-CS / large-circle maps.
        if (string.Equals(x.ErrorClass, "Underaim", StringComparison.OrdinalIgnoreCase))
        {
            double recovery = strictR switch
            {
                <= .45 => .44,
                <= .65 => .31,
                <= .85 => .20,
                <= 1.00 => .11,
                <= 1.12 => .04, // slight exact-time forgiveness for a cursor that is only barely outside
                _ => 0
            };

            double bigCirclePenalty = 1.0 / (1.0 + 1.20 * Math.Max(0, radiusRatio - 1.0));
            recovery *= Math.Clamp(bigCirclePenalty, .58, 1.0);
            landing += (1000 - landing) * recovery;

            // DecelerationScore includes an underaim class multiplier. Give most of it back so
            // underaim is judged mainly by actual centering rather than a duplicated class penalty.
            decel = Math.Min(1000, decel / .88 * .97);
        }

        double ideal = EffectiveIdealPathMatch(x);
        return Clamp1000(landing * w.Landing
                       + x.Arrival * w.Arrival
                       + x.Straightness * w.Straightness
                       + decel * w.Deceleration
                       + x.Stability * w.Stability
                       + ideal * w.IdealPath);
    }

    public static double AggregateProficiency(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        double mean = a.Average();
        int qn = Math.Max(1, (int)Math.Ceiling(a.Length * .25));
        int dn = Math.Max(1, (int)Math.Ceiling(a.Length * .10));
        double q = a.Take(qn).Average();
        double d = a.Take(dn).Average();
        return Clamp1000(mean * AggregateMeanWeight + q * AggregateLowerQuartileWeight + d * AggregateWorstDecileWeight);
    }

    // Replay-derived estimate, not a physiological measurement.
    // 0 = relaxed/settled-looking cursor, 100 = strong signs of over-tension.
    // High-BPM / very-high-AR aim is intentionally judged more strictly because the
    // same visible shake/correction usually represents less spare control margin there.
    public static double InferredAimTension(
        double stability,
        double deceleration,
        double straightness,
        double landing,
        string? errorClass = null,
        double bpm = 0,
        double effectiveAr = 0,
        double spacing = 0,
        double density = 0)
    {
        double stabilityLoss = 1000 - Math.Clamp(stability, 0, 1000);
        double decelLoss = 1000 - Math.Clamp(deceleration, 0, 1000);
        double straightLoss = 1000 - Math.Clamp(straightness, 0, 1000);
        double landingLoss = 1000 - Math.Clamp(landing, 0, 1000);

        double strain = .57 * stabilityLoss
                      + .25 * decelLoss
                      + .11 * straightLoss
                      + .07 * landingLoss;

        strain += errorClass switch
        {
            "Correction" => 75,
            "Overaim" or "Underaim" => 42,
            "Lateral" or "Plain error" => 32,
            _ => 0
        };

        double context = 1.0;
        if (bpm > 220) context += Math.Min(.30, (bpm - 220) / 260.0);
        if (effectiveAr > 9.5) context += Math.Min(.28, (effectiveAr - 9.5) * .30);
        if (spacing > 180) context += Math.Min(.16, (spacing - 180) / 500.0);
        if (density > 4.0) context += Math.Min(.12, (density - 4.0) * .05);

        if (bpm >= 250 && effectiveAr >= 10.0)
        {
            if (stability < 900) strain += (900 - stability) * .28;
            if (deceleration < 900) strain += (900 - deceleration) * .18;
        }

        double raw = strain * context / 5.15;
        return Math.Clamp(raw, 0, 100);
    }

    public static string ClassifyZone(double proficiency, double accuracy, int misses)
    {
        if (proficiency >= MasteredThreshold || (misses == 0 && accuracy >= 99.5 && proficiency >= 850))
            return "Mastered";
        if (proficiency >= ControlledThreshold || (misses == 0 && accuracy >= 99.0 && proficiency >= 800))
            return "Controlled";
        if (proficiency >= ChallengingThreshold)
            return "Challenging";
        return "Breakdown";
    }

    private static double Clamp1000(double x) => Math.Clamp(x, 0, 1000);
}
