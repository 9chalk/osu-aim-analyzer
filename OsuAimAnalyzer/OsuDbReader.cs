using System.Text;

namespace OsuAimAnalyzer;

public sealed class OsuDbBeatmap
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string Md5 { get; set; } = "";
    public string OsuFileName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public int RankedStatus { get; set; }
    public byte Mode { get; set; }
    public double AR { get; set; }
    public double CS { get; set; }
    public double HP { get; set; }
    public double OD { get; set; }
    public double SliderVelocity { get; set; }
    public int BeatmapId { get; set; }
    public int BeatmapSetId { get; set; }
    public Dictionary<int, double> StandardStars { get; set; } = new();
}

public static class OsuDbReader
{
    public static Dictionary<string, OsuDbBeatmap> Read(string dbPath)
    {
        using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        int version = br.ReadInt32();
        _ = br.ReadInt32(); // folder count
        _ = br.ReadByte(); // account unlocked
        _ = br.ReadInt64(); // unlock date
        _ = ReplayReader.ReadOsuString(br); // player
        int beatmapCount = br.ReadInt32();
        var result = new Dictionary<string, OsuDbBeatmap>(Math.Max(beatmapCount, 0), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < beatmapCount; i++)
        {
            long entryStart = fs.Position;
            int entrySize = 0;
            if (version < 20191106) entrySize = br.ReadInt32();
            try
            {
                var map = ReadBeatmap(br, version);
                if (!string.IsNullOrWhiteSpace(map.Md5)) result[map.Md5] = map;
            }
            catch
            {
                if (entrySize > 0)
                {
                    fs.Position = entryStart + 4 + entrySize;
                    continue;
                }
                throw;
            }
        }
        return result;
    }

    private static OsuDbBeatmap ReadBeatmap(BinaryReader br, int version)
    {
        var m = new OsuDbBeatmap
        {
            Artist = ReplayReader.ReadOsuString(br)
        };
        _ = ReplayReader.ReadOsuString(br); // artist unicode
        m.Title = ReplayReader.ReadOsuString(br);
        _ = ReplayReader.ReadOsuString(br); // title unicode
        m.Creator = ReplayReader.ReadOsuString(br);
        m.Difficulty = ReplayReader.ReadOsuString(br);
        _ = ReplayReader.ReadOsuString(br); // audio file
        m.Md5 = ReplayReader.ReadOsuString(br);
        m.OsuFileName = ReplayReader.ReadOsuString(br);
        m.RankedStatus = br.ReadByte();
        _ = br.ReadUInt16(); // circles
        _ = br.ReadUInt16(); // sliders
        _ = br.ReadUInt16(); // spinners
        _ = br.ReadInt64();

        if (version < 20140609)
        {
            m.AR = br.ReadByte(); m.CS = br.ReadByte(); m.HP = br.ReadByte(); m.OD = br.ReadByte();
        }
        else
        {
            m.AR = br.ReadSingle(); m.CS = br.ReadSingle(); m.HP = br.ReadSingle(); m.OD = br.ReadSingle();
        }
        m.SliderVelocity = br.ReadDouble();

        if (version >= 20140609)
        {
            m.StandardStars = ReadStars(br, version);
            SkipStars(br, version); // taiko
            SkipStars(br, version); // catch
            SkipStars(br, version); // mania
        }

        _ = br.ReadInt32(); // drain time
        _ = br.ReadInt32(); // total time
        _ = br.ReadInt32(); // preview time
        int timingCount = br.ReadInt32();
        for (int t = 0; t < timingCount; t++)
        {
            _ = br.ReadDouble(); _ = br.ReadDouble(); _ = br.ReadByte();
        }

        m.BeatmapId = br.ReadInt32();
        m.BeatmapSetId = br.ReadInt32();
        _ = br.ReadInt32(); // thread id
        _ = br.ReadByte(); _ = br.ReadByte(); _ = br.ReadByte(); _ = br.ReadByte();
        _ = br.ReadInt16(); // local offset
        _ = br.ReadSingle(); // stack leniency
        m.Mode = br.ReadByte();
        _ = ReplayReader.ReadOsuString(br); // source
        _ = ReplayReader.ReadOsuString(br); // tags
        _ = br.ReadInt16(); // online offset
        _ = ReplayReader.ReadOsuString(br); // font
        _ = br.ReadByte(); // unplayed
        _ = br.ReadInt64(); // last played
        _ = br.ReadByte(); // osz2
        m.FolderName = ReplayReader.ReadOsuString(br);
        _ = br.ReadInt64(); // last checked
        _ = br.ReadByte(); _ = br.ReadByte(); _ = br.ReadByte(); _ = br.ReadByte(); _ = br.ReadByte();
        if (version < 20140609) _ = br.ReadInt16();
        _ = br.ReadInt32();
        _ = br.ReadByte();
        return m;
    }

    private static Dictionary<int, double> ReadStars(BinaryReader br, int version)
    {
        int count = br.ReadInt32();
        var d = new Dictionary<int, double>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            byte keyTag = br.ReadByte();
            if (keyTag != 0x08) throw new InvalidDataException("Invalid star pair key marker.");
            int mods = br.ReadInt32();
            byte valueTag = br.ReadByte();
            double value;
            if (version >= 20250107)
            {
                if (valueTag != 0x0c) throw new InvalidDataException("Invalid star pair float marker.");
                value = br.ReadSingle();
            }
            else
            {
                if (valueTag != 0x0d) throw new InvalidDataException("Invalid star pair double marker.");
                value = br.ReadDouble();
            }
            d[mods] = value;
        }
        return d;
    }

    private static void SkipStars(BinaryReader br, int version)
    {
        int count = br.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            _ = br.ReadByte(); _ = br.ReadInt32(); _ = br.ReadByte();
            if (version >= 20250107) _ = br.ReadSingle(); else _ = br.ReadDouble();
        }
    }
}
