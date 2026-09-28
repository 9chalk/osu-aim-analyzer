using System.IO.Compression;
using System.Text.Json;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public sealed class PracticeExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aim-export-test-" + Guid.NewGuid());
    private string Mapset => Path.Combine(root, "source");
    private string Destination => Path.Combine(root, "practice.osz");
    private readonly BeatmapDocument document;
    private readonly PracticeSourceIdentity source;
    private readonly PracticeVariant spacing;
    internal PracticeSourceIdentity Source => source;
    internal string OutputPath => Destination;
    internal PracticeSeriesPreview Preview => PracticeSeriesPlanner.Plan(source, document, 1, Array.Empty<RecommendationEvidence>(), PracticePitchPolicy.PreservePitch);

    public PracticeExportTests()
    {
        Directory.CreateDirectory(Mapset);
        document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        source = PracticePlannerTests.Identity(document, path: Path.Combine(Mapset, "source.osu"));
        File.WriteAllBytes(source.BeatmapPath, document.ToBytes());
        WriteAsset("audio/song.mp3"); WriteAsset("images/bg,wide.jpg"); WriteAsset("sounds/click.wav"); WriteAsset("sounds/hit.wav");
        WriteAsset("normal-hitclap2.wav"); WriteAsset("drum-slidertick.wav");
        File.WriteAllText(Path.Combine(Mapset, "unrelated.osu"), "do not export");
        File.WriteAllText(Path.Combine(Mapset, "private.txt"), "do not export");
        spacing = PracticeSeriesPlanner.Plan(source, document, 1, Array.Empty<RecommendationEvidence>(), PracticePitchPolicy.PreservePitch).Variants.Single(v => v.Name == "Reduced spacing");
    }
    private void WriteAsset(string name)
    {
        string path = Path.Combine(Mapset, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 255 });
    }
    private Task<PublishedPracticePackage> Export(IProgress<string>? progress = null, CancellationToken token = default)
        => PracticePackageExporter.ExportAsync(source, new[] { spacing }, Destination, progress: progress, token: token);
    private void AssertNoPackageOrStaging()
    {
        Assert.False(File.Exists(Destination));
        Assert.Empty(Directory.EnumerateFiles(root, ".aim-practice-*.tmp"));
        Assert.Empty(Directory.EnumerateDirectories(root, ".aim-audio-*"));
    }

    [Fact]
    public async Task Export_MultiDifficultyRetainsAssetsTitleAndOriginalsWithFreshIdentities()
    {
        var originalBytes = Directory.EnumerateFiles(Mapset, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var options = BeatmapDocumentTests.Options(difficulty: new(4, 4, 7, 6));
        var stats = new PracticeVariant("Lower HP", options, BeatmapTransforms.Apply(document, options), "Synthetic stat-only recipe");
        var result = await PracticePackageExporter.ExportAsync(source, new[] { spacing, stats }, Destination);
        Assert.Equal(Destination, result.PackagePath);
        Assert.Equal(2, result.DifficultyEntries.Count);
        using var zip = ZipFile.OpenRead(Destination);
        Assert.Equal(2, zip.Entries.Count(e => e.Name.EndsWith(".osu")));
        Assert.Null(zip.GetEntry("unrelated.osu")); Assert.Null(zip.GetEntry("private.txt"));
        Assert.NotNull(zip.GetEntry("normal-hitclap2.wav")); Assert.NotNull(zip.GetEntry("drum-slidertick.wav"));
        foreach (string name in result.DifficultyEntries)
        {
            using var input = zip.GetEntry(name)!.Open();
            using var reader = new StreamReader(input);
            var generated = BeatmapDocument.Parse(await reader.ReadToEndAsync());
            var map = BeatmapParser.ParseDocument(generated);
            Assert.Equal("Original title", map.Title);
            Assert.Equal(0, map.BeatmapId); Assert.Equal(-1, map.BeatmapSetId);
            Assert.Contains("Practice", map.Version);
            Assert.Contains("images/bg,wide.jpg", generated.ToString());
        }
        using (var input = zip.GetEntry("images/bg,wide.jpg")!.Open())
        {
            using var memory = new MemoryStream(); await input.CopyToAsync(memory);
            Assert.Equal(File.ReadAllBytes(Path.Combine(Mapset, "images/bg,wide.jpg")), memory.ToArray());
        }
        using (var input = zip.GetEntry("aim-analyzer-provenance.json")!.Open())
        {
            using var manifest = await JsonDocument.ParseAsync(input);
            Assert.Equal(2, manifest.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(2, manifest.RootElement.GetProperty("Variants").GetArrayLength());
            Assert.Equal(source.BeatmapHash, manifest.RootElement.GetProperty("Source").GetProperty("BeatmapHash").GetString());
        }
        foreach (var item in originalBytes) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task Export_MissingAudioToolsFailsWholeSelectedSet()
    {
        var options = BeatmapDocumentTests.Options(.9);
        var slowdown = new PracticeVariant("Slowdown", options, BeatmapTransforms.Apply(document, options), "test");
        await Assert.ThrowsAsync<FileNotFoundException>(() => PracticePackageExporter.ExportAsync(source, new[] { spacing, slowdown }, Destination, audioRenderer: new PracticeAudioRenderer(Path.Combine(root, "missing-tools"))));
        AssertNoPackageOrStaging();
    }

    [Fact]
    public async Task Export_PreservesStoryboardAssetsAndAnimationFrames()
    {
        WriteAsset("story/sprite.png"); WriteAsset("story/frame0.png"); WriteAsset("story/frame1.png");
        string storyboard = "[Events]\nSprite,Background,Centre,\"story/sprite.png\",0,0\n F,0,0,100,0,1\nAnimation,Foreground,Centre,\"story/frame.png\",0,0,2,100,LoopForever\n";
        File.WriteAllText(Path.Combine(Mapset, "original.osb"), storyboard);
        await Export();
        using var zip = ZipFile.OpenRead(Destination);
        Assert.NotNull(zip.GetEntry("story/frame0.png")); Assert.NotNull(zip.GetEntry("story/frame1.png"));
        using var reader = new StreamReader(zip.GetEntry("original.osb")!.Open());
        Assert.Equal(storyboard, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Export_UnsupportedStoryboardOrMissingAssetPublishesNothing()
    {
        File.WriteAllText(Path.Combine(Mapset, "original.osb"), "[Variables]\n$asset=story/sprite.png\n[Events]\nSprite,Background,Centre,\"$asset\",0,0");
        await Assert.ThrowsAsync<NotSupportedException>(() => Export());
        AssertNoPackageOrStaging();
        File.Delete(Path.Combine(Mapset, "original.osb"));
        File.Delete(Path.Combine(Mapset, "audio/song.mp3"));
        await Assert.ThrowsAnyAsync<IOException>(() => Export());
        AssertNoPackageOrStaging();
    }

    [Fact]
    public async Task Export_NeverOverwritesAndRepeatExportHasDistinctIdentity()
    {
        await Export();
        byte[] original = File.ReadAllBytes(Destination);
        await Assert.ThrowsAsync<IOException>(() => Export());
        Assert.Equal(original, File.ReadAllBytes(Destination));
        string another = Path.Combine(root, "second.osz");
        await PracticePackageExporter.ExportAsync(source, new[] { spacing }, another);
        using var a = ZipFile.OpenRead(Destination); using var b = ZipFile.OpenRead(another);
        Assert.NotEqual(a.Entries.Single(e => e.Name.EndsWith(".osu")).Name, b.Entries.Single(e => e.Name.EndsWith(".osu")).Name);
    }

    [Fact]
    public async Task Export_RejectsOriginalFolderSongsAndReferenceDestinations()
    {
        await Assert.ThrowsAsync<IOException>(() => PracticePackageExporter.ExportAsync(source, new[] { spacing }, Path.Combine(Mapset, "bad.osz")));
        await Assert.ThrowsAsync<IOException>(() => PracticePackageExporter.ExportAsync(source, new[] { spacing }, Destination, new[] { root }));
        string reference = Path.Combine(root, "reference"); Directory.CreateDirectory(reference);
        await Assert.ThrowsAsync<IOException>(() => PracticePackageExporter.ExportAsync(source, new[] { spacing }, Path.Combine(reference, "bad.osz")));
        Assert.Empty(Directory.EnumerateFiles(reference));
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("C:/escape.png")]
    [InlineData("images/NUL.png")]
    [InlineData("images/bg.png:secret")]
    public async Task Export_RejectsUnsafeResources(string resource)
    {
        var changed = BeatmapDocument.Parse(BeatmapDocumentTests.Map.Replace("images/bg,wide.jpg", resource));
        var changedSource = PracticePlannerTests.Identity(changed, path: source.BeatmapPath);
        File.WriteAllBytes(source.BeatmapPath, changed.ToBytes());
        var options = BeatmapDocumentTests.Options(spacing: .9);
        var variant = new PracticeVariant("Spacing", options, BeatmapTransforms.Apply(changed, options), "test");
        var error = await Record.ExceptionAsync(() => PracticePackageExporter.ExportAsync(changedSource, new[] { variant }, Destination));
        Assert.True(error is IOException or NotSupportedException, error?.ToString());
        AssertNoPackageOrStaging();
    }

    [Fact]
    public async Task Export_CancelDuringCopyAndResourceMutationRemoveStaging()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Export(new ImmediateProgress(_ => cancellation.Cancel()), cancellation.Token));
        AssertNoPackageOrStaging();
        await Assert.ThrowsAsync<IOException>(() => Export(new ImmediateProgress(message =>
        {
            if (message.StartsWith("Validating")) File.AppendAllText(Path.Combine(Mapset, "audio/song.mp3"), "changed");
        })));
        AssertNoPackageOrStaging();
    }

    [Fact]
    public async Task Export_RejectsSourceChangeAndForgedPreview()
    {
        File.AppendAllText(source.BeatmapPath, "\n// changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Export());
        File.WriteAllBytes(source.BeatmapPath, document.ToBytes());
        var forged = spacing with { Preview = spacing.Preview with { Document = document } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => PracticePackageExporter.ExportAsync(source, new[] { forged }, Destination));
        await Assert.ThrowsAsync<ArgumentException>(() => PracticePackageExporter.ExportAsync(source, new[] { spacing, spacing }, Destination));
        AssertNoPackageOrStaging();
    }

    [Fact]
    public async Task Export_RejectsJunctionResources()
    {
        string outside = Path.Combine(root, "outside");
        string link = Path.Combine(Mapset, "linked");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "bg.jpg"), "outside asset");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{outside}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var changed = BeatmapDocument.Parse(BeatmapDocumentTests.Map.Replace("images/bg,wide.jpg", "linked/bg.jpg"));
            File.WriteAllBytes(source.BeatmapPath, changed.ToBytes());
            var changedSource = PracticePlannerTests.Identity(changed, path: source.BeatmapPath);
            var options = BeatmapDocumentTests.Options(spacing: .9);
            var variant = new PracticeVariant("Spacing", options, BeatmapTransforms.Apply(changed, options), "test");
            var error = await Assert.ThrowsAsync<IOException>(() => PracticePackageExporter.ExportAsync(changedSource, new[] { variant }, Destination));
            Assert.Contains("reparse-point", error.Message);
            AssertNoPackageOrStaging();
            Assert.Equal("outside asset", File.ReadAllText(Path.Combine(outside, "bg.jpg")));
        }
        finally { Directory.Delete(link); }
    }

    private sealed class ImmediateProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(root);
        if (Path.GetDirectoryName(full) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("aim-export-test-"))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        Directory.Delete(full, recursive: true);
    }
}
