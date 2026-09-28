using System.Globalization;
using System.Text;
using SharpCompress.Compressors.LZMA;

namespace OsuAimAnalyzer;

public static class ReplayReader
{
    public static ReplayData ReadHeader(string path) => ReadCore(path, includeFrames: false);

    public static ReplayData Read(string path) => ReadCore(path, includeFrames: true);

    private static ReplayData ReadCore(string path, bool includeFrames)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        var r = new ReplayData
        {
            Mode = br.ReadByte(),
            Version = br.ReadInt32(),
            BeatmapHash = ReadOsuString(br),
            PlayerName = ReadOsuString(br),
            ReplayHash = ReadOsuString(br),
            Count300 = br.ReadUInt16(),
            Count100 = br.ReadUInt16(),
            Count50 = br.ReadUInt16(),
            CountGeki = br.ReadUInt16(),
            CountKatu = br.ReadUInt16(),
            CountMiss = br.ReadUInt16(),
            Score = br.ReadInt32(),
            MaxCombo = br.ReadUInt16(),
            Perfect = br.ReadByte() != 0,
            Mods = br.ReadInt32()
        };

        _ = ReadOsuString(br); // life bar graph
        long ticks = br.ReadInt64();
        try { r.TimestampUtc = new DateTime(ticks, DateTimeKind.Utc); }
        catch { r.TimestampUtc = File.GetLastWriteTimeUtc(path); }

        int compressedLength = br.ReadInt32();
        if (compressedLength < 0 || compressedLength > fs.Length - fs.Position)
            throw new InvalidDataException("Replay contains an invalid compressed-data length.");

        if (!includeFrames)
        {
            // History/backfill scans only need metadata to determine whether this replay
            // has already been imported. Avoiding LZMA decompression makes rescans cheap.
            return r;
        }

        byte[] compressed = br.ReadBytes(compressedLength);
        r.Frames = DecompressFrames(compressed);
        return r;
    }

    private static List<ReplayFrame> DecompressFrames(byte[] data)
    {
        if (data.Length < 14) return new List<ReplayFrame>();

        // osu! uses an LZMA-alone stream: 5 property bytes, 8-byte output size, payload.
        byte[] props = data.AsSpan(0, 5).ToArray();
        long outputSize = BitConverter.ToInt64(data, 5);
        using var payload = new MemoryStream(data, 13, data.Length - 13, writable: false);
        using var lzma = LzmaStream.Create(props, payload, payload.Length, outputSize);
        using var sr = new StreamReader(lzma, Encoding.UTF8);
        string text = sr.ReadToEnd();

        var frames = new List<ReplayFrame>(Math.Max(128, text.Length / 16));
        long time = 0;
        foreach (string token in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = token.Split('|');
            if (p.Length < 4) continue;
            if (!long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long delta)) continue;
            if (delta == -12345) break; // RNG seed sentinel
            if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
            if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
            if (!int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int keys)) continue;
            time += delta;
            if (!double.IsFinite(x) || !double.IsFinite(y)) continue;
            frames.Add(new ReplayFrame(time, x, y, keys));
        }
        // stable writes two non-gameplay marker frames at (256, -500) at the front of many replays.
        int markerCount = 0;
        while (frames.Count > 0 && markerCount < 2 && frames[0].Y < -400)
        {
            frames.RemoveAt(0);
            markerCount++;
        }
        return frames;
    }

    public static string ReadOsuString(BinaryReader br)
    {
        byte marker = br.ReadByte();
        if (marker == 0) return "";
        if (marker != 0x0b) throw new InvalidDataException($"Invalid osu! string marker: 0x{marker:X2}");
        ulong len = ReadUleb128(br);
        if (len > int.MaxValue) throw new InvalidDataException("String is too long.");
        return Encoding.UTF8.GetString(br.ReadBytes((int)len));
    }

    private static ulong ReadUleb128(BinaryReader br)
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = br.ReadByte();
            result |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("Invalid ULEB128 value.");
        }
    }
}
