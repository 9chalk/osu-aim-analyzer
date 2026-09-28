using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace OsuAimAnalyzer;

public sealed class SelectedBeatmapInfo
{
    public string OsuFileName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string ProcessPath { get; set; } = "";
    public string FullPath { get; set; } = "";
    public int Pid { get; set; }
}

public static class SelectedBeatmapReader
{
    public static bool IsInstalled()
    {
        string native = Path.Combine(AppContext.BaseDirectory, "native");
        return File.Exists(Path.Combine(native, "OsuMemoryDataProvider.dll"))
            && File.Exists(Path.Combine(native, "ProcessMemoryDataFinder.dll"))
            && File.Exists(Path.Combine(AppContext.BaseDirectory, "memory_reader.ps1"));
    }

    public static async Task<SelectedBeatmapInfo?> TryReadAsync(string songsDirectory, CancellationToken token = default)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "memory_reader.ps1");
        if (!File.Exists(script)) return null;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\""
        };

        using var proc = Process.Start(psi);
        if (proc == null) return null;
        string stdout = await proc.StandardOutput.ReadToEndAsync(token);
        string stderr = await proc.StandardError.ReadToEndAsync(token);
        await proc.WaitForExitAsync(token);
        if (string.IsNullOrWhiteSpace(stdout)) return null;

        string jsonLine = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return null;

        string file = root.TryGetProperty("osuFileName", out var f) ? f.GetString() ?? "" : "";
        string folder = root.TryGetProperty("folderName", out var fo) ? fo.GetString() ?? "" : "";
        string processPath = root.TryGetProperty("processPath", out var pp) ? pp.GetString() ?? "" : "";
        int pid = root.TryGetProperty("pid", out var p) && p.TryGetInt32(out int id) ? id : 0;
        if (string.IsNullOrWhiteSpace(file)) return null;

        string candidate = string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(songsDirectory, file)
            : Path.Combine(songsDirectory, folder.TrimEnd('\\','/'), file);
        if (!File.Exists(candidate) && Directory.Exists(songsDirectory))
        {
            try
            {
                candidate = Directory.EnumerateFiles(songsDirectory, file, SearchOption.AllDirectories).FirstOrDefault() ?? candidate;
            }
            catch { }
        }
        if (!File.Exists(candidate)) return null;

        return new SelectedBeatmapInfo
        {
            OsuFileName = file,
            FolderName = folder,
            ProcessPath = processPath,
            FullPath = candidate,
            Pid = pid
        };
    }

    public static string Md5(string path)
    {
        using var md5 = MD5.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(fs)).ToLowerInvariant();
    }

    public static void LaunchInstaller()
    {
        string script = Path.Combine(AppContext.BaseDirectory, "setup_native.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("setup_native.ps1 was not found.", script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\""
        });
    }
}
