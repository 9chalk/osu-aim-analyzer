using System.Globalization;

namespace OsuAimAnalyzer;

public static class BeatmapParser
{
    public static BeatmapData Parse(string path, OsuDbBeatmap? cached = null)
        => ParseLines(File.ReadLines(path), path, cached, true);

    // Preview documents never inherit the source map's hash, ranked status or cached stars.
    public static BeatmapData ParseDocument(BeatmapDocument document)
        => ParseLines(document.Lines.Select(l => l.Text), "", null, false);

    private static BeatmapData ParseLines(IEnumerable<string> lines, string path, OsuDbBeatmap? cached, bool resolveBackground)
    {
        var map = new BeatmapData
        {
            Path = path,
            Hash = cached?.Md5 ?? "",
            Artist = cached?.Artist ?? "",
            Title = cached?.Title ?? "",
            Creator = cached?.Creator ?? "",
            Version = cached?.Difficulty ?? "",
            BeatmapId = cached?.BeatmapId ?? 0,
            BeatmapSetId = cached?.BeatmapSetId ?? 0,
            RankedStatus = cached?.RankedStatus ?? 0,
            Mode = cached?.Mode ?? 0,
            AR = cached?.AR ?? 5,
            CS = cached?.CS ?? 5,
            OD = cached?.OD ?? 5,
            HP = cached?.HP ?? 5,
            SliderMultiplier = cached?.SliderVelocity ?? 1.4,
            StandardStars = cached?.StandardStars ?? new Dictionary<int, double>()
        };

        string section = "";
        int objectIndex = 0;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line; continue; }

            if (section is "[General]" or "[Metadata]" or "[Difficulty]")
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                switch (section)
                {
                    case "[General]" when key == "Mode": if (byte.TryParse(value, out var mode)) map.Mode = mode; break;
                    case "[Metadata]" when key == "Artist": map.Artist = value; break;
                    case "[Metadata]" when key == "Title": map.Title = value; break;
                    case "[Metadata]" when key == "Creator": map.Creator = value; break;
                    case "[Metadata]" when key == "Version": map.Version = value; break;
                    case "[Metadata]" when key == "BeatmapID": if (int.TryParse(value, out var bid)) map.BeatmapId = bid; break;
                    case "[Metadata]" when key == "BeatmapSetID": if (int.TryParse(value, out var sid)) map.BeatmapSetId = sid; break;
                    case "[Difficulty]" when key == "ApproachRate": map.AR = D(value, map.AR); break;
                    case "[Difficulty]" when key == "CircleSize": map.CS = D(value, map.CS); break;
                    case "[Difficulty]" when key == "OverallDifficulty": map.OD = D(value, map.OD); break;
                    case "[Difficulty]" when key == "HPDrainRate": map.HP = D(value, map.HP); break;
                    case "[Difficulty]" when key == "SliderMultiplier": map.SliderMultiplier = D(value, map.SliderMultiplier); break;
                }
            }
            else if (section == "[Events]")
            {
                // Standard background event: 0,0,"background.jpg",0,0
                // Generated/local maps sometimes omit quotes, so support both forms.
                if (string.IsNullOrWhiteSpace(map.BackgroundPath) && line.StartsWith("0,0,", StringComparison.Ordinal))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 3)
                    {
                        string file = parts[2].Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(file))
                        {
                            string candidate = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", file);
                            if (resolveBackground && File.Exists(candidate)) map.BackgroundPath = candidate;
                        }
                    }
                }
            }
            else if (section == "[TimingPoints]")
            {
                var p = line.Split(',');
                if (p.Length >= 7 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
                    && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength))
                {
                    bool uninherited = p[6].Trim() == "1";
                    map.TimingPoints.Add(new TimingPointData(time, beatLength, uninherited));
                }
            }
            else if (section == "[HitObjects]")
            {
                var p = line.Split(',');
                if (p.Length < 4) continue;
                if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
                if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
                if (!long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long time)) continue;
                if (!int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)) continue;
                HitObjectKind kind = (type & 1) != 0 ? HitObjectKind.Circle : (type & 2) != 0 ? HitObjectKind.Slider : (type & 8) != 0 ? HitObjectKind.Spinner : HitObjectKind.Other;
                map.HitObjects.Add(new HitObjectData { Index = objectIndex++, X = x, Y = y, TimeMs = time, Kind = kind });
            }
        }
        map.TimingPoints.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return map;
    }

    private static double D(string s, double fallback) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static double BpmAt(BeatmapData map, long timeMs, double clockRate)
    {
        double beatLength = 500;
        foreach (var tp in map.TimingPoints)
        {
            if (tp.TimeMs > timeMs) break;
            if (tp.Uninherited && tp.BeatLength > 0) beatLength = tp.BeatLength;
        }
        return 60000.0 / beatLength * clockRate;
    }
}
