namespace OsuAimAnalyzer;

internal static class PracticeExportPaths
{
    public static string Resource(string root, string relative)
    {
        string normalized = relative.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(p)))
            throw new IOException("Unsafe resource path: " + relative);
        string full = Path.GetFullPath(Path.Combine(root, normalized));
        if (!Within(full, root)) throw new IOException("Resource escapes its mapset: " + relative);
        CheckNoLinks(full);
        return full;
    }

    public static string Destination(string path, string sourceDirectory, IEnumerable<string> forbiddenRoots)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an absolute .osz destination.", nameof(path));
        path = Path.GetFullPath(path);
        if (Within(path, sourceDirectory) || forbiddenRoots.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => Within(path, Path.GetFullPath(p))))
            throw new IOException("Choose an output folder outside the source mapset and Songs folder.");
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("reference", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("reference/ is read-only and cannot be an export destination.");
        string directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing output directory.");
        CheckNoLinks(directory);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("The destination already exists. Choose a new filename; exports never overwrite files.");
        string filename = Path.GetFileName(path);
        if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || filename.EndsWith(' ') || IsDeviceName(filename))
            throw new IOException("Unsupported output filename.");
        return path;
    }

    public static void CheckNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked/reparse-point paths are not supported for export: " + current);
        }
    }

    private static bool Within(string path, string root)
        => path.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsDeviceName(string segment)
    {
        string name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && char.IsDigit(name[3]));
    }
}
