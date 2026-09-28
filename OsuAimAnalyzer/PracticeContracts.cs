namespace OsuAimAnalyzer;

/// <summary>A value snapshot from the existing resolver, not a second file index.</summary>
public sealed record PracticeSourceIdentity
{
    public string BeatmapPath { get; }
    public string BeatmapHash { get; }
    public long? SelectedPlayId { get; }
    public int PlayedMods { get; }

    public PracticeSourceIdentity(string beatmapPath, string beatmapHash, long? selectedPlayId, int playedMods)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beatmapPath);
        if (!Path.IsPathFullyQualified(beatmapPath)) throw new ArgumentException("An absolute resolved path is required.", nameof(beatmapPath));
        if (beatmapHash is null || beatmapHash.Length != 32 || !beatmapHash.All(Uri.IsHexDigit))
            throw new ArgumentException("A resolved beatmap MD5 is required.", nameof(beatmapHash));
        if (selectedPlayId is <= 0) throw new ArgumentOutOfRangeException(nameof(selectedPlayId));
        BeatmapPath = Path.GetFullPath(beatmapPath);
        BeatmapHash = beatmapHash.ToLowerInvariant();
        SelectedPlayId = selectedPlayId;
        PlayedMods = playedMods;
    }

    public static PracticeSourceIdentity FromResolvedMap(BeatmapData map, long? selectedPlayId, int playedMods)
        => new(map.Path, map.Hash, selectedPlayId, playedMods);
}

/// <summary>Serialized .osu difficulty values, not mod-adjusted/effective values.</summary>
public sealed record SerializedDifficulty
{
    public double Hp { get; }
    public double Cs { get; }
    public double Ar { get; }
    public double Od { get; }

    public SerializedDifficulty(double hp, double cs, double ar, double od)
    {
        Hp = Validate(hp, nameof(hp)); Cs = Validate(cs, nameof(cs));
        Ar = Validate(ar, nameof(ar)); Od = Validate(od, nameof(od));
    }

    private static double Validate(double value, string name)
        => double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
}

public enum PracticePitchPolicy { PreservePitch, ChangeWithRate }

/// <summary>
/// Explicit options only; no defaults choose a product policy. Rate is relative to source map time,
/// not multiplied by PlayedMods. Generated difficulty values are explicit, never implicitly compensated.
/// Upper format/export limits and support for a particular map belong to later validation.
/// </summary>
public sealed record PracticeTransformOptions
{
    public double SourceClockRate { get; }
    public double SpacingMultiplier { get; }
    public SerializedDifficulty SourceDifficulty { get; }
    public SerializedDifficulty GeneratedDifficulty { get; }
    public PracticePitchPolicy PitchPolicy { get; }

    public PracticeTransformOptions(double sourceClockRate, double spacingMultiplier,
        SerializedDifficulty sourceDifficulty, SerializedDifficulty generatedDifficulty, PracticePitchPolicy pitchPolicy)
    {
        if (!double.IsFinite(sourceClockRate) || sourceClockRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceClockRate));
        if (!double.IsFinite(spacingMultiplier) || spacingMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(spacingMultiplier));
        ArgumentNullException.ThrowIfNull(sourceDifficulty);
        ArgumentNullException.ThrowIfNull(generatedDifficulty);
        if (!Enum.IsDefined(pitchPolicy)) throw new ArgumentOutOfRangeException(nameof(pitchPolicy));
        SourceClockRate = sourceClockRate; SpacingMultiplier = spacingMultiplier;
        SourceDifficulty = sourceDifficulty; GeneratedDifficulty = generatedDifficulty; PitchPolicy = pitchPolicy;
    }
}

/// <summary>
/// Describes an already-published artifact, never a temporary staging path. Construction performs no I/O
/// and cannot certify persistence: a future exporter must validate and publish before constructing it.
/// </summary>
public sealed record PublishedPracticePackage
{
    public PracticeSourceIdentity Source { get; }
    public string PackagePath { get; }
    public IReadOnlyList<string> DifficultyEntries { get; }

    public PublishedPracticePackage(PracticeSourceIdentity source, string packagePath, IEnumerable<string> difficultyEntries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(difficultyEntries);
        if (!Path.IsPathFullyQualified(packagePath) || !Path.GetExtension(packagePath).Equals(".osz", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute published .osz path is required.", nameof(packagePath));
        var entries = difficultyEntries.ToArray();
        if (entries.Length == 0 || entries.Any(e => string.IsNullOrWhiteSpace(e) || e.Contains('/') || e.Contains('\\') || e.Contains(':') || !e.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
            || entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new ArgumentException("Distinct top-level .osu entry names are required.", nameof(difficultyEntries));
        Source = source; PackagePath = Path.GetFullPath(packagePath); DifficultyEntries = Array.AsReadOnly(entries);
    }
}
