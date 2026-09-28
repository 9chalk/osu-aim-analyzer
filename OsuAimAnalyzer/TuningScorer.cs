namespace OsuAimAnalyzer;

public sealed record ProficiencyBreakdown(
    double LandingRaw, double LandingEffective,
    double Arrival, double Straightness, double DecelerationRaw, double DecelerationEffective,
    double Stability, double IdealPath,
    double LandingWeight, double ArrivalWeight, double StraightnessWeight,
    double DecelerationWeight, double StabilityWeight, double IdealPathWeight,
    double Score);

public sealed record PerformanceBreakdown(
    double ChallengeValue, double ChallengeRatio,
    double StarRatio, double StarDemandIndex, double StarFloorIndex,
    double BpmRatio, double SpacingRatio, double ArRatio, double DensityRatio,
    double HardSectionBpm, double HardSectionSpacing, double HardSectionDensity, double HardSectionVelocity,
    int HardSectionTransitions, double HardSectionThreshold,
    double BaseDemandIndex, double DemandIndex, double HardExecutionProficiency, double ExecutionMultiplier,
    double BaseCapabilityScore, double AimActiveSeconds, int MissCount,
    double MissConsistency, double ControlCoverage, double DurationFactor,
    double ConsistencyQuality, double ConsistencyMultiplier,
    double Score);

public sealed record DifficultyWeightInfo(double Weight, int JumpStreakLength, double RelativeDemand);

public static class TuningScorer
{
    public static ProficiencyBreakdown Transition(TransitionMetric x, TuningConfig c)
    {
        var weights = EffectiveWeights(x, c);

        double strictR = StrictLandingRatio(x, c, out double radiusRatio);
        double strictLanding = 1000 * Math.Exp(-0.72 * strictR * strictR);
        double landing = Math.Min(x.Landing, strictLanding);
        double decel = x.Deceleration;

        if (string.Equals(x.ErrorClass, "Underaim", StringComparison.OrdinalIgnoreCase))
        {
            double recovery = strictR switch
            {
                <= .45 => c.UnderAimInnerRecovery,
                <= .65 => c.UnderAimMidRecovery,
                <= .85 => c.UnderAimOuterRecovery,
                <= 1.00 => c.UnderAimEdgeRecovery,
                <= 1.12 => c.UnderAimOutsideRecovery,
                _ => 0
            };
            double bigCirclePenalty = 1.0 / (1.0 + 1.20 * Math.Max(0, radiusRatio - 1.0));
            recovery *= Math.Clamp(bigCirclePenalty, .45, 1.0);
            landing += (1000 - landing) * recovery;
            decel = Math.Min(1000, decel / .88 * .97);
        }

        double ideal = ScoringConfig.EffectiveIdealPathMatch(x);
        double score = Clamp1000(landing * weights.Landing
                               + x.Arrival * weights.Arrival
                               + x.Straightness * weights.Straightness
                               + decel * weights.Deceleration
                               + x.Stability * weights.Stability
                               + ideal * weights.IdealPath);

        return new ProficiencyBreakdown(
            x.Landing, landing, x.Arrival, x.Straightness, x.Deceleration, decel, x.Stability, ideal,
            weights.Landing, weights.Arrival, weights.Straightness, weights.Deceleration, weights.Stability, weights.IdealPath,
            score);
    }

