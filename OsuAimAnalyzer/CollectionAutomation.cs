using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OsuAimAnalyzer;

public sealed class CollectionRule
{
    public bool Enabled { get; set; } = true;
    public string CollectionName { get; set; } = "Aim · 700-799";
    public double MinProficiency { get; set; } = 700;
    public double MaxProficiency { get; set; } = 799.999;

    public bool Matches(double proficiency)
        => Enabled && proficiency >= MinProficiency && (MaxProficiency >= 1000 ? proficiency <= MaxProficiency : proficiency < MaxProficiency + 0.001);
}

internal sealed class ManagedCollectionState
{
    public Dictionary<string, List<string>> Collections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OsuCollectionDatabase
{
    public int Version { get; set; }
    public List<OsuCollection> Collections { get; set; } = new();

    public static OsuCollectionDatabase Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);
        var db = new OsuCollectionDatabase { Version = br.ReadInt32() };
        int count = br.ReadInt32();
        if (count < 0 || count > 100000) throw new InvalidDataException($"Invalid collection count: {count}");
        for (int i = 0; i < count; i++)
        {
            string name = ReadOsuString(br);
            int maps = br.ReadInt32();
            if (maps < 0 || maps > 2_000_000) throw new InvalidDataException($"Invalid map count in collection '{name}': {maps}");
            var c = new OsuCollection { Name = name };
            for (int j = 0; j < maps; j++)
            {
                string hash = ReadOsuString(br);
                if (!string.IsNullOrWhiteSpace(hash)) c.BeatmapHashes.Add(hash.ToLowerInvariant());
            }
            db.Collections.Add(c);
        }
        return db;
    }

    public static OsuCollectionDatabase CreateEmpty(int version)
        => new() { Version = version > 0 ? version : 20250101 };

    public void SaveAtomic(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".aimanalyzer.tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
        {
            bw.Write(Version);
            bw.Write(Collections.Count);
            foreach (var c in Collections)
            {
                WriteOsuString(bw, c.Name);
                var hashes = c.BeatmapHashes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bw.Write(hashes.Count);
                foreach (string h in hashes) WriteOsuString(bw, h.ToLowerInvariant());
            }
            bw.Flush();
            fs.Flush(true);
        }

        Exception? last = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                Thread.Sleep(50 + attempt * 30);
            }
        }
        try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        throw new IOException("Could not atomically update collection.db", last);
    }

    private static string ReadOsuString(BinaryReader br)
    {
        byte marker = br.ReadByte();
        if (marker == 0x00) return "";
        if (marker != 0x0B) throw new InvalidDataException($"Unexpected osu! string marker 0x{marker:X2}");
        int length = checked((int)ReadUleb128(br));
        if (length == 0) return "";
        return Encoding.UTF8.GetString(br.ReadBytes(length));
    }

    private static void WriteOsuString(BinaryWriter bw, string value)
    {
        if (string.IsNullOrEmpty(value)) { bw.Write((byte)0x00); return; }
        byte[] data = Encoding.UTF8.GetBytes(value);
        bw.Write((byte)0x0B);
        WriteUleb128(bw, (uint)data.Length);
        bw.Write(data);
    }

    private static uint ReadUleb128(BinaryReader br)
    {
        uint result = 0; int shift = 0;
        while (true)
        {
            byte b = br.ReadByte();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 28) throw new InvalidDataException("ULEB128 value is too large.");
        }
    }

    private static void WriteUleb128(BinaryWriter bw, uint value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            bw.Write(b);
        } while (value != 0);
    }
}

internal sealed class OsuCollection
{
    public string Name { get; set; } = "";
    public HashSet<string> BeatmapHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CollectionBackfillResult
{
    public int PlaysConsidered { get; set; }
    public int UniqueMaps { get; set; }
    public int CategorizedMaps { get; set; }
    public int UnmatchedMaps { get; set; }
    public int MembershipsAdded { get; set; }
}

public sealed class CollectionRuleService : IDisposable
{
    private readonly AppSettings settings;
    private readonly AppPaths paths;
    private readonly object gate = new();
    private ManagedCollectionState state = new();
    private FileSystemWatcher? watcher;
    private bool backedUpThisSession;
    private DateTime suppressWatcherUntilUtc;
    private CancellationTokenSource? remergeCts;

    public event Action<string>? StatusChanged;

    public CollectionRuleService(AppSettings settings, AppPaths paths)
    {
        this.settings = settings;
        this.paths = paths;
        LoadState();
    }

