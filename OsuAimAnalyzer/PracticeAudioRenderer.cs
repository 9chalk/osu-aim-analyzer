using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OsuAimAnalyzer;

public sealed record RenderedPracticeAudio(double SourceDurationSeconds, double DurationSeconds, int SampleRate, string Renderer);

public interface IPracticeAudioRenderer
{
    Task<RenderedPracticeAudio> RenderAsync(string source, string output, double rate, PracticePitchPolicy pitch, CancellationToken token);
}

/// <summary>Optional external process boundary. No shell, network protocols, automatic download or startup work.</summary>
public sealed class PracticeAudioRenderer : IPracticeAudioRenderer
{
    public const string Distribution = "FFmpeg 9.0.2 Gyan essentials; PCM16 stereo 44100 Hz; pipeline v1";
    private readonly string directory;
    public PracticeAudioRenderer(string? directory = null) => this.directory = directory ?? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
    public bool IsAvailable => File.Exists(Path.Combine(directory, "ffmpeg.exe")) && File.Exists(Path.Combine(directory, "ffprobe.exe"));

    public async Task<RenderedPracticeAudio> RenderAsync(string source, string output, double rate, PracticePitchPolicy pitch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(rate) || rate < .5 || rate > 2) throw new NotSupportedException("Audio rates must be between 0.5x and 2x.");
        if (!Enum.IsDefined(pitch)) throw new ArgumentOutOfRangeException(nameof(pitch));
        if (!IsAvailable) throw new FileNotFoundException("Audio tools are missing. Run setup_audio.ps1 beside the app, or use the complete published folder. Spacing-only export still works.");
        if (File.Exists(output)) throw new IOException("Audio output already exists.");
        string format = Path.GetExtension(source).ToLowerInvariant() switch { ".mp3" => "mp3", ".ogg" => "ogg", ".wav" => "wav", _ => throw new NotSupportedException("Rate audio currently supports MP3, Ogg and WAV sources.") };
        bool complete = false;
        try
        {
            double sourceDuration = await DurationAsync(source, format, token);
            if (sourceDuration / rate * 44100 * 4 + 4096 > 256L * 1024 * 1024)
                throw new NotSupportedException("Rendered audio would exceed the 256 MiB resource limit.");
            string speed = rate.ToString("G17", CultureInfo.InvariantCulture);
            string filter = pitch == PracticePitchPolicy.PreservePitch ? $"atempo={speed},asetpts=PTS-STARTPTS"
                : $"aresample=44100,asetrate=44100*{speed},aresample=44100,asetpts=PTS-STARTPTS";
            await RunAsync("ffmpeg.exe", new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe", "-f", format, "-i", source,
                "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "-1", "-af", filter, "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", "-f", "wav", output }, token);
            double duration = await DurationAsync(output, "wav", token);
            double expected = sourceDuration / rate;
            if (Math.Abs(duration - expected) > Math.Max(.08, expected * .001))
                throw new IOException($"Rendered audio duration mismatch: expected {expected:0.000}s, received {duration:0.000}s.");
            complete = true;
            return new(sourceDuration, duration, 44100, Distribution);
        }
        finally { if (!complete && File.Exists(output)) File.Delete(output); }
    }

    private async Task<double> DurationAsync(string path, string format, CancellationToken token)
    {
        string json = await RunAsync("ffprobe.exe", new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-f", format, "-select_streams", "a:0", "-show_entries", "format=duration:stream=sample_rate,channels", "-of", "json", path }, token);
        using var parsed = JsonDocument.Parse(json);
        if (!parsed.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() != 1 ||
            !double.TryParse(parsed.RootElement.GetProperty("format").GetProperty("duration").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double duration) ||
            !double.IsFinite(duration) || duration <= 0 || duration > 1800)
            throw new NotSupportedException("Audio must contain a readable stream with duration between zero and 30 minutes.");
        return duration;
    }

    private async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var info = new ProcessStartInfo(Path.Combine(directory, executable))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = ReadBoundedAsync(process.StandardOutput);
        var stderr = ReadBoundedAsync(process.StandardError);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("Audio processing timed out after ten minutes.");
        }
        string errors = await stderr;
        string output = await stdout;
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException($"{executable} failed: {errors}");
        return output;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
            if (text.Length < 32768) text.Append(buffer, 0, Math.Min(count, 32768 - text.Length));
        return text.ToString();
    }
}