    public static DifficultyWeightInfo[] DifficultyWeights(IReadOnlyList<TransitionMetric> transitions, TuningConfig c)
    {
        if (transitions.Count == 0) return Array.Empty<DifficultyWeightInfo>();

        double medianSpacing = Median(transitions.Select(t => Math.Max(1, t.NormalizedSpacing)));
        double medianBpm = Median(transitions.Select(t => Math.Max(1, t.Bpm)));
        double threshold = Math.Max(0, c.DifficultyStreakSpacingThreshold);
        int[] streak = new int[transitions.Count];

        // Give every object in a sustained jump run the run's full length, not merely its
        // position in the streak. This makes an 8-jump difficult section matter as a section.
        int start = 0;
        while (start < transitions.Count)
        {
            if (transitions[start].NormalizedSpacing < threshold) { start++; continue; }
            int end = start + 1;
            while (end < transitions.Count
                && transitions[end].NormalizedSpacing >= threshold
                && transitions[end].ObjectIndex == transitions[end - 1].ObjectIndex + 1) end++;
            int len = end - start;
            for (int i = start; i < end; i++) streak[i] = len;
            start = end;
        }

        double sw = Math.Max(0, c.DifficultySpacingWeight);
        double bw = Math.Max(0, c.DifficultyBpmWeight);
        double jw = Math.Max(0, c.DifficultyStreakWeight);
        double denom = Math.Max(.001, sw + bw + jw);
        double full = Math.Max(1, c.DifficultyStreakFullLength);
        var rawDemand = new double[transitions.Count];
        var rawWeights = new double[transitions.Count];

        for (int i = 0; i < transitions.Count; i++)
        {
            var t = transitions[i];
            double spacingRatio = Math.Clamp(t.NormalizedSpacing / Math.Max(1, medianSpacing), .35, 3.0);
            double bpmRatio = Math.Clamp(t.Bpm / Math.Max(1, medianBpm), .50, 2.0);
            double streakRatio = 1.0 + Math.Min(1.0, streak[i] / full); // 1.0..2.0
            double demand = (sw * spacingRatio + bw * bpmRatio + jw * streakRatio) / denom;
            rawDemand[i] = demand;
            rawWeights[i] = Math.Exp(Math.Clamp(c.DifficultyWeightStrength, 0, 4) * (demand - 1.0));
        }

        // Normalize to an average weight of 1 so enabling difficulty weighting changes
        // *which sections matter*, not the numerical scale of proficiency itself.
        double avg = rawWeights.Average();
        double floor = Math.Max(.05, c.DifficultyWeightFloor);
        double ceil = Math.Max(floor, c.DifficultyWeightCeiling);
        var result = new DifficultyWeightInfo[transitions.Count];
        for (int i = 0; i < transitions.Count; i++)
        {
            double w = rawWeights[i] / Math.Max(.001, avg);
            w = Math.Clamp(w, floor, ceil);
            result[i] = new DifficultyWeightInfo(w, streak[i], rawDemand[i]);
        }
        return result;
    }

    public static double AggregateRaw(IReadOnlyList<double> values, IReadOnlyList<DifficultyWeightInfo> difficulty, TuningConfig c)
    {
        if (values.Count == 0) return 0;
        var pairs = values.Select((score, i) => (Score: score, Weight: i < difficulty.Count ? difficulty[i].Weight : 1.0))
            .OrderBy(x => x.Score).ToArray();
        double mean = WeightedMean(pairs);
        int qn = Math.Max(1, (int)Math.Ceiling(pairs.Length * .25));
        int dn = Math.Max(1, (int)Math.Ceiling(pairs.Length * .10));
        double q = WeightedMean(pairs.Take(qn));
        double d = WeightedMean(pairs.Take(dn));
        double sum = c.MeanWeight + c.LowerQuartileWeight + c.WorstDecileWeight;
        if (sum <= 0) return Clamp1000(mean);
        return Clamp1000((mean * c.MeanWeight + q * c.LowerQuartileWeight + d * c.WorstDecileWeight) / sum);
    }

    public static double Aggregate(IReadOnlyList<double> values, IReadOnlyList<DifficultyWeightInfo> difficulty, TuningConfig c)
        => RemapProficiency(AggregateRaw(values, difficulty, c), c);

    public static double Aggregate(IEnumerable<double> values, TuningConfig c)
    {
        var a = values.ToArray();
        if (a.Length == 0) return 0;
        return Aggregate(a, Enumerable.Repeat(new DifficultyWeightInfo(1, 0, 1), a.Length).ToArray(), c);
    }

    public static double RemapProficiency(double raw, TuningConfig c)
    {
        raw = Clamp1000(raw);
        double r1 = Math.Clamp(c.ProficiencyFocusRawStart, 1, 998);
        double r2 = Math.Clamp(c.ProficiencyFocusRawEnd, r1 + 1, 999);
        double m1 = Math.Clamp(c.ProficiencyFocusMappedStart, 0, 998);
        double m2 = Math.Clamp(c.ProficiencyFocusMappedEnd, m1 + 1, 999);

        double target;
        if (raw <= r1)
            target = raw / r1 * m1;
        else if (raw <= r2)
            target = m1 + (raw - r1) / (r2 - r1) * (m2 - m1);
        else
            target = m2 + (raw - r2) / (1000.0 - r2) * (1000.0 - m2);

        double strength = Math.Clamp(c.ProficiencyResolutionStrength, 0, 1);
        return Clamp1000(raw + (target - raw) * strength);
    }

