using System.Text.Json;

namespace OsuAimAnalyzer;

public sealed class AppPaths
{
    public required string DataDirectory { get; init; }
    public required string SettingsPath { get; init; }
    public required string DatabasePath { get; init; }
    public required string CollectionStatePath { get; init; }

    public static AppPaths Create()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OsuAimAnalyzer");
        return new AppPaths
        {
            DataDirectory = root,
            SettingsPath = Path.Combine(root, "settings.json"),
            DatabasePath = Path.Combine(root, "aim-analysis.sqlite3"),
            CollectionStatePath = Path.Combine(root, "collection-routing-state.json")
        };
    }
}

public sealed class AppSettings
{
    public string OsuDirectory { get; set; } = "";
    public string SongsDirectory { get; set; } = "";
    public string ReplayDirectory { get; set; } = "";
    public int SessionGapMinutes { get; set; } = 45;
    public string ReplayBackfillRange { get; set; } = "30 days";
    public bool WatchReplaysAutomatically { get; set; } = true;
    public bool AnalyzeOnlyPureJumps { get; set; } = true;
    public int ResampleIntervalMs { get; set; } = 4;
    public bool AutoCollectionEnabled { get; set; } = false;
    public bool CollectionRulesExclusive { get; set; } = true;
    public string CollectionBackfillRange { get; set; } = "30 days";
    public List<CollectionRule> CollectionRules { get; set; } = new();

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void FillDefaults()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var common = Path.Combine(local, "osu!");
        if (string.IsNullOrWhiteSpace(OsuDirectory) && Directory.Exists(common)) OsuDirectory = common;
        if (string.IsNullOrWhiteSpace(SongsDirectory) && !string.IsNullOrWhiteSpace(OsuDirectory)) SongsDirectory = Path.Combine(OsuDirectory, "Songs");
        if (string.IsNullOrWhiteSpace(ReplayDirectory) && !string.IsNullOrWhiteSpace(OsuDirectory)) ReplayDirectory = Path.Combine(OsuDirectory, "Data", "r");
        if (CollectionRules == null) CollectionRules = new();
        if (CollectionRules.Count == 0 || LooksLikeLegacyDefaultBands(CollectionRules))
            CollectionRules = ProductionDefaultCollectionRules();
    }

    private static bool LooksLikeLegacyDefaultBands(List<CollectionRule> rules)
    {
        if (rules.Count != 6) return false;

        string[] names = {
            "Aim · 700-749", "Aim · 750-799", "Aim · 800-849",
            "Aim · 850-899", "Aim · 900-949", "Aim · 950-1000"
        };
        double[] mins = { 700, 750, 800, 850, 900, 950 };
        double[] maxes = { 749.999, 799.999, 849.999, 899.999, 949.999, 1000 };

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!rule.Enabled || !string.Equals(rule.CollectionName, names[i], StringComparison.Ordinal)) return false;
            if (Math.Abs(rule.MinProficiency - mins[i]) > .01 || Math.Abs(rule.MaxProficiency - maxes[i]) > .01) return false;
        }
        return true;
    }

    public static List<CollectionRule> ProductionDefaultCollectionRules() => new()
    {
        new() { CollectionName = "Aim · 500-599", MinProficiency = 500, MaxProficiency = 599.999 },
        new() { CollectionName = "Aim · 600-699", MinProficiency = 600, MaxProficiency = 699.999 },
        new() { CollectionName = "Aim · 700-799", MinProficiency = 700, MaxProficiency = 799.999 },
        new() { CollectionName = "Aim · 800-899", MinProficiency = 800, MaxProficiency = 899.999 },
        new() { CollectionName = "Aim · 900-949", MinProficiency = 900, MaxProficiency = 949.999 },
        new() { CollectionName = "Aim · 950-1000", MinProficiency = 950, MaxProficiency = 1000 },
    };
}