    public string CollectionDbPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(settings.OsuDirectory)) return "";
            string primary = Path.Combine(settings.OsuDirectory, "collection.db");
            string plural = Path.Combine(settings.OsuDirectory, "collections.db");
            return File.Exists(primary) || !File.Exists(plural) ? primary : plural;
        }
    }

    public string BackupDirectory => Path.Combine(paths.DataDirectory, "CollectionBackups");

    public void Start()
    {
        StopWatcher();
        string db = CollectionDbPath;
        string? dir = string.IsNullOrWhiteSpace(db) ? null : Path.GetDirectoryName(db);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        watcher = new FileSystemWatcher(dir, Path.GetFileName(db))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnCollectionDbChanged;
        watcher.Created += OnCollectionDbChanged;
        watcher.Renamed += (_, _) => ScheduleRemerge();
    }

    public void RefreshConfiguration()
    {
        Start();
        if (settings.AutoCollectionEnabled) EnsureCollectionsExist();
    }

    public void RoutePlay(PlayRow play)
    {
        if (!settings.AutoCollectionEnabled || settings.CollectionRules.Count == 0 || string.IsNullOrWhiteSpace(play.BeatmapHash)) return;
        lock (gate)
        {
            string hash = play.BeatmapHash.ToLowerInvariant();
            var enabledNames = settings.CollectionRules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.CollectionName))
                .Select(r => r.CollectionName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string name in enabledNames)
                if (!state.Collections.ContainsKey(name)) state.Collections[name] = new List<string>();

            if (settings.CollectionRulesExclusive)
                foreach (string name in enabledNames) state.Collections[name].RemoveAll(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase));

            var matches = settings.CollectionRules.Where(r => r.Matches(play.Proficiency) && !string.IsNullOrWhiteSpace(r.CollectionName)).ToList();
            foreach (var r in matches)
            {
                string name = r.CollectionName.Trim();
                if (!state.Collections.TryGetValue(name, out var list)) state.Collections[name] = list = new List<string>();
                if (!list.Contains(hash, StringComparer.OrdinalIgnoreCase)) list.Add(hash);
            }
            SaveState();
            ApplyManagedStateToDisk();
            StatusChanged?.Invoke(matches.Count == 0
                ? $"Collections: {play.Proficiency:0} did not match an enabled range."
                : $"Collections: {play.Map} → {string.Join(", ", matches.Select(m => m.CollectionName))}");
        }
    }

    public CollectionBackfillResult BackfillFromPlays(IEnumerable<PlayRow> plays)
    {
        lock (gate)
        {
            var source = plays.Where(p => !string.IsNullOrWhiteSpace(p.BeatmapHash)).ToList();
            var latest = source
                .GroupBy(p => p.BeatmapHash, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.TimestampUtc).First())
                .ToList();

            var result = new CollectionBackfillResult
            {
                PlaysConsidered = source.Count,
                UniqueMaps = latest.Count
            };

            var enabledNames = settings.CollectionRules
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.CollectionName))
                .Select(r => r.CollectionName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string name in enabledNames)
                if (!state.Collections.ContainsKey(name)) state.Collections[name] = new List<string>();

            foreach (var play in latest)
            {
                string hash = play.BeatmapHash.ToLowerInvariant();
                if (settings.CollectionRulesExclusive)
                {
                    foreach (string name in enabledNames)
                        state.Collections[name].RemoveAll(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase));
                }

                var matches = settings.CollectionRules
                    .Where(r => r.Matches(play.Proficiency) && !string.IsNullOrWhiteSpace(r.CollectionName))
                    .ToList();

                if (matches.Count == 0)
                {
                    result.UnmatchedMaps++;
                    continue;
                }

                result.CategorizedMaps++;
                foreach (var rule in matches)
                {
                    string name = rule.CollectionName.Trim();
                    if (!state.Collections.TryGetValue(name, out var list)) state.Collections[name] = list = new List<string>();
                    if (!list.Contains(hash, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(hash);
                        result.MembershipsAdded++;
                    }
                }
            }

            SaveState();
            ApplyManagedStateToDisk();
            StatusChanged?.Invoke($"Collections backfill: categorized {result.CategorizedMaps:N0}/{result.UniqueMaps:N0} unique maps from {result.PlaysConsidered:N0} analyzed plays; {result.UnmatchedMaps:N0} did not match an enabled range.");
            return result;
        }
    }

    public void RebuildFromLatestPlays(IEnumerable<PlayRow> plays)
    {
        lock (gate)
        {
            var names = settings.CollectionRules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.CollectionName))
                .Select(r => r.CollectionName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string n in names) state.Collections[n] = new List<string>();

            foreach (var p in plays.Where(p => !string.IsNullOrWhiteSpace(p.BeatmapHash))
                         .GroupBy(p => p.BeatmapHash, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.OrderByDescending(p => p.TimestampUtc).First()))
            {
                foreach (var r in settings.CollectionRules.Where(r => r.Matches(p.Proficiency)))
                {
                    string name = r.CollectionName.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!state.Collections.TryGetValue(name, out var list)) state.Collections[name] = list = new List<string>();
                    if (!list.Contains(p.BeatmapHash, StringComparer.OrdinalIgnoreCase)) list.Add(p.BeatmapHash.ToLowerInvariant());
                }
            }
            SaveState();
            ApplyManagedStateToDisk();
            StatusChanged?.Invoke($"Collections rebuilt from latest analyzed run on each map ({state.Collections.Sum(kv => kv.Value.Count):N0} memberships). ");
        }
    }

    public void EnsureCollectionsExist()
    {
        lock (gate)
        {
            foreach (var r in settings.CollectionRules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.CollectionName)))
                if (!state.Collections.ContainsKey(r.CollectionName.Trim())) state.Collections[r.CollectionName.Trim()] = new List<string>();
            SaveState();
            ApplyManagedStateToDisk();
        }
    }

    public string BackupNow()
    {
        lock (gate)
        {
            string db = CollectionDbPath;
            if (!File.Exists(db)) throw new FileNotFoundException("collection.db was not found.", db);
            Directory.CreateDirectory(BackupDirectory);
            string dest = Path.Combine(BackupDirectory, $"collection_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db");
            File.Copy(db, dest, overwrite: false);
            backedUpThisSession = true;
            StatusChanged?.Invoke($"Collection backup created: {Path.GetFileName(dest)}");
            return dest;
        }
    }

    public string? RestoreLatestBackup()
    {
        lock (gate)
        {
            if (!Directory.Exists(BackupDirectory)) return null;
            string? latest = Directory.EnumerateFiles(BackupDirectory, "collection_*.db")
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest == null) return null;
            string db = CollectionDbPath;
            if (string.IsNullOrWhiteSpace(db)) return null;
            suppressWatcherUntilUtc = DateTime.UtcNow.AddSeconds(1);
            File.Copy(latest, db, overwrite: true);
            StatusChanged?.Invoke($"Restored collection backup: {Path.GetFileName(latest)}");
            return latest;
        }
    }

    public int CountFor(string collectionName)
    {
        lock (gate) return state.Collections.TryGetValue(collectionName, out var list) ? list.Distinct(StringComparer.OrdinalIgnoreCase).Count() : 0;
    }

    public bool IsOsuRunning()
    {
        try { return Process.GetProcessesByName("osu!").Length > 0 || Process.GetProcessesByName("osu").Length > 0; }
        catch { return false; }
    }

    private void OnCollectionDbChanged(object? sender, FileSystemEventArgs e)
    {
        if (!settings.AutoCollectionEnabled || DateTime.UtcNow < suppressWatcherUntilUtc) return;
        ScheduleRemerge();
    }

    private void ScheduleRemerge()
    {
        remergeCts?.Cancel();
        remergeCts?.Dispose();
        remergeCts = new CancellationTokenSource();
        var token = remergeCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450, token).ConfigureAwait(false);
                lock (gate) ApplyManagedStateToDisk();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusChanged?.Invoke("Collection re-merge failed: " + ex.Message); }
        }, token);
    }

    private void ApplyManagedStateToDisk()
    {
        string dbPath = CollectionDbPath;
        if (string.IsNullOrWhiteSpace(dbPath)) throw new InvalidOperationException("osu! folder is not configured.");
        if (!backedUpThisSession && File.Exists(dbPath)) BackupNow();

        OsuCollectionDatabase db;
        if (File.Exists(dbPath)) db = OsuCollectionDatabase.Load(dbPath);
        else db = OsuCollectionDatabase.CreateEmpty(ReadOsuDbVersion());

        foreach (var kv in state.Collections)
        {
            var existing = db.Collections.FirstOrDefault(c => c.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new OsuCollection { Name = kv.Key };
                db.Collections.Add(existing);
            }
            // Analyzer-managed collections are intentionally authoritative so that replaying
            // a map into a new proficiency range can move it cleanly between bands.
            existing.BeatmapHashes = kv.Value.Where(h => !string.IsNullOrWhiteSpace(h)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        suppressWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(900);
        db.SaveAtomic(dbPath);
    }

    private int ReadOsuDbVersion()
    {
        try
        {
            string path = Path.Combine(settings.OsuDirectory, "osu!.db");
            if (!File.Exists(path)) return 20250101;
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            return br.ReadInt32();
        }
        catch { return 20250101; }
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(paths.CollectionStatePath))
                state = JsonSerializer.Deserialize<ManagedCollectionState>(File.ReadAllText(paths.CollectionStatePath)) ?? new ManagedCollectionState();
        }
        catch { state = new ManagedCollectionState(); }
        state.Collections = new Dictionary<string, List<string>>(state.Collections ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
    }

    private void SaveState()
    {
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.CollectionStatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void StopWatcher()
    {
        if (watcher == null) return;
        watcher.EnableRaisingEvents = false;
        watcher.Dispose(); watcher = null;
    }

    public void Dispose()
    {
        remergeCts?.Cancel(); remergeCts?.Dispose();
        StopWatcher();
    }
}
