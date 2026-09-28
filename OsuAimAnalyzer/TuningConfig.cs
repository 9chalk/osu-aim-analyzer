using System.Text.Json;

namespace OsuAimAnalyzer;

public sealed class TuningConfig
{
    public int SchemaVersion { get; set; } = 8;
    // Proficiency component weights. They are normalized at calculation time.
    public double LandingWeight { get; set; } = 0.21;
    public double ArrivalWeight { get; set; } = 0.14;
    public double StraightnessWeight { get; set; } = 0.19;
    public double DecelerationWeight { get; set; } = 0.17;
    public double StabilityWeight { get; set; } = 0.17;
    public double IdealPathWeight { get; set; } = 0.12;

    // Play aggregation.
    public double MeanWeight { get; set; } = 0.65;
    public double LowerQuartileWeight { get; set; } = 0.25;
    public double WorstDecileWeight { get; set; } = 0.10;

    // Final map-proficiency resolution curve. Transition/component scores stay on the
    // original linear 0-1000 scale; only the aggregated play score is remapped.
    // Defaults deliberately expand the common old 750-900 band.
    public double ProficiencyResolutionStrength { get; set; } = 1.0;
    public double ProficiencyFocusRawStart { get; set; } = 750.0;
    public double ProficiencyFocusMappedStart { get; set; } = 550.0;
    public double ProficiencyFocusRawEnd { get; set; } = 900.0;
    public double ProficiencyFocusMappedEnd { get; set; } = 900.0;

    // Difficulty-aware play aggregation. Hard sections should matter more than filler.
    // 0 disables the effect. Higher values spread easy/hard transition importance further apart.
    public double DifficultyWeightStrength { get; set; } = 1.0;
    public double DifficultySpacingWeight { get; set; } = 0.50;
    public double DifficultyBpmWeight { get; set; } = 0.25;
    public double DifficultyStreakWeight { get; set; } = 0.25;
    public double DifficultyStreakSpacingThreshold { get; set; } = 140.0;
    public double DifficultyStreakFullLength { get; set; } = 8.0;
    public double DifficultyWeightFloor { get; set; } = 0.55;
    public double DifficultyWeightCeiling { get; set; } = 2.75;

    // Existing context-aware weight shifting, multiplied by this strength.
    // 0 = fixed weights, 1 = current analyzer behavior, >1 exaggerates context adaptation.
    public double ContextWeightStrength { get; set; } = 1.0;

    // Underaim handling / large-circle strictness.
    public double UnderAimInnerRecovery { get; set; } = 0.44;
    public double UnderAimMidRecovery { get; set; } = 0.31;
    public double UnderAimOuterRecovery { get; set; } = 0.20;
    public double UnderAimEdgeRecovery { get; set; } = 0.11;
    public double UnderAimOutsideRecovery { get; set; } = 0.04;
    public double LargeCircleStrictness { get; set; } = 0.65;

    // Aim performance: sustained mechanical demand + execution multiplier.
    // These defaults deliberately separate elite speed aim from slower wide aim more strongly.
    public double PerformanceChallengeWeight { get; set; } = 0.30;
    // v8: star rating is an independent demand floor. This is the floor strength, not a geometric blend weight.
    public double PerformanceStarWeight { get; set; } = 1.00;
    public double PerformanceBpmWeight { get; set; } = 0.35;
    public double PerformanceSpacingWeight { get; set; } = 0.12;
    public double PerformanceArWeight { get; set; } = 0.13;
    public double PerformanceDensityWeight { get; set; } = 0.10;
    public double PerformanceScale { get; set; } = 500.0;
    public double PerformanceReferenceProficiency { get; set; } = 850.0;
    public double PerformanceExecutionCurve { get; set; } = 450.0;
    public double PerformanceExecutionWeight { get; set; } = 1.0;
    public double PerformanceHardExecutionMeanWeight { get; set; } = 0.90;
    public double PerformanceHardExecutionLowerQuartileWeight { get; set; } = 0.10;

    // Nonlinear sustained-section demand controls.
    public double PerformanceVelocityExponent { get; set; } = 1.50;
    public double PerformanceSpacingExponent { get; set; } = 0.35;
    public double PerformanceStarDemandExponent { get; set; } = 1.15;
    public double PerformanceDemandCurve { get; set; } = 1.25;
    public double PerformanceDemandPercentile { get; set; } = 0.85;
    public double PerformanceMinHardSectionStreak { get; set; } = 4.0;

    // Full-map consistency bonus. This is intentionally BONUS-ONLY: misses reduce how much
    // extra credit the replay earns for sustained conversion, but do not globally nuke the
    // underlying capability score demonstrated by hard aim sections.
    public double PerformanceConsistencyMaxBonus { get; set; } = 0.65;
    public double PerformanceConsistencyDurationReferenceSeconds { get; set; } = 120.0;
    public double PerformanceConsistencyDurationExponent { get; set; } = 1.0;
    public double PerformanceConsistencyMissTolerance { get; set; } = 4.0;
    public double PerformanceConsistencyMissWeight { get; set; } = 0.45;
    public double PerformanceConsistencyControlWeight { get; set; } = 0.55;
    public double PerformanceConsistencyControlFloor { get; set; } = 650.0;
    public double PerformanceConsistencyControlFull { get; set; } = 900.0;

    // Reference demand values = 1.0 in the performance model.
    // RefChallenge is now a transparent 100-based sustained challenge reference.
    public double RefChallenge { get; set; } = 100.0;
    public double RefVelocity { get; set; } = 1800.0;
    public double RefStar { get; set; } = 6.0;
    public double RefBpm { get; set; } = 240.0;
    public double RefSpacing { get; set; } = 200.0;
    public double RefAr { get; set; } = 9.5;
    public double RefDensity { get; set; } = 4.0;

    // The production analyzer ships a fixed profile. These methods remain only so the
    // tuner-derived Replay Tools code can compile without persisting a hidden scoring lab.
    public static TuningConfig LoadOrDefault() => ProductionScoring.CreateProfile();
    public void Save() { }

    public static TuningConfig Defaults() => new();
}
