using System.Security.Cryptography;

namespace OsuAimAnalyzer;

public sealed class BeatmapResolver
{
    private readonly AppSettings settings;
    private Dictionary<string, OsuDbBeatmap> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> fallbackHashPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BeatmapData> parsedBeatmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private bool fullSongsHashIndexBuilt;
    private FileSystemWatcher? songsWatcher;
    private readonly HashSet<string> pendingSongHashes = new(StringComparer.OrdinalIgnoreCase);

    public int CachedBeatmapCount
    {
        get
        {
            lock (gate) return cache.Count + fallbackHashPaths.Count;
        }
    }

    public BeatmapResolver(AppSettings settings) => this.settings = settings;

    public Task RebuildAsync(IProgress<string>? progress = null, CancellationToken token = default)
        => Task.Run(() => Rebuild(progress, token), token);

    public Task WarmRecentSongsAsync(TimeSpan age, CancellationToken token = default)
        => Task.Run(() =>
        {
            if (!Directory.Exists(settings.SongsDirectory)) return;
            DateTime cutoff = DateTime.UtcNow - age;
            foreach (var file in Directory.EnumerateFiles(settings.SongsDirectory, "*.osu", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff) HashAndCache(file, wantedHash: null, out _);
                }
                catch { }
            }
        }, token);

    private void Rebuild(IProgress<string>? progress, CancellationToken token)
    {
        lock (gate)
        {
            cache = new Dictionary<string, OsuDbBeatmap>(StringComparer.OrdinalIgnoreCase);
            fallbackHashPaths.Clear();
            parsedBeatmaps.Clear();
            fullSongsHashIndexBuilt = false;
        }

        string db = Path.Combine(settings.OsuDirectory, "osu!.db");
        if (File.Exists(db))
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    progress?.Report(attempt == 1 ? "Reading osu!.db…" : $"Retrying osu!.db ({attempt}/3)…");
                    var loaded = OsuDbReader.Read(db);
                    lock (gate) cache = loaded;
                    StartSongsWatcher();
                    progress?.Report($"Loaded {loaded.Count:N0} beatmaps from osu!.db.");
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(250);
                }
            }
            progress?.Report($"osu!.db parse failed ({last?.Message}); falling back to Songs scan.");
        }

        if (!Directory.Exists(settings.SongsDirectory)) return;
        BuildFullSongsHashIndex(progress, token);
        StartSongsWatcher();
    }

    private void StartSongsWatcher()
    {
        try
        {
            songsWatcher?.Dispose();
            songsWatcher = null;
            if (!Directory.Exists(settings.SongsDirectory)) return;
            songsWatcher = new FileSystemWatcher(settings.SongsDirectory, "*.osu")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            songsWatcher.Created += (_, e) => QueueSongHash(e.FullPath);
            songsWatcher.Changed += (_, e) => QueueSongHash(e.FullPath);
            songsWatcher.Renamed += (_, e) => QueueSongHash(e.FullPath);
        }
        catch { }
    }

    private void QueueSongHash(string path)
    {
        if (!path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) return;
        lock (gate) if (!pendingSongHashes.Add(path)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                // Let imports/generators finish replacing the file before hashing it.
                await Task.Delay(75).ConfigureAwait(false);
                for (int i = 0; i < 5; i++)
                {
                    if (File.Exists(path) && HashAndCache(path, wantedHash: null, out _)) return;
                    await Task.Delay(60).ConfigureAwait(false);
                }
            }
            finally { lock (gate) pendingSongHashes.Remove(path); }
        });
    }

    public string DisplayNameForHash(string md5)
    {
        md5 = NormalizeHash(md5);
        if (md5.Length == 0) return "Unknown map";
        lock (gate)
        {
            if (cache.TryGetValue(md5, out var c))
                return $"{c.Artist} - {c.Title} [{c.Difficulty}]";
            if (fallbackHashPaths.TryGetValue(md5, out var path) && !string.IsNullOrWhiteSpace(path))
                return Path.GetFileNameWithoutExtension(path);
        }
        return $"Unknown map · {ShortHash(md5)}";
    }

    public BeatmapData? Resolve(string md5, bool refreshDbOnMiss = true)
    {
        md5 = NormalizeHash(md5);
        if (md5.Length == 0) return null;

        BeatmapData? resolved = ResolveCached(md5);
        if (resolved != null) return resolved;

        // New practice maps can be imported while the analyzer is already running. Stable may update
        // osu!.db a moment later, so refresh it once before escalating to a direct Songs hash scan.
        if (refreshDbOnMiss && RefreshDb())
            return Resolve(md5, refreshDbOnMiss: false);

        return null;
    }

    /// <summary>
    /// Slow-path resolver for fresh/local practice maps that exist in Songs but are not yet present in osu!.db.
    /// It hashes recently modified .osu files first, then builds a complete in-memory Songs hash index only if needed.
    /// </summary>
    public Task<BeatmapData?> ResolveFromSongsAsync(string md5, IProgress<string>? progress = null, CancellationToken token = default)
        => Task.Run(() => ResolveFromSongs(md5, progress, token), token);

    private BeatmapData? ResolveFromSongs(string md5, IProgress<string>? progress, CancellationToken token)
    {
        md5 = NormalizeHash(md5);
        if (md5.Length == 0 || !Directory.Exists(settings.SongsDirectory)) return null;

        var cached = ResolveCached(md5);
        if (cached != null) return cached;

        bool alreadyFull;
        lock (gate) alreadyFull = fullSongsHashIndexBuilt;

        // Even after a full index has been built, new trainer/merger maps can appear while the app stays open.
        // Always check recently modified files again on a miss; only skip the expensive old-file pass.
        progress?.Report($"Map is not in osu!.db · searching recently changed .osu files for {ShortHash(md5)}…");

        // Merger/trainer maps are usually brand-new. Hash only recently modified files first so these
        // resolve quickly without immediately chewing through a huge Songs library.
        DateTime recentCutoff = DateTime.UtcNow.AddDays(-3);
        var older = new List<string>();
        int enumerated = 0;
        int recentHashed = 0;

        foreach (string file in Directory.EnumerateFiles(settings.SongsDirectory, "*.osu", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            enumerated++;

            DateTime write;
            try { write = File.GetLastWriteTimeUtc(file); }
            catch { continue; }

            if (write >= recentCutoff)
            {
                recentHashed++;
                if (HashAndCache(file, md5, out var found))
                {
                    progress?.Report($"Resolved local practice map from Songs: {Path.GetFileName(file)}");
                    return found;
                }
                if (recentHashed % 250 == 0)
                    progress?.Report($"Searching recent Songs files… {recentHashed:N0} hashed");
            }
            else
            {
                older.Add(file);
            }
        }

        if (alreadyFull)
        {
            progress?.Report($"No recent Songs file matches replay hash {ShortHash(md5)}.");
            return null;
        }

        // If the replay is older or the file timestamps were preserved by an import/copy, do a complete
        // hash pass. Every computed hash is cached, so subsequent misses during this run are cheap.
        progress?.Report($"No recent hash match · building full Songs hash index ({enumerated:N0} maps)…");
        int fullHashed = recentHashed;
        foreach (string file in older)
        {
            token.ThrowIfCancellationRequested();
            fullHashed++;
            if (HashAndCache(file, md5, out var found))
            {
                progress?.Report($"Resolved map from full Songs scan: {Path.GetFileName(file)}");
                return found;
            }
            if (fullHashed % 1000 == 0)
                progress?.Report($"Full Songs hash scan… {fullHashed:N0}/{enumerated:N0}");
        }

        lock (gate) fullSongsHashIndexBuilt = true;
        progress?.Report($"Songs hash index complete · no local .osu matched replay hash {ShortHash(md5)}.");
        return null;
    }

    private void BuildFullSongsHashIndex(IProgress<string>? progress, CancellationToken token)
    {
        if (!Directory.Exists(settings.SongsDirectory)) return;
        int n = 0;
        foreach (var file in Directory.EnumerateFiles(settings.SongsDirectory, "*.osu", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            HashAndCache(file, wantedHash: null, out _);
            n++;
            if (n % 1000 == 0) progress?.Report($"Fallback index: {n:N0} maps hashed…");
        }
        lock (gate) fullSongsHashIndexBuilt = true;
        progress?.Report($"Fallback index complete: {fallbackHashPaths.Count:N0} maps.");
    }

    private BeatmapData? ResolveCached(string md5)
    {
        OsuDbBeatmap? c = null;
        string? fallback = null;
        lock (gate)
        {
            if (parsedBeatmaps.TryGetValue(md5, out var parsed)) return parsed;
            cache.TryGetValue(md5, out c);
            fallbackHashPaths.TryGetValue(md5, out fallback);
        }

        if (c != null)
        {
            string path = Path.Combine(settings.SongsDirectory, c.FolderName, c.OsuFileName);
            if (File.Exists(path))
            {
                try
                {
                    var parsed = BeatmapParser.Parse(path, c);
                    lock (gate) parsedBeatmaps[md5] = parsed;
                    return parsed;
                }
                catch { }
            }
        }

        if (!string.IsNullOrWhiteSpace(fallback) && File.Exists(fallback))
        {
            try
            {
                // Generated maps can be replaced in-place, so validate fallback paths once.
                string actual = ComputeMd5(fallback);
                if (actual.Equals(md5, StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = BeatmapParser.Parse(fallback);
                    parsed.Hash = md5;
                    lock (gate) parsedBeatmaps[md5] = parsed;
                    return parsed;
                }

                lock (gate) { fallbackHashPaths.Remove(md5); parsedBeatmaps.Remove(md5); }
            }
            catch { }
        }

        return null;
    }

    private bool RefreshDb()
    {
        string db = Path.Combine(settings.OsuDirectory, "osu!.db");
        if (!File.Exists(db)) return false;
        try
        {
            var loaded = OsuDbReader.Read(db);
            lock (gate) cache = loaded;
            return true;
        }
        catch { return false; }
    }

    private bool HashAndCache(string file, string? wantedHash, out BeatmapData? found)
    {
        found = null;
        try
        {
            string hash = ComputeMd5(file);
            lock (gate) fallbackHashPaths[hash] = file;
            if (wantedHash == null) return true;
            if (hash.Equals(wantedHash, StringComparison.OrdinalIgnoreCase))
            {
                found = BeatmapParser.Parse(file);
                found.Hash = hash;
                lock (gate) parsedBeatmaps[hash] = found;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string ComputeMd5(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }

    private static string NormalizeHash(string? hash) => (hash ?? "").Trim().ToLowerInvariant();
    private static string ShortHash(string hash) => hash[..Math.Min(8, hash.Length)] + "…";
}
