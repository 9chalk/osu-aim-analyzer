namespace OsuAimAnalyzer;

/// <summary>
/// Fixed production scoring profile promoted from Aim Tuner v9.
/// The standalone analyzer intentionally does not expose these values as live tuning controls.
/// </summary>
public static class ProductionScoring
{
    public static TuningConfig Profile { get; } = CreateProfile();

    public static double MasteredThreshold => TuningScorer.RemapProficiency(ScoringConfig.MasteredThreshold, Profile);
    public static double ControlledThreshold => TuningScorer.RemapProficiency(ScoringConfig.ControlledThreshold, Profile);
    public static double ChallengingThreshold => TuningScorer.RemapProficiency(ScoringConfig.ChallengingThreshold, Profile);

    public static TuningConfig CreateProfile() => new()
    {
        SchemaVersion = 8,
        LandingWeight = 0.21,
        ArrivalWeight = 0.14,
        StraightnessWeight = 0.19,
        DecelerationWeight = 0.17,
        StabilityWeight = 0.17,
        IdealPathWeight = 0.12,
        MeanWeight = 0.65,
        LowerQuartileWeight = 0.25,
        WorstDecileWeight = 0.10,
        ProficiencyResolutionStrength = 1.0,
        ProficiencyFocusRawStart = 750.0,
        ProficiencyFocusMappedStart = 550.0,
        ProficiencyFocusRawEnd = 900.0,
        ProficiencyFocusMappedEnd = 900.0,
        DifficultyWeightStrength = 1.0,
        DifficultySpacingWeight = 0.50,
        DifficultyBpmWeight = 0.25,
        DifficultyStreakWeight = 0.25,
        DifficultyStreakSpacingThreshold = 140.0,
        DifficultyStreakFullLength = 8.0,
        DifficultyWeightFloor = 0.55,
        DifficultyWeightCeiling = 2.75,
        ContextWeightStrength = 1.0,
        UnderAimInnerRecovery = 0.44,
        UnderAimMidRecovery = 0.31,
        UnderAimOuterRecovery = 0.20,
        UnderAimEdgeRecovery = 0.11,
        UnderAimOutsideRecovery = 0.04,
        LargeCircleStrictness = 0.65,

        PerformanceChallengeWeight = 0.22,
        PerformanceStarWeight = 1.25,
        PerformanceBpmWeight = 0.45,
        PerformanceSpacingWeight = 0.03,
        PerformanceArWeight = 0.15,
        PerformanceDensityWeight = 0.10,
        PerformanceScale = 500.0,
        PerformanceReferenceProficiency = 850.0,
        PerformanceExecutionCurve = 550.0,
        PerformanceExecutionWeight = 1.0,
        PerformanceHardExecutionMeanWeight = 0.90,
        PerformanceHardExecutionLowerQuartileWeight = 0.10,
        PerformanceVelocityExponent = 1.35,
        PerformanceSpacingExponent = 0.12,
        PerformanceStarDemandExponent = 1.35,
        PerformanceDemandCurve = 1.10,
        PerformanceDemandPercentile = 0.85,
        PerformanceMinHardSectionStreak = 4.0,
        PerformanceConsistencyMaxBonus = 0.20,
        PerformanceConsistencyDurationReferenceSeconds = 180.0,
        PerformanceConsistencyDurationExponent = 1.40,
        PerformanceConsistencyMissTolerance = 4.0,
        PerformanceConsistencyMissWeight = 0.45,
        PerformanceConsistencyControlWeight = 0.55,
        PerformanceConsistencyControlFloor = 650.0,
        PerformanceConsistencyControlFull = 900.0,
        RefChallenge = 100.0,
        RefVelocity = 1800.0,
        RefStar = 6.0,
        RefBpm = 240.0,
        RefSpacing = 200.0,
        RefAr = 9.5,
        RefDensity = 4.0
    };

    public static double TransitionScore(TransitionMetric metric)
        => TuningScorer.Transition(metric, Profile).Score;

    public static (double RawProficiency, double FinalProficiency, PerformanceBreakdown Performance) Calculate(PlayAnalysis analysis)
    {
        var transitions = analysis.Transitions ?? new List<TransitionMetric>();
        var scores = transitions.Select(t => TuningScorer.Transition(t, Profile).Score).ToArray();
        var weights = TuningScorer.DifficultyWeights(transitions, Profile);
        double raw = TuningScorer.AggregateRaw(scores, weights, Profile);
        double final = TuningScorer.RemapProficiency(raw, Profile);
        var performance = TuningScorer.Performance(analysis, raw, Profile);
        return (raw, final, performance);
    }

    public static void Apply(PlayAnalysis analysis)
    {
        if (analysis.Transitions.Count == 0)
        {
            analysis.Proficiency = 0;
            analysis.RawAimRating = 0;
            analysis.TrainingZone = "Breakdown";
            return;
        }

        foreach (var transition in analysis.Transitions)
        {
            transition.IdealPathMatch = ScoringConfig.EffectiveIdealPathMatch(transition);
            transition.Proficiency = TransitionScore(transition);
        }

        var calculated = Calculate(analysis);
        analysis.Proficiency = calculated.FinalProficiency;
        analysis.RawAimRating = calculated.Performance.Score;
        analysis.TrainingZone = ClassifyZone(
            analysis.Proficiency,
            ScoreUtils.Accuracy(analysis.Replay.Count300, analysis.Replay.Count100, analysis.Replay.Count50, analysis.Replay.CountMiss),
            analysis.Replay.CountMiss);
    }

    public static string ClassifyZone(double proficiency, double accuracy, int misses)
    {
        double mastered = MasteredThreshold;
        double controlled = ControlledThreshold;
        double challenging = ChallengingThreshold;
        double fcMasterFloor = TuningScorer.RemapProficiency(850, Profile);
        double fcControlledFloor = TuningScorer.RemapProficiency(800, Profile);

        if (proficiency >= mastered || (misses == 0 && accuracy >= 99.5 && proficiency >= fcMasterFloor))
            return "Mastered";
        if (proficiency >= controlled || (misses == 0 && accuracy >= 99.0 && proficiency >= fcControlledFloor))
            return "Controlled";
        if (proficiency >= challenging)
            return "Challenging";
        return "Breakdown";
    }
}
