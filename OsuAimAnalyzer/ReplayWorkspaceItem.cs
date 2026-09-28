namespace OsuAimAnalyzer;

public sealed class ReplayWorkspaceItem
{
    public bool IsSynthetic { get; set; }
    public string SyntheticLabel { get; set; } = "";
    public string Key { get; set; } = "";
    public string Path { get; set; } = "";
    public string Signature { get; set; } = "";
    public ReplayData Replay { get; set; } = new();
    public BeatmapData Beatmap { get; set; } = new();
    public PlayAnalysis Analysis { get; set; } = new();
    public List<ProficiencyBreakdown> TunedTransitions { get; set; } = new();
    public DifficultyWeightInfo[] DifficultyWeights { get; set; } = Array.Empty<DifficultyWeightInfo>();
    public double RawTunedProficiency { get; set; }
    public double TunedProficiency { get; set; }
    public PerformanceBreakdown? TunedPerformance { get; set; }
}

public sealed class LocalReplayEntry
{
    public string Path { get; set; } = "";
    public ReplayData Header { get; set; } = new();
    public string MapName { get; set; } = "";
}