    public static PerformanceBreakdown Performance(PlayAnalysis a, double tunedProficiency, TuningConfig c)
    {
        var transitions = a.Transitions ?? new List<TransitionMetric>();
        var sustained = SustainedDemand(transitions, c);

        double challengeValue = sustained.ChallengeRatio * c.RefChallenge;
        double challenge = SafeRatio(challengeValue, c.RefChallenge);
        double star = a.StarRating > 0 ? SafeRatio(a.StarRating, c.RefStar) : 1.0;
        double bpm = SafeRatio(sustained.Bpm > 0 ? sustained.Bpm : a.MeanBpm, c.RefBpm);
        double spacing = SafeRatio(sustained.Spacing > 0 ? sustained.Spacing : a.SpacingP75, c.RefSpacing);
        double ar = SafeRatio(a.EffectiveAr, c.RefAr);
        double density = SafeRatio(sustained.Density > 0 ? sustained.Density : a.DensityP90, c.RefDensity);

        // v8: SR is no longer just one small ingredient in a six-way geometric mean.
        // That allowed a real 12-13* aim map to collapse toward 8* if our simplified
        // spacing/velocity model under-described its pattern complexity.  Telemetry now
        // builds an independent demand estimate, while SR establishes a tunable floor.
        var telemetryParts = new[]
        {
            (challenge, c.PerformanceChallengeWeight), (bpm, c.PerformanceBpmWeight),
            (spacing, c.PerformanceSpacingWeight), (ar, c.PerformanceArWeight), (density, c.PerformanceDensityWeight)
        };
        double wsum = telemetryParts.Sum(p => Math.Max(0, p.Item2));
        if (wsum <= 0) wsum = 1;

        double log = 0;
        foreach (var p in telemetryParts)
        {
            double w = Math.Max(0, p.Item2) / wsum;
            log += w * Math.Log(Math.Max(.05, p.Item1));
        }
        double baseDemand = Math.Exp(log);
        double demandCurve = Math.Clamp(c.PerformanceDemandCurve, .25, 4.0);
        double telemetryDemand = Math.Pow(baseDemand, demandCurve);

        double starExponent = Math.Clamp(c.PerformanceStarDemandExponent, .25, 3.0);
        double starDemand = Math.Pow(Math.Max(.05, star), starExponent);
        double starFloorStrength = Math.Clamp(c.PerformanceStarWeight, 0, 2.0);
        double starFloor = starDemand >= 1
            ? 1.0 + (starDemand - 1.0) * starFloorStrength
            : 1.0 - (1.0 - starDemand) * Math.Min(1.0, starFloorStrength);
        starFloor = Math.Max(.05, starFloor);

        double demand = Math.Max(telemetryDemand, starFloor);

        // Aim Performance should represent demonstrated capability, not whole-map score conversion.
        // Measure execution on the same sustained hard material that defines demand. A miss in
        // that material hurts locally, but a few misses elsewhere cannot globally nuke the score.
        var allTransitionScores = transitions.Select(t => Transition(t, c).Score).ToArray();
        var hardScores = sustained.SelectedIndices.Count > 0
            ? sustained.SelectedIndices.Select(k => allTransitionScores[k]).OrderBy(x => x).ToArray()
            : allTransitionScores.OrderBy(x => x).ToArray();
        double hardMean = hardScores.Length > 0 ? hardScores.Average() : tunedProficiency;
        int hardQn = Math.Max(1, (int)Math.Ceiling(hardScores.Length * .25));
        double hardLowerQuartile = hardScores.Length > 0 ? hardScores.Take(hardQn).Average() : tunedProficiency;
        double hem = Math.Max(0, c.PerformanceHardExecutionMeanWeight);
        double heq = Math.Max(0, c.PerformanceHardExecutionLowerQuartileWeight);
        double hes = Math.Max(.001, hem + heq);
        double hardExecutionProficiency = (hardMean * hem + hardLowerQuartile * heq) / hes;

        double curve = Math.Max(10, c.PerformanceExecutionCurve);
        double execution = Math.Exp(((hardExecutionProficiency - c.PerformanceReferenceProficiency) / curve) * c.PerformanceExecutionWeight);
        double baseCapability = c.PerformanceScale * demand * execution;

        // Consistency is a bonus-only layer. Missing objects does not erase the mechanical
        // capability demonstrated on the rest of the replay; it merely reduces the extra
        // credit earned for sustaining that capability across the whole map.
        double aimActiveSeconds = transitions.Sum(t => Math.Min(Math.Max(0, t.IntervalMs), 500.0)) / 1000.0;
        int misses = a.Replay?.CountMiss ?? 0;
        double missTolerance = Math.Max(.25, c.PerformanceConsistencyMissTolerance);
        double missConsistency = Math.Exp(-misses / missTolerance);

        var transitionScores = allTransitionScores;
        var importance = DifficultyWeights(transitions, c);
        double controlFloor = Math.Clamp(c.PerformanceConsistencyControlFloor, 0, 990);
        double controlFull = Math.Clamp(c.PerformanceConsistencyControlFull, controlFloor + 1, 1000);
        double controlNumerator = 0, controlWeights = 0;
        for (int k = 0; k < transitionScores.Length; k++)
        {
            double w = k < importance.Length ? Math.Max(.001, importance[k].Weight) : 1.0;
            double q = Math.Clamp((transitionScores[k] - controlFloor) / (controlFull - controlFloor), 0, 1);
            // smoothstep keeps near-threshold noise from causing abrupt bonus changes.
            q = q * q * (3 - 2 * q);
            controlNumerator += q * w;
            controlWeights += w;
        }
        double controlCoverage = controlWeights > 0 ? controlNumerator / controlWeights : 0;

        double mw = Math.Max(0, c.PerformanceConsistencyMissWeight);
        double cw = Math.Max(0, c.PerformanceConsistencyControlWeight);
        double qsum = Math.Max(.001, mw + cw);
        double consistencyQuality = Math.Pow(Math.Max(.001, missConsistency), mw / qsum)
                                  * Math.Pow(Math.Max(.001, controlCoverage), cw / qsum);

        double durationRef = Math.Max(5, c.PerformanceConsistencyDurationReferenceSeconds);
        double durationExp = Math.Clamp(c.PerformanceConsistencyDurationExponent, .20, 3.0);
        double durationFactor = 1.0 - Math.Exp(-Math.Pow(Math.Max(0, aimActiveSeconds) / durationRef, durationExp));
        double maxBonus = Math.Clamp(c.PerformanceConsistencyMaxBonus, 0, 2.0);
        double consistencyMultiplier = 1.0 + maxBonus * durationFactor * consistencyQuality;
        double score = baseCapability * consistencyMultiplier;

        return new PerformanceBreakdown(
            challengeValue, challenge, star, starDemand, starFloor, bpm, spacing, ar, density,
            sustained.Bpm, sustained.Spacing, sustained.Density, sustained.Velocity,
            sustained.Count, sustained.Threshold,
            baseDemand, demand, hardExecutionProficiency, execution,
            baseCapability, aimActiveSeconds, misses,
            missConsistency, controlCoverage, durationFactor,
            consistencyQuality, consistencyMultiplier,
            score);
    }

