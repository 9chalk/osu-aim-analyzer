using System.Security.Cryptography;

namespace OsuAimAnalyzer;

public sealed class ReplayProcessor : IDisposable
{
    private readonly AppSettings settings;
    private readonly BeatmapResolver resolver;
    private readonly AnalyzerDatabase database;
    private readonly AimAnalyzer analyzer;
    private readonly SemaphoreSlim processingGate = new(1, 1);
    private FileSystemWatcher? watcher;
    private readonly HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object pendingGate = new();

    public event Action<string>? StatusChanged;
    public event Action<long>? PlayAnalyzed;

    public ReplayProcessor(AppSettings settings, BeatmapResolver resolver, AnalyzerDatabase database)
    {
        this.settings = settings;
        this.resolver = resolver;
        this.database = database;
        analyzer = new AimAnalyzer(settings);
    }

    public void StartWatcher()
    {
        StopWatcher();
        if (!settings.WatchReplaysAutomatically || !Directory.Exists(settings.ReplayDirectory)) return;
        watcher = new FileSystemWatcher(settings.ReplayDirectory, "*.osr")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        watcher.Created += OnReplayChanged;
        watcher.Changed += OnReplayChanged;
        watcher.Renamed += (_, e) => Queue(e.FullPath);
        StatusChanged?.Invoke("Replay watcher active.");
    }

    public void StopWatcher()
    {
        if (watcher == null) return;
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        watcher = null;
    }

    private void OnReplayChanged(object sender, FileSystemEventArgs e) => Queue(e.FullPath);

    private void Queue(string path)
    {
        if (!path.EndsWith(".osr", StringComparison.OrdinalIgnoreCase)) return;
        lock (pendingGate)
        {
            if (!pending.Add(path)) return;
        }
        StatusChanged?.Invoke($"Replay detected · {Path.GetFileName(path)}");
        _ = Task.Run(async () =>
        {
            try
            {
                await WaitUntilStable(path).ConfigureAwait(false);
                await ProcessFileAsync(path).ConfigureAwait(false);
            }
            finally
            {
                lock (pendingGate) pending.Remove(path);
            }
        });
    }

    private static async Task WaitUntilStable(string path)
    {
        // Replays are tiny. Two equal size observations ~35 ms apart are enough to start
        // parsing; ProcessFileAsync also retries header reads if osu! is still finishing the write.
        long last = -1;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(35).ConfigureAwait(false);
            try
            {
                long size = new FileInfo(path).Length;
                if (size > 0 && size == last)
                {
                    using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return;
                }
                last = size;
            }
            catch { }
        }
    }

    public Task<int> ScanRecentAsync(TimeSpan age, IProgress<string>? progress = null, CancellationToken token = default)
        => ScanHistoryAsync(age, progress, token);

    public async Task<int> ScanHistoryAsync(TimeSpan? age, IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (!Directory.Exists(settings.ReplayDirectory)) return 0;

        DateTime? cutoff = age.HasValue ? DateTime.UtcNow - age.Value : null;
        var files = Directory.EnumerateFiles(settings.ReplayDirectory, "*.osr", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .Where(f => !cutoff.HasValue || f.LastWriteTimeUtc >= cutoff.Value)
            .OrderBy(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .ToArray();

        int imported = 0;
        for (int i = 0; i < files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Checking replay {i + 1:N0}/{files.Length:N0} · {imported:N0} new…");
            try { if (await ProcessFileAsync(files[i], token).ConfigureAwait(false)) imported++; }
            catch (Exception ex) { progress?.Report($"Skipped {Path.GetFileName(files[i])}: {ex.Message}"); }
        }
        return imported;
    }

    public async Task<int> ImportFilesAsync(IEnumerable<string> paths, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var files = paths.ToArray();
        int done = 0;
        for (int i = 0; i < files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Importing {i + 1:N0}/{files.Length:N0}: {Path.GetFileName(files[i])}");
            try { if (await ProcessFileAsync(files[i], token).ConfigureAwait(false)) done++; }
            catch (Exception ex) { progress?.Report($"Skipped {Path.GetFileName(files[i])}: {ex.Message}"); }
        }
        return done;
    }

    public async Task<bool> ProcessFileAsync(string path, CancellationToken token = default)
    {
        await processingGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return false;

            // Read only the small replay header first. Large history rescans can then skip
            // already-imported replays without decompressing their cursor-frame streams.
            ReplayData? header = null;
            Exception? headerError = null;
            for (int attempt = 0; attempt < 8 && header == null; attempt++)
            {
                try { header = ReplayReader.ReadHeader(path); }
                catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
                {
                    headerError = ex;
                    if (attempt < 7) await Task.Delay(40, token).ConfigureAwait(false);
                }
            }
            if (header == null) throw new InvalidDataException("Replay file never became readable.", headerError);
            string replayHash = header.ReplayHash;
            if (string.IsNullOrWhiteSpace(replayHash))
            {
                using var fs = File.OpenRead(path);
                replayHash = Convert.ToHexString(MD5.HashData(fs)).ToLowerInvariant();
            }
            if (database.ContainsReplay(replayHash))
            {
                database.UpdateReplayPath(replayHash, path);
                return false;
            }
            if (header.Mode != 0) return false;

            ReplayData replay = ReplayReader.Read(path);
            if (string.IsNullOrWhiteSpace(replay.ReplayHash)) replay.ReplayHash = replayHash;

            // Hot path: cache-only resolution. The resolver watches Songs for new .osu files,
            // so freshly generated trainer/merger maps are usually already available here.
            BeatmapData? map = resolver.Resolve(replay.BeatmapHash, refreshDbOnMiss: false);
            if (map == null)
            {
                // Give the Songs watcher a very short chance to finish hashing a just-imported map.
                for (int retry = 0; retry < 3 && map == null; retry++)
                {
                    await Task.Delay(55, token).ConfigureAwait(false);
                    map = resolver.Resolve(replay.BeatmapHash, refreshDbOnMiss: false);
                }
            }

            if (map == null)
            {
                // Fresh merger/trainer maps can exist in Songs before stable has committed them to osu!.db.
                // Resolve the replay hash against the real .osu files instead of silently dropping the play.
                var songsProgress = new Progress<string>(s => StatusChanged?.Invoke(s));
                map = await resolver.ResolveFromSongsAsync(replay.BeatmapHash, songsProgress, token).ConfigureAwait(false);
            }

            if (map == null)
            {
                string shortHash = replay.BeatmapHash[..Math.Min(8, replay.BeatmapHash.Length)];
                StatusChanged?.Invoke($"Replay skipped: no local .osu file matches replay map hash {shortHash}…. If the map was regenerated after the replay, import the exact version that was played.");
                return false;
            }

            StatusChanged?.Invoke($"Analyzing {map.DisplayName}…");
            PlayAnalysis result = await Task.Run(() => analyzer.Analyze(replay, map), token).ConfigureAwait(false);
            ProductionScoring.Apply(result);
            result.ReplayPath = path;
            long playId = database.Save(result);
            StatusChanged?.Invoke($"{map.DisplayName}: {result.Proficiency:0} proficiency · {result.RawAimRating:0} aim performance · {result.TrainingZone}");
            PlayAnalyzed?.Invoke(playId);
            return true;
        }
        finally
        {
            processingGate.Release();
        }
    }

    public void Dispose()
    {
        StopWatcher();
        processingGate.Dispose();
    }
}
