namespace OsuAimAnalyzer;

/// <summary>Immutable numeric evidence; confidence is an association score, not a causal probability.</summary>
public sealed record RecommendationEvidence(string Metric, string UnitKind, double ObservedMedian,
    double ControlledMedian, double ControlledLowerQuartile, double ControlledUpperQuartile,
    double ConfidencePercent, double Lift, int ErrorSamples, int ComparisonSamples,
    string Direction, string Explanation, string Adjustment);

/// <summary>Detached result: neither mutable telemetry nor database handles escape the engine.</summary>
public sealed class RunDiagnosisResult
{
    public long PlayId { get; }
    public bool HasTelemetry { get; }
    public string Category { get; }
    public string SelectionKey { get; }
    public string ErrorLabel { get; }
    public int TransitionCount { get; }
    public int ErrorCount { get; }
    public double ErrorRate { get; }
    public IReadOnlyList<RecommendationEvidence> Evidence { get; }
    public IReadOnlyList<string> TrainingResponse { get; }
    public string DisplayText { get; }

    internal RunDiagnosisResult(long playId, bool hasTelemetry, string category, string selectionKey,
        AimCategoryDiagnosis? diagnosis, IEnumerable<string> trainingResponse, string displayText)
    {
        PlayId = playId; HasTelemetry = hasTelemetry; Category = category; SelectionKey = selectionKey;
        ErrorLabel = diagnosis?.ErrorLabel ?? "";
        TransitionCount = diagnosis?.TransitionCount ?? 0; ErrorCount = diagnosis?.ErrorCount ?? 0;
        ErrorRate = diagnosis?.ErrorRate ?? 0;
        Evidence = Array.AsReadOnly((diagnosis?.Factors ?? new()).Select(f => new RecommendationEvidence(
            f.Metric, f.UnitKind, f.ObservedMedian, f.ControlledMedian, f.ControlledLowerQuartile,
            f.ControlledUpperQuartile, f.Confidence, f.Lift, f.ErrorSamples, f.ComparisonSamples,
            f.Direction, f.Explanation, f.Adjustment)).ToArray());
        TrainingResponse = Array.AsReadOnly(trainingResponse.ToArray());
        DisplayText = displayText;
    }
}