    private sealed record SustainedDemandInfo(double ChallengeRatio, double Bpm, double Spacing, double Density, double Velocity, int Count, double Threshold, IReadOnlyList<int> SelectedIndices);

    private static SustainedDemandInfo SustainedDemand(IReadOnlyList<TransitionMetric> transitions, TuningConfig c)
    {
        if (transitions.Count == 0)
            return new SustainedDemandInfo(1, 0, 0, 0, 0, 0, 0, Array.Empty<int>());

        double vRef = Math.Max(50, c.RefVelocity);
        double sRef = Math.Max(20, c.RefSpacing);
        double dRef = Math.Max(1, c.RefDensity);
        double vExp = Math.Clamp(c.PerformanceVelocityExponent, .1, 4.0);
        double sExp = Math.Clamp(c.PerformanceSpacingExponent, 0, 2.0);

        double Raw(TransitionMetric t)
        {
            double velocity = Math.Pow(Math.Max(.05, t.Velocity / vRef), vExp);
            double spacing = Math.Pow(Math.Max(.05, t.NormalizedSpacing / sRef), sExp);
            double reversal = (1 - Math.Cos(t.Angle * Math.PI / 180.0)) / 2.0;
            double angle = 1.0 + .30 * reversal;
            double density = Math.Pow(Math.Max(.35, t.Density / dRef), .10);
            return velocity * spacing * angle * density;
        }

        var raw = transitions.Select(Raw).ToArray();
        double pct = Math.Clamp(c.PerformanceDemandPercentile, .50, .99);
        double threshold = Percentile(raw, pct);
        int minStreak = Math.Max(1, (int)Math.Round(c.PerformanceMinHardSectionStreak));
        var selected = new List<int>();

        int i = 0;
        while (i < transitions.Count)
        {
            if (raw[i] < threshold) { i++; continue; }
            int j = i + 1;
            while (j < transitions.Count
                && raw[j] >= threshold
                && transitions[j].ObjectIndex == transitions[j - 1].ObjectIndex + 1) j++;
            if (j - i >= minStreak)
                for (int k = i; k < j; k++) selected.Add(k);
            i = j;
        }

        // Some maps alternate hard/easy objects and therefore have no strict percentile run.
        // Fall back to the strongest transitions so the score remains defined, but expose the
        // selected count in the UI so the user can see when this fallback is happening.
        if (selected.Count == 0)
        {
            int take = Math.Max(minStreak, (int)Math.Ceiling(transitions.Count * Math.Max(.05, 1.0 - pct)));
            selected = Enumerable.Range(0, transitions.Count)
                .OrderByDescending(k => raw[k])
                .Take(Math.Min(take, transitions.Count))
                .OrderBy(k => k)
                .ToList();
        }

        var chosenRaw = selected.Select(k => raw[k]).ToArray();
        // Use the mean of the sustained hard material, with a small upper-tail emphasis.
        double mean = chosenRaw.Average();
        double upper = Percentile(chosenRaw, .75);
        double challengeRatio = .70 * mean + .30 * upper;

        double bpm = Median(selected.Select(k => transitions[k].Bpm));
        double spacing = Percentile(selected.Select(k => transitions[k].NormalizedSpacing), .60);
        double density = Median(selected.Select(k => transitions[k].Density));
        double velocity = Median(selected.Select(k => transitions[k].Velocity));
        return new SustainedDemandInfo(challengeRatio, bpm, spacing, density, velocity, selected.Count, threshold, selected);
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var a = values.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        p = Math.Clamp(p, 0, 1);
        double pos = (a.Length - 1) * p;
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return a[lo];
        double f = pos - lo;
        return a[lo] * (1 - f) + a[hi] * f;
    }

