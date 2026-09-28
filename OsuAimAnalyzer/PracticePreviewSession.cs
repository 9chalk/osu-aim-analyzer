using System.Security.Cryptography;

namespace OsuAimAnalyzer;

public sealed record PracticePreviewRequest(long Version, long PlayId, CancellationToken Token);

/// <summary>Owned by the UI thread. Late completion cannot replace a newer selection or retry.</summary>
public sealed class PracticePreviewSession : IDisposable
{
    private CancellationTokenSource? cancellation;
    private long version;
    public PracticePreviewRequest Begin(long playId)
    {
        Cancel();
        cancellation = new CancellationTokenSource();
        return new(version, playId, cancellation.Token);
    }
    public bool IsCurrent(PracticePreviewRequest request, long? selectedPlayId)
        => version == request.Version && request.PlayId == selectedPlayId && !request.Token.IsCancellationRequested;
    public void Cancel()
    {
        version++;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }
    public void Dispose() => Cancel();
}

public static class PracticePreviewSource
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static async Task<BeatmapDocument> ReadVerifiedAsync(PracticeSourceIdentity source, CancellationToken token = default)
    {
        byte[] bytes = await ReadBytesAsync(source.BeatmapPath, token).ConfigureAwait(false);
        if (!Convert.ToHexString(MD5.HashData(bytes)).Equals(source.BeatmapHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The .osu file has changed since this play was analyzed. Restore the matching version or analyze a replay for the current version.");
        return BeatmapDocument.FromBytes(bytes);
    }

    internal static async Task<byte[]> ReadBytesAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumBytes) throw new NotSupportedException("Beatmap and storyboard documents are limited to 16 MiB.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return bytes;
    }
}
