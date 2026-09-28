namespace OsuAimAnalyzer;

public static class ModUtils
{
    public const int NoFail = 1;
    public const int Easy = 2;
    public const int TouchDevice = 4;
    public const int Hidden = 8;
    public const int HardRock = 16;
    public const int SuddenDeath = 32;
    public const int DoubleTime = 64;
    public const int Relax = 128;
    public const int HalfTime = 256;
    public const int Nightcore = 512;
    public const int Flashlight = 1024;
    public const int Autoplay = 2048;
    public const int SpunOut = 4096;
    public const int Autopilot = 8192;
    public const int Perfect = 16384;

    public static double ClockRate(int mods)
    {
        if ((mods & (DoubleTime | Nightcore)) != 0) return 1.5;
        if ((mods & HalfTime) != 0) return 0.75;
        return 1.0;
    }

    public static double ApplyDifficultyMods(double value, int mods)
    {
        if ((mods & Easy) != 0) value *= 0.5;
        if ((mods & HardRock) != 0) value *= 1.4;
        return Math.Clamp(value, 0, 10);
    }

    public static int NormalizeForStarLookup(int mods)
    {
        // First lookup uses exact replay mask. This fallback keeps only mods that commonly
        // appear as difficulty combinations in stable's star cache.
        int keep = Easy | Hidden | HardRock | DoubleTime | HalfTime | Nightcore | Flashlight | TouchDevice;
        int result = mods & keep;
        if ((result & Nightcore) != 0) result |= DoubleTime;
        return result;
    }

    public static string ToShortString(int mods)
    {
        if (mods == 0) return "NM";
        var parts = new List<string>();
        void Add(int bit, string name) { if ((mods & bit) != 0) parts.Add(name); }
        Add(NoFail, "NF"); Add(Easy, "EZ"); Add(TouchDevice, "TD"); Add(Hidden, "HD");
        Add(HardRock, "HR"); Add(SuddenDeath, "SD");
        if ((mods & Nightcore) != 0) parts.Add("NC"); else Add(DoubleTime, "DT");
        Add(Relax, "RX"); Add(HalfTime, "HT"); Add(Flashlight, "FL"); Add(SpunOut, "SO");
        Add(Autopilot, "AP"); Add(Perfect, "PF");
        return parts.Count == 0 ? $"MODS:{mods}" : string.Join("", parts);
    }
}
