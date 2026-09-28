using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public sealed class PracticeAudioTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aim-audio-test-" + Guid.NewGuid());
    public PracticeAudioTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData(PracticePitchPolicy.PreservePitch, 440)]
    [InlineData(PracticePitchPolicy.ChangeWithRate, 396)]
    public async Task Render_ExplicitPitchPolicyControlsFrequencyAndDuration(PracticePitchPolicy pitch, double expectedFrequency)
    {
        string source = Path.Combine(root, "tone.wav"), output = Path.Combine(root, "rendered.wav");
        WriteWave(source, 8, t => .5 * Math.Sin(2 * Math.PI * 440 * t));
        byte[] original = File.ReadAllBytes(source);
        var result = await new PracticeAudioRenderer().RenderAsync(source, output, .9, pitch, default);
        Assert.InRange(result.DurationSeconds, 8 / .9 - .08, 8 / .9 + .08);
        var samples = ReadWave(output);
        int crossings = 0;
        for (int i = 44100; i < 44100 * 5; i++) if (samples[i - 1] <= 0 && samples[i] > 0) crossings++;
        Assert.InRange(crossings / 4.0, expectedFrequency - 2, expectedFrequency + 2);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(PracticePitchPolicy.PreservePitch)]
    [InlineData(PracticePitchPolicy.ChangeWithRate)]
    public async Task Render_ToneBurstsFollowScaledBeatmapTimestamps(PracticePitchPolicy pitch)
    {
        string source = Path.Combine(root, "markers.wav"), output = Path.Combine(root, "scaled.wav");
        WriteWave(source, 8, t => Enumerable.Range(1, 3).Any(i => t >= i * 2 && t < i * 2 + .12) ? .7 * Math.Sin(2 * Math.PI * 880 * t) : 0);
        await new PracticeAudioRenderer().RenderAsync(source, output, .9, pitch, default);
        var samples = ReadWave(output);
        foreach (double time in new[] { 2.0, 4, 6 })
        {
            int start = (int)((time / .9 - .15) * 44100), end = (int)((time / .9 + .2) * 44100);
            int onset = Enumerable.Range(start, end - start).First(i => Math.Abs(samples[i]) > .15);
            Assert.InRange(onset / 44100.0, time / .9 - .055, time / .9 + .055);
        }
    }

    [Fact]
    public async Task Render_InvalidInputAndPreCanceledWorkLeaveNoOutput()
    {
        string source = Path.Combine(root, "invalid.mp3"), output = Path.Combine(root, "bad.wav");
        File.WriteAllText(source, "not audio");
        await Assert.ThrowsAsync<IOException>(() => new PracticeAudioRenderer().RenderAsync(source, output, .9, PracticePitchPolicy.PreservePitch, default));
        Assert.False(File.Exists(output));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PracticeAudioRenderer().RenderAsync(source, output, .9, PracticePitchPolicy.PreservePitch, cancel.Token));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Render_CancelRunningProcessRemovesOutputAndReleasesFiles()
    {
        string source = Path.Combine(root, "long.wav"), output = Path.Combine(root, "canceled.wav");
        WriteWave(source, 600, t => .4 * Math.Sin(2 * Math.PI * 440 * t));
        using var cancellation = new CancellationTokenSource();
        Task work = new PracticeAudioRenderer().RenderAsync(source, output, .9, PracticePitchPolicy.PreservePitch, cancellation.Token);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(output) && !work.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(1);
        Assert.True(File.Exists(output), "Renderer did not begin writing before cancellation.");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.False(File.Exists(output));
        using var exclusive = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")]
    [InlineData("ogg", "libvorbis")]
    public async Task Render_CompressedSourcesHaveValidatedDuration(string extension, string codec)
    {
        string wave = Path.Combine(root, "tone.wav"), compressed = Path.Combine(root, "tone." + extension), output = Path.Combine(root, "decoded.wav");
        WriteWave(wave, 8, t => .4 * Math.Sin(2 * Math.PI * 440 * t));
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", wave, "-c:a", codec, compressed }) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        string error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        var rendered = await new PracticeAudioRenderer().RenderAsync(compressed, output, .9, PracticePitchPolicy.PreservePitch, default);
        Assert.InRange(rendered.DurationSeconds, 8 / .9 - .08, 8 / .9 + .08);
    }

    [Fact]
    public async Task Export_FullSeriesDeduplicatesAudioAndRetainsOriginalResources()
    {
        var (source, preview) = CreateMap();
        var originals = Directory.EnumerateFiles(Path.GetDirectoryName(source.BeatmapPath)!, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        string package = Path.Combine(root, "series.osz");
        var result = await PracticePackageExporter.ExportAsync(source, preview.Variants, package);
        Assert.Equal(4, result.DifficultyEntries.Count);
        using var zip = ZipFile.OpenRead(package);
        using var manifest = JsonDocument.Parse(zip.GetEntry("aim-analyzer-provenance.json")!.Open());
        Assert.Equal(2, manifest.RootElement.GetProperty("RenderedAudio").GetArrayLength());
        var audioNames = new List<string>();
        foreach (string entry in result.DifficultyEntries)
        {
            using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
            string text = await reader.ReadToEndAsync();
            string audio = text.Split('\n').Single(l => l.StartsWith("AudioFilename:")).Split(':', 2)[1].Trim();
            Assert.NotNull(zip.GetEntry(audio));
            audioNames.Add(audio);
            Assert.Equal("Original title", BeatmapParser.ParseDocument(BeatmapDocument.Parse(text)).Title);
        }
        Assert.Equal(3, audioNames.Distinct().Count()); // original + two unique rate jobs
        foreach (var original in originals) Assert.Equal(original.Value, File.ReadAllBytes(original.Key));
        Assert.Empty(Directory.EnumerateDirectories(root, ".aim-audio-*"));
    }

    [Fact]
    public async Task Export_DifferentPitchOptionsNeverShareRenderedAudio()
    {
        var (source, preview) = CreateMap();
        var preserve = preview.Variants.First(v => v.Options.SourceClockRate != 1);
        var o = preserve.Options;
        var changedOptions = new PracticeTransformOptions(o.SourceClockRate, o.SpacingMultiplier, o.SourceDifficulty, o.GeneratedDifficulty, PracticePitchPolicy.ChangeWithRate);
        var change = preserve with { Name = "Rate-linked pitch", Options = changedOptions };
        string package = Path.Combine(root, "pitch.osz");
        await PracticePackageExporter.ExportAsync(source, new[] { preserve, change }, package);
        using var zip = ZipFile.OpenRead(package);
        using var manifest = JsonDocument.Parse(zip.GetEntry("aim-analyzer-provenance.json")!.Open());
        var audio = manifest.RootElement.GetProperty("RenderedAudio");
        Assert.Equal(2, audio.GetArrayLength());
        Assert.NotEqual(audio[0].GetProperty("Entry").GetString(), audio[1].GetProperty("Entry").GetString());
    }

    [Fact]
    public async Task Export_SourceAudioChangeAfterSnapshotPublishesNothing()
    {
        var (source, preview) = CreateMap();
        string originalAudio = Path.Combine(Path.GetDirectoryName(source.BeatmapPath)!, "song.wav");
        await Assert.ThrowsAsync<IOException>(() => PracticePackageExporter.ExportAsync(source, preview.Variants,
            Path.Combine(root, "bad.osz"), audioRenderer: new MutatingRenderer(originalAudio)));
        AssertNoStaging();
    }

    private sealed class MutatingRenderer(string originalAudio) : IPracticeAudioRenderer
    {
        public async Task<RenderedPracticeAudio> RenderAsync(string source, string output, double rate, PracticePitchPolicy pitch, CancellationToken token)
        {
            var result = await new PracticeAudioRenderer().RenderAsync(source, output, rate, pitch, token);
            File.AppendAllText(originalAudio, "changed after snapshot");
            return result;
        }
    }

    [Fact]
    public async Task Export_OmitsAllOptionalMediaAndRetainsGameplayForFullSeries()
    {
        var (source, preview) = CreateMap(optionalMedia: true);
        string mapset = Path.GetDirectoryName(source.BeatmapPath)!;
        string storyboard = "[Variables]\n$asset=missing.png\n[Events]\nSprite,Background,Centre,\"$asset\",0,0";
        File.WriteAllText(Path.Combine(mapset, "story.osb"), storyboard);
        var originals = Directory.EnumerateFiles(mapset, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        string package = Path.Combine(root, "clean.osz");
        var result = await PracticePackageExporter.ExportAsync(source, preview.Variants, package);
        Assert.Equal(4, result.DifficultyEntries.Count);
        using var zip = ZipFile.OpenRead(package);
        Assert.Null(zip.GetEntry("story.osb")); Assert.Null(zip.GetEntry("sounds/click.wav"));
        Assert.NotNull(zip.GetEntry("images/bg,wide.jpg")); Assert.NotNull(zip.GetEntry("sounds/hit.wav"));
        foreach (string entry in result.DifficultyEntries)
        {
            using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
            string generated = await reader.ReadToEndAsync();
            foreach (string omitted in new[] { "movie.mp4", "missing.png", "Sprite,", "Animation,", "_F,", "[Variables]", "Sample," }) Assert.DoesNotContain(omitted, generated);
            Assert.Contains("images/bg,wide.jpg", generated);
            Assert.Contains("sounds/hit.wav", generated);
        }
        foreach (var original in originals) Assert.Equal(original.Value, File.ReadAllBytes(original.Key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_RenderFailureOrCancellationRemovesAllOwnedFiles(bool cancel)
    {
        var (source, preview) = CreateMap();
        var error = await Record.ExceptionAsync(() => PracticePackageExporter.ExportAsync(source, preview.Variants, Path.Combine(root, "bad.osz"), audioRenderer: new FailingRenderer(cancel)));
        Assert.True(cancel ? error is OperationCanceledException : error is IOException, error?.ToString());
        AssertNoStaging();
    }

    private void AssertNoStaging()
    {
        Assert.Empty(Directory.EnumerateFiles(root, "*.osz"));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        Assert.Empty(Directory.EnumerateDirectories(root, ".aim-audio-*"));
    }

    internal (PracticeSourceIdentity, PracticeSeriesPreview) CreateMap(bool optionalMedia = false)
    {
        string mapset = Path.Combine(root, "source"); Directory.CreateDirectory(mapset);
        string text = BeatmapDocumentTests.Map.Replace("audio/song.mp3", "song.wav");
        if (optionalMedia) text = text.Replace("[Events]", "[Events]\nVideo,0,\"movie.mp4\"\nSprite,Background,Centre,\"missing.png\",0,0\n_F,0,0,100,0,1\nAnimation,Foreground,Centre,\"frames.png\",0,0,2,100,LoopForever") + "\n[Variables]\n$asset=missing.png\n";
        var document = BeatmapDocument.Parse(text);
        var source = PracticePlannerTests.Identity(document, path: Path.Combine(mapset, "source.osu"));
        File.WriteAllBytes(source.BeatmapPath, document.ToBytes());
        WriteWave(Path.Combine(mapset, "song.wav"), 8, t => .3 * Math.Sin(2 * Math.PI * 440 * t));
        foreach (string resource in new[] { "images/bg,wide.jpg", "sounds/click.wav", "sounds/hit.wav" })
        {
            string path = Path.Combine(mapset, resource); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        }
        return (source, PracticeSeriesPlanner.Plan(source, document, 1, Array.Empty<RecommendationEvidence>(), PracticePitchPolicy.PreservePitch));
    }

    private sealed class FailingRenderer(bool cancel) : IPracticeAudioRenderer
    {
        public Task<RenderedPracticeAudio> RenderAsync(string source, string output, double rate, PracticePitchPolicy pitch, CancellationToken token)
        {
            File.WriteAllText(output, "partial");
            return Task.FromException<RenderedPracticeAudio>(cancel ? new OperationCanceledException() : new IOException("Synthetic renderer failure"));
        }
    }

    internal static void WriteWave(string path, int seconds, Func<double, double> sample)
    {
        using var writer = new BinaryWriter(File.Create(path));
        int count = 44100 * seconds;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
        for (int i = 0; i < count; i++) writer.Write((short)(sample(i / 44100.0) * short.MaxValue));
    }

    private static double[] ReadWave(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4)); int size = reader.ReadInt32();
            if (id != "data") { reader.BaseStream.Seek(size + size % 2, SeekOrigin.Current); continue; }
            var samples = new double[size / 4];
            for (int i = 0; i < samples.Length; i++) { samples[i] = reader.ReadInt16() / 32768.0; reader.ReadInt16(); }
            return samples;
        }
        throw new IOException("WAV has no data chunk.");
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(root);
        if (Path.GetDirectoryName(full) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("aim-audio-test-")) throw new InvalidOperationException("Unexpected cleanup path.");
        Directory.Delete(full, recursive: true);
    }
}
