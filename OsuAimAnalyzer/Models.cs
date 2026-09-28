namespace OsuAimAnalyzer;

public enum HitObjectKind { Circle, Slider, Spinner, Other }

public sealed class ReplayData
{
    public byte Mode { get; set; }
    public int Version { get; set; }
    public string BeatmapHash { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string ReplayHash { get; set; } = "";
    public ushort Count300 { get; set; }
    public ushort Count100 { get; set; }
    public ushort Count50 { get; set; }
    public ushort CountGeki { get; set; }
    public ushort CountKatu { get; set; }
    public ushort CountMiss { get; set; }
    public int Score { get; set; }
    public ushort MaxCombo { get; set; }
    public bool Perfect { get; set; }
    public int Mods { get; set; }
    public DateTime TimestampUtc { get; set; }
    public List<ReplayFrame> Frames { get; set; } = new();
}

public readonly record struct ReplayFrame(long TimeMs, double X, double Y, int Keys);

public sealed class BeatmapData
{
    public string Hash { get; set; } = "";
    public string Path { get; set; } = "";
    public string BackgroundPath { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Version { get; set; } = "";
    public int BeatmapId { get; set; }
    public int BeatmapSetId { get; set; }
    public int RankedStatus { get; set; }
    public byte Mode { get; set; }
    public double AR { get; set; }
    public double CS { get; set; }
    public double OD { get; set; }
    public double HP { get; set; }
    public double SliderMultiplier { get; set; } = 1.4;
    public Dictionary<int, double> StandardStars { get; set; } = new();
    public List<TimingPointData> TimingPoints { get; set; } = new();
    public List<HitObjectData> HitObjects { get; set; } = new();

    public string DisplayName => $"{Artist} - {Title} [{Version}]";
}

public readonly record struct TimingPointData(double TimeMs, double BeatLength, bool Uninherited);

public sealed class HitObjectData
{
    public int Index { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public long TimeMs { get; set; }
    public HitObjectKind Kind { get; set; }
}

public sealed class TransitionMetric
{
    public long PlayId { get; set; }
    public int ObjectIndex { get; set; }
    public long TimeMs { get; set; }
    public double Bpm { get; set; }
    public double Spacing { get; set; }
    public double NormalizedSpacing { get; set; }
    public double IntervalMs { get; set; }
    public double Velocity { get; set; }
    public double Density { get; set; }
    public double Angle { get; set; }
    public double Straightness { get; set; }
    public double Landing { get; set; }
    public double Arrival { get; set; }
    public double Stability { get; set; }
    public double Deceleration { get; set; }
    public double IdealPathMatch { get; set; }
    public double AimTension { get; set; }
    public double Proficiency { get; set; }
    public double AxialError { get; set; }
    public double LateralError { get; set; }
    public string ErrorClass { get; set; } = "Clean";
    public double Challenge { get; set; }
}

public sealed class PlayAnalysis
{
    public ReplayData Replay { get; set; } = new();
    public BeatmapData Beatmap { get; set; } = new();
    public string ReplayPath { get; set; } = "";
    public double StarRating { get; set; }
    public string StarSource { get; set; } = "unknown";
    public double ClockRate { get; set; } = 1;
    public double EffectiveAr { get; set; }
    public double MeanBpm { get; set; }
    public double SpacingP75 { get; set; }
    public double SpacingP90 { get; set; }
    public double MeanDensity { get; set; }
    public double DensityP90 { get; set; }
    public double MeanStraightness { get; set; }
    public double MeanLanding { get; set; }
    public double MeanArrival { get; set; }
    public double MeanStability { get; set; }
    public double MeanDeceleration { get; set; }
    public double MeanIdealPathMatch { get; set; }
    public double MeanTension { get; set; }
    public double Proficiency { get; set; }
    public double AimChallenge { get; set; }
    public double RawAimRating { get; set; }
    public int TransitionCount { get; set; }
    public string TrainingZone { get; set; } = "Unknown";
    public List<TransitionMetric> Transitions { get; set; } = new();
}

public sealed class PlayRow
{
    public long Id { get; set; }
    public string ReplayHash { get; set; } = "";
    public string BeatmapHash { get; set; } = "";
    public string ReplayPath { get; set; } = "";
    public string BeatmapPath { get; set; } = "";
    public DateTime TimestampUtc { get; set; }
    public string Map { get; set; } = "";
    public string Player { get; set; } = "";
    public int Mods { get; set; }
    public string ModsText { get; set; } = "NM";
    public double Accuracy { get; set; }
    public string Grade { get; set; } = "D";
    public int MissCount { get; set; }
    public double StarRating { get; set; }
    public double EffectiveAr { get; set; }
    public double MeanBpm { get; set; }
    public double SpacingP75 { get; set; }
    public double DensityP90 { get; set; }
    public double Proficiency { get; set; }
    public double AimChallenge { get; set; }
    public double RawAimRating { get; set; }
    public int TransitionCount { get; set; }
    public string Zone { get; set; } = "";
    public double Straightness { get; set; }
    public double Landing { get; set; }
    public double Arrival { get; set; }
    public double Stability { get; set; }
    public double Deceleration { get; set; }
    public double IdealPathMatch { get; set; }
    public double AimTension { get; set; }
    public double PpEstimate { get; set; }
}

public sealed class DiagnosticCohort
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double BpmMin { get; set; }
    public double BpmMax { get; set; }
    public double BpmMedian { get; set; }
    public double SpacingMin { get; set; }
    public double SpacingMax { get; set; }
    public double SpacingMedian { get; set; }
    public double DensityMin { get; set; }
    public double DensityMax { get; set; }
    public double DensityMedian { get; set; }
    public double StarMin { get; set; }
    public double StarMax { get; set; }
    public double StarMedian { get; set; }
    public double ArMin { get; set; }
    public double ArMax { get; set; }
    public double ArMedian { get; set; }
    public double IntervalMedian { get; set; }
    public double Proficiency { get; set; }
    public double Straightness { get; set; }
    public double Landing { get; set; }
    public double Arrival { get; set; }
    public double Stability { get; set; }
    public double Deceleration { get; set; }
    public double AimTension { get; set; }
    public double TensionBaseline { get; set; }
    public double TensionDelta { get; set; }
    public double Accuracy { get; set; }
    public double PeakAimRating { get; set; }
    public string Weakest { get; set; } = "";
    public double WeakestScore { get; set; }
    public double WeakestBaseline { get; set; }
    public double WeakestDelta { get; set; }
    public string Insight { get; set; } = "";
}

public sealed class TrainingRecommendation
{
    public string Range { get; set; } = "";
    public string Status { get; set; } = "";
    public int Samples { get; set; }
    public double CurrentProficiency { get; set; }
    public string TargetBpm { get; set; } = "";
    public string TargetSpacing { get; set; } = "";
    public string TargetDensity { get; set; } = "";
    public string TargetAr { get; set; } = "";
    public string TargetStar { get; set; } = "";
    public string Focus { get; set; } = "";
    public double TargetBpmMid { get; set; }
    public double TargetSpacingMid { get; set; }
    public double TargetDensityMid { get; set; }
    public double TargetArMid { get; set; }
    public double TargetStarMid { get; set; }
    public string TargetSummary { get; set; } = "";
    public string Rationale { get; set; } = "";
    public double CurrentTension { get; set; }
    public List<TrainingMapExample> ExampleMaps { get; set; } = new();
}

public sealed class TrainingMapExample
{
    public string Map { get; set; } = "";
    public string Mods { get; set; } = "NM";
    public double Accuracy { get; set; }
    public double Proficiency { get; set; }
    public double AimPerformance { get; set; }
    public double Bpm { get; set; }
    public double Spacing { get; set; }
    public double Density { get; set; }
    public double Ar { get; set; }
    public double Star { get; set; }
    public double FitScore { get; set; }
    public double AimTension { get; set; }
}


public sealed class AimAspectScore
{
    public string Name { get; set; } = "";
    public double Score { get; set; }
    public string Interpretation { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class AimAnalysisReport
{
    public string Preset { get; set; } = "All aim";
    public string HistoryRange { get; set; } = "All time";
    public int PlayCount { get; set; }
    public int TransitionCount { get; set; }
    public double AverageProficiency { get; set; }
    public double AimRating { get; set; }
    public double AverageTension { get; set; }
    public List<AimAspectScore> Aspects { get; set; } = new();
    public List<string> Traits { get; set; } = new();
    public string Summary { get; set; } = "";
    public string StrengthText { get; set; } = "";
    public string WeaknessText { get; set; } = "";
    public string StyleText { get; set; } = "";
}

public sealed class ErrorVisualSample
{
    public TransitionMetric Metric { get; set; } = new();
    public HitObjectData From { get; set; } = new();
    public HitObjectData To { get; set; } = new();
    public List<CursorPoint> Path { get; set; } = new();
    public CursorPoint TapPoint { get; set; }
    public double TapOffsetMs { get; set; }
    public bool HasTap { get; set; }
    public double CircleRadius { get; set; } = 28;
}