    private static ScoringConfig.ProficiencyWeights EffectiveWeights(TransitionMetric x, TuningConfig c)
    {
        double l = c.LandingWeight, a = c.ArrivalWeight, s = c.StraightnessWeight, d = c.DecelerationWeight, st = c.StabilityWeight, i = c.IdealPathWeight;
        double baseSum = l + a + s + d + st + i;
        if (baseSum <= 0) return new ScoringConfig.ProficiencyWeights(.21, .14, .19, .17, .17, .12);
        l /= baseSum; a /= baseSum; s /= baseSum; d /= baseSum; st /= baseSum; i /= baseSum;

        // Reuse the analyzer's context shift, but apply only the difference from its own baseline.
        var context = ScoringConfig.GetProficiencyWeights(x.Bpm, x.NormalizedSpacing);
        double strength = c.ContextWeightStrength;
        l += (context.Landing - ScoringConfig.WeightLanding) * strength;
        a += (context.Arrival - ScoringConfig.WeightArrival) * strength;
        s += (context.Straightness - ScoringConfig.WeightStraightness) * strength;
        d += (context.Deceleration - ScoringConfig.WeightDeceleration) * strength;
        st += (context.Stability - ScoringConfig.WeightStability) * strength;
        i += (context.IdealPath - ScoringConfig.WeightIdealPath) * strength;

        l = Math.Max(0, l); a = Math.Max(0, a); s = Math.Max(0, s); d = Math.Max(0, d); st = Math.Max(0, st); i = Math.Max(0, i);
        double sum = l + a + s + d + st + i;
        return new ScoringConfig.ProficiencyWeights(l / sum, a / sum, s / sum, d / sum, st / sum, i / sum);
    }

    private static double StrictLandingRatio(TransitionMetric x, TuningConfig c, out double actualToCs4RadiusRatio)
    {
        double r = Math.Sqrt(x.AxialError * x.AxialError + x.LateralError * x.LateralError);
        actualToCs4RadiusRatio = 1.0;
        if (x.Spacing > .001 && x.NormalizedSpacing > .001)
            actualToCs4RadiusRatio = Math.Clamp(x.Spacing / x.NormalizedSpacing, .55, 1.60);
        double extra = Math.Max(0, actualToCs4RadiusRatio - 1.0);
        double strictness = 1.0 + Math.Min(.55, extra * c.LargeCircleStrictness);
        return r * strictness;
    }

    private static double WeightedMean(IEnumerable<(double Score, double Weight)> values)
    {
        double sum = 0, weights = 0;
        foreach (var v in values)
        {
            double w = Math.Max(.001, v.Weight);
            sum += v.Score * w;
            weights += w;
        }
        return weights <= 0 ? 0 : sum / weights;
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    private static double SafeRatio(double value, double reference) => Math.Clamp(value / Math.Max(.001, reference), .05, 4.0);
    private static double Clamp1000(double x) => Math.Clamp(double.IsFinite(x) ? x : 0, 0, 1000);
}
