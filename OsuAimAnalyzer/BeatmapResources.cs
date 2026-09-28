namespace OsuAimAnalyzer;

public sealed record BeatmapResource(string RelativePath, string Kind);

/// <summary>Referenced assets only; no filesystem scanning, copying or assertion of existence.</summary>
public sealed class BeatmapResources
{
    public IReadOnlyList<BeatmapResource> Files { get; }
    public IReadOnlyList<string> Issues { get; }
    private BeatmapResources(List<BeatmapResource> files, List<string> issues)
    {
        Files = Array.AsReadOnly(files.Distinct().ToArray());
        Issues = Array.AsReadOnly(issues.Distinct().ToArray());
    }

    public static BeatmapResources Inspect(BeatmapDocument document)
    {
        var files = new List<BeatmapResource>();
        var issues = new List<string>();
        void Add(string value, string kind)
        {
            string path = value.Trim().Trim('"').Replace('\\', '/');
            if (path.Length == 0) return;
            if (path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(p => p is ".." or "." or "" || p.EndsWith(' ') || p.EndsWith('.'))
                || path.Any(c => c < 32 || "<>|?*\"".Contains(c)))
                issues.Add($"Unsafe {kind} resource path: {path}");
            else files.Add(new(path, kind));
        }
        foreach (var line in document.Lines.Where(BeatmapDocument.IsContent))
        {
            try
            {
                string text = line.Text.Trim();
                if (line.Section == "General" && text.StartsWith("AudioFilename:", StringComparison.Ordinal)) Add(text[(text.IndexOf(':') + 1)..], "audio");
                if (line.Section == "Events")
                {
                    var p = BeatmapDocument.Csv(text);
                    switch (p[0].Trim())
                    {
                        case "0": case "Background": case "1": case "Video":
                            if (p.Length < 3) throw new FormatException("Missing event filename.");
                            Add(p[2], p[0] is "0" or "Background" ? "background" : "video"); break;
                        case "Sample": case "5":
                            if (p.Length < 4) throw new FormatException("Missing sample filename.");
                            Add(p[3], "sample"); break;
                        case "Sprite": case "4":
                            if (p.Length < 4) throw new FormatException("Missing sprite filename.");
                            Add(p[3], "storyboard"); issues.Add("Storyboard requires export/retiming policy."); break;
                        case "2": case "Break": break;
                        default: issues.Add("Unsupported event or storyboard command; resource manifest may be incomplete."); break;
                    }
                }
                if (line.Section == "HitObjects")
                {
                    var p = BeatmapDocument.Csv(text);
                    if (p.Length < 5 || !int.TryParse(p[3], out int type)) throw new FormatException("Invalid hit object.");
                    int sampleIndex = (type & 1) != 0 ? 5 : (type & 2) != 0 ? 10 : (type & 8) != 0 ? 6 : -1;
                    if (sampleIndex < 0) issues.Add("Unsupported hit object resource format.");
                    else if (p.Length > sampleIndex)
                    {
                        var sample = p[sampleIndex].Split(':', 5);
                        if (sample.Length == 5) Add(sample[4], "hitsound");
                    }
                }
            }
            catch (FormatException e) { issues.Add(e.Message); }
        }
        // Named files cannot enumerate sample-set/index fallback assets or external .osb files.
        issues.Add("Exporter must resolve implicit sample-set/index hitsounds and check external .osb files separately.");
        return new(files, issues);
    }
}
