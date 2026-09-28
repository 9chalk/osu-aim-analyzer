using System.Text;
using System.Text.RegularExpressions;

namespace OsuAimAnalyzer;

public sealed record BeatmapDocumentLine(string Section, string Text, string Ending);

/// <summary>Lossless text ownership for editing; deliberately separate from the analysis projection.</summary>
public sealed class BeatmapDocument
{
    public IReadOnlyList<BeatmapDocumentLine> Lines { get; }
    private readonly Encoding encoding;
    private readonly byte[] preamble;

    private BeatmapDocument(IEnumerable<BeatmapDocumentLine> lines, Encoding encoding, byte[] preamble)
    {
        Lines = Array.AsReadOnly(lines.ToArray());
        this.encoding = encoding;
        this.preamble = preamble.ToArray();
    }

    public static BeatmapDocument Parse(string text)
        => ParseCore(text, new UTF8Encoding(false, true), Array.Empty<byte>());

    public static BeatmapDocument FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding encoding = new UTF8Encoding(false, true);
        int skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        return ParseCore(encoding.GetString(bytes, skip, bytes.Length - skip), encoding, bytes[..skip]);
    }

    private static BeatmapDocument ParseCore(string text, Encoding encoding, byte[] preamble)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<BeatmapDocumentLine>();
        string section = "";
        foreach (Match match in Regex.Matches(text, @"([^\r\n]*)(\r\n|\r|\n|$)"))
        {
            if (match.Length == 0) continue;
            string raw = match.Groups[1].Value, trimmed = raw.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1];
            lines.Add(new(section, raw, match.Groups[2].Value));
        }
        return new(lines, encoding, preamble);
    }

    public override string ToString() => string.Concat(Lines.Select(l => l.Text + l.Ending));
    public byte[] ToBytes() => preamble.Concat(encoding.GetBytes(ToString())).ToArray();
    internal BeatmapDocument Replace(IReadOnlyDictionary<int, string> replacements)
        => new(Lines.Select((l, i) => replacements.TryGetValue(i, out var text) ? l with { Text = text } : l), encoding, preamble);

    internal BeatmapDocument WithMetadata(IReadOnlyDictionary<string, string> values)
    {
        if (Lines.Count(l => l.Text.Trim() == "[Metadata]") != 1)
            throw new NotSupportedException("Export requires exactly one Metadata section.");
        var remaining = new Dictionary<string, string>(values);
        var seen = new HashSet<string>();
        var output = new List<BeatmapDocumentLine>();
        string ending = Lines.FirstOrDefault(l => l.Ending.Length > 0)?.Ending ?? "\r\n";
        int last = Lines.Select((l, i) => (l, i)).Last(pair => pair.l.Section == "Metadata").i;
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            int colon = line.Text.IndexOf(':');
            if (line.Section == "Metadata" && colon > 0 && IsContent(line))
            {
                string key = line.Text[..colon].Trim();
                if (!seen.Add(key)) throw new NotSupportedException("Duplicate metadata key: " + key);
                if (remaining.Remove(key, out var value)) line = line with { Text = key + ":" + value };
            }
            output.Add(line);
            if (i == last && remaining.Count > 0)
            {
                if (output[^1].Ending.Length == 0) output[^1] = output[^1] with { Ending = ending };
                foreach (var pair in remaining) output.Add(new("Metadata", pair.Key + ":" + pair.Value, ending));
            }
        }
        return new(output, encoding, preamble);
    }

    internal static bool IsContent(BeatmapDocumentLine line)
    {
        string text = line.Text.Trim();
        return text.Length > 0 && !text.StartsWith("//") && !text.StartsWith('[');
    }

    internal static string[] Csv(string text)
    {
        var fields = new List<string>();
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') quoted = !quoted;
            if (text[i] == ',' && !quoted) { fields.Add(text[start..i]); start = i + 1; }
        }
        if (quoted) throw new FormatException("Unclosed quoted beatmap field.");
        fields.Add(text[start..]);
        return fields.ToArray();
    }
}
