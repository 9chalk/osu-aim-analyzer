using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OsuAimAnalyzer;

/// <summary>One staged package for an explicitly selected set. No source writes or automatic import.</summary>
public static class PracticePackageExporter
{
    private const long MaximumAssetBytes = 256L * 1024 * 1024;
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private static readonly Regex SampleBank = new(@"^(normal|soft|drum)-(hitnormal|hitwhistle|hitfinish|hitclap|slidertick|sliderslide|sliderwhistle)\d*\.(wav|ogg|mp3)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task<PublishedPracticePackage> ExportAsync(PracticeSourceIdentity source,
        IReadOnlyList<PracticeVariant> selectedVariants, string destination, IEnumerable<string>? forbiddenRoots = null,
        IProgress<string>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var selected = selectedVariants.ToArray();
        if (selected.Length is < 1 or > 5) throw new ArgumentException("Select between one and five variants.");
        if (selected.Any(v => v.Options.SourceClockRate != 1 || v.Preview.RequiresAudioRendering))
            throw new NotSupportedException("Rate-changing variants need TG5 audio rendering. Export only the explicitly selected spacing/stat variants.");
        string root = Path.GetDirectoryName(source.BeatmapPath)!;
        string[] forbidden = (forbiddenRoots ?? Array.Empty<string>()).ToArray();
        destination = PracticeExportPaths.Destination(destination, root, forbidden);
        PracticeExportPaths.CheckNoLinks(source.BeatmapPath);
        var original = await PracticePreviewSource.ReadVerifiedAsync(source, token);
        var resources = BeatmapResources.Inspect(original, includeStoryboards: true);
        Validate(resources);
        if (resources.Files.Count(r => r.Kind == "audio") != 1) throw new NotSupportedException("Export requires one explicit AudioFilename.");
        var assets = resources.Files.Select(r => r.RelativePath).ToList();
        var inspectedStoryboards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Preserve local sample-bank fallback without guessing slider edge/tick sample selection.
        // Only named banks are included, never unrelated difficulties or arbitrary mapset files.
        foreach (string file in Directory.EnumerateFiles(root))
        {
            token.ThrowIfCancellationRequested();
            if (SampleBank.IsMatch(Path.GetFileName(file))) { assets.Add(Path.GetFileName(file)); discoveredFiles.Add(Path.GetFileName(file)); }
            if (!Path.GetExtension(file).Equals(".osb", StringComparison.OrdinalIgnoreCase)) continue;
            PracticeExportPaths.CheckNoLinks(file);
            byte[] storyboardBytes = await PracticePreviewSource.ReadBytesAsync(file, token);
            var storyboard = BeatmapDocument.FromBytes(storyboardBytes);
            inspectedStoryboards.Add(Path.GetFileName(file), Hash(storyboardBytes));
            discoveredFiles.Add(Path.GetFileName(file));
            var storyboardResources = BeatmapResources.Inspect(storyboard, includeStoryboards: true);
            Validate(storyboardResources);
            assets.Add(Path.GetFileName(file));
            assets.AddRange(storyboardResources.Files.Select(r => r.RelativePath));
        }
        var assetPaths = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string asset in assets)
        {
            string entry = asset.Replace('\\', '/');
            string extension = Path.GetExtension(entry).ToLowerInvariant();
            if (extension is not (".mp3" or ".ogg" or ".wav" or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".mp4" or ".avi" or ".flv" or ".wmv" or ".m4v" or ".osb"))
                throw new NotSupportedException("Unsupported asset type: " + entry);
            assetPaths[entry] = PracticeExportPaths.Resource(root, entry);
        }
        if (assetPaths.Count > 20000) throw new NotSupportedException("Too many resources in this package.");
        long total = 0;
        foreach (string path in assetPaths.Values)
        {
            long length = new FileInfo(path).Length;
            total += length;
            if (length > MaximumAssetBytes || total > MaximumPackageBytes) throw new NotSupportedException("Export exceeds the 256 MiB asset or 1 GiB total resource limit.");
        }
        string id = Guid.NewGuid().ToString("N");
        var maps = new Dictionary<string, byte[]>();
        var recipes = new List<object>();
        var distinct = new HashSet<string>();
        var sourceMap = BeatmapParser.ParseDocument(original);
        for (int i = 0; i < selected.Length; i++)
        {
            var variant = selected[i];
            if (variant.Name.Length > 100 || variant.Name.Any(char.IsControl)) throw new ArgumentException("Invalid variant name.");
            var fresh = BeatmapTransforms.Apply(original, variant.Options, token);
            if (!fresh.Document.ToBytes().SequenceEqual(variant.Preview.Document.ToBytes()))
                throw new InvalidOperationException("Preview content does not match the source/options. Rebuild the preview.");
            string transformedHash = Hash(fresh.Document.ToBytes());
            if (transformedHash == Hash(original.ToBytes()) || !distinct.Add(transformedHash)) throw new ArgumentException("Unchanged or duplicate variants cannot be exported.");
            string entry = $"practice-{id[..8]}-{i + 1:00}.osu";
            var generated = fresh.Document.WithMetadata(new Dictionary<string, string>
            {
                ["BeatmapID"] = "0", ["BeatmapSetID"] = "-1",
                ["Version"] = $"{sourceMap.Version} · Practice {i + 1} {variant.Name} {id[..8]}"
            });
            byte[] bytes = generated.ToBytes();
            maps.Add(entry, bytes);
            recipes.Add(new { Entry = entry, GeneratedMd5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
                Sha256 = Hash(bytes), variant.Name, variant.Options, variant.Reason, fresh.AchievedHeadSpacingRatio,
                fresh.RepositionedGroups, fresh.RelaxedGroups, fresh.PreservedOutsideAnchors });
        }
        string staging = Path.Combine(Path.GetDirectoryName(destination)!, ".aim-practice-" + id + ".tmp");
        var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool ownsStaging = false;
        try
        {
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true))
            {
                ownsStaging = true;
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    long copiedResourceBytes = 0;
                    foreach (var asset in assetPaths)
                    {
                        token.ThrowIfCancellationRequested();
                        progress?.Report("Copying " + asset.Key);
                        PracticeExportPaths.Resource(root, asset.Key);
                        await using var input = new FileStream(asset.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                        copiedResourceBytes += input.Length;
                        if (copiedResourceBytes > MaximumPackageBytes) throw new IOException("Resources grew beyond the 1 GiB package limit.");
                        await using var target = archive.CreateEntry(asset.Key, CompressionLevel.Optimal).Open();
                        checksums.Add(asset.Key, await CopyHashAsync(input, target, MaximumAssetBytes, token));
                        if (inspectedStoryboards.TryGetValue(asset.Key, out string? expected) && checksums[asset.Key] != expected)
                            throw new IOException("Storyboard changed after resource discovery: " + asset.Key);
                    }
                    foreach (var map in maps) await WriteEntryAsync(archive, map.Key, map.Value, checksums, token);
                    byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        SchemaVersion = 1, PackageId = id, CreatedUtc = DateTime.UtcNow,
                        Source = new { source.BeatmapHash, source.SelectedPlayId, source.PlayedMods, File = Path.GetFileName(source.BeatmapPath) },
                        Title = sourceMap.Title, Variants = recipes,
                        Resources = assetPaths.Keys.Select(e => new { Entry = e, Sha256 = checksums[e] }).ToArray(),
                        Policy = "TG4 spacing/stat only; local sample banks retained; no rate audio rendering."
                    }, new JsonSerializerOptions { WriteIndented = true });
                    await WriteEntryAsync(archive, "aim-analyzer-provenance.json", manifest, checksums, token);
                }
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
            }
            progress?.Report("Validating package and source files…");
            using (var archive = ZipFile.OpenRead(staging))
            {
                if (archive.Entries.Count != checksums.Count) throw new IOException("Package entry count mismatch.");
                foreach (var entry in archive.Entries)
                {
                    await using var input = entry.Open();
                    if (await CopyHashAsync(input, Stream.Null, MaximumPackageBytes, token) != checksums[entry.FullName])
                        throw new IOException("Package validation failed: " + entry.FullName);
                }
            }
            await PracticePreviewSource.ReadVerifiedAsync(source, token);
            var currentDiscovered = Directory.EnumerateFiles(root).Select(Path.GetFileName)
                .Where(name => name is not null && (SampleBank.IsMatch(name) || Path.GetExtension(name).Equals(".osb", StringComparison.OrdinalIgnoreCase)))
                .Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!discoveredFiles.SetEquals(currentDiscovered)) throw new IOException("Mapset resources changed during export. Retry after editing has finished.");
            foreach (var asset in assetPaths)
            {
                PracticeExportPaths.Resource(root, asset.Key);
                await using var input = new FileStream(asset.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                if (await CopyHashAsync(input, Stream.Null, MaximumAssetBytes, token) != checksums[asset.Key])
                    throw new IOException("A resource changed during export: " + asset.Key);
            }
            token.ThrowIfCancellationRequested();
            PracticeExportPaths.Destination(destination, root, forbidden);
            // Commit point: same-directory, non-overwriting rename. Cancellation after this point cannot undo publication.
            File.Move(staging, destination, overwrite: false);
            return new(source, destination, maps.Keys);
        }
        finally
        {
            if (ownsStaging && File.Exists(staging)) File.Delete(staging);
        }
    }

    private static void Validate(BeatmapResources resources)
    {
        if (resources.Issues.Count > 0) throw new NotSupportedException(string.Join("\n", resources.Issues));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static async Task WriteEntryAsync(ZipArchive archive, string entry, byte[] bytes, Dictionary<string, string> checksums, CancellationToken token)
    {
        if (checksums.ContainsKey(entry)) throw new IOException("Archive entry collision: " + entry);
        await using var target = archive.CreateEntry(entry, CompressionLevel.Optimal).Open();
        await target.WriteAsync(bytes, token);
        checksums.Add(entry, Hash(bytes));
    }
    private static async Task<string> CopyHashAsync(Stream input, Stream output, long maximumBytes, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new IOException("Resource grew beyond the supported size.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
