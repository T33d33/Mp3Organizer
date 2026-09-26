namespace Mp3Organizer;

public static class PathSafetyGuard
{
    // This source is permanently protected even when it is not supplied on the command line.
    public const string ProtectedSource = @"\\192.168.0.124\Public\mp3";
    public static string Canonical(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) throw new IOException("Device/extended paths are not supported.");
        var full = Path.GetFullPath(path);
        var tail = full[Path.GetPathRoot(full)!.Length..];
        if (tail.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(x => x.EndsWith(' ') || x.EndsWith('.') || x.Contains(':'))) throw new IOException("Ambiguous Windows paths and alternate data streams are not supported.");
        return Path.TrimEndingDirectorySeparator(full);
    }
    public static bool Within(string path, string root) => string.Equals(Canonical(path), Canonical(root), StringComparison.OrdinalIgnoreCase)
        || Canonical(path).StartsWith(Canonical(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void NoLinks(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            if ((Directory.Exists(current.FullName) || File.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Links/reparse points are not allowed: {current.FullName}");
    }
    public static void Writable(string path, string? source = null)
    {
        if (Within(path, ProtectedSource) || (source != null && Within(path, source)))
            throw new IOException("Writing into a source library is forbidden.");
        NoLinks(path);
    }
    public static void Separate(string source, string target)
    {
        if (Within(source, target) || Within(target, source)) throw new IOException("Source and target must not overlap.");
        NoLinks(source);
        Writable(target, source);
    }
    public static string Destination(string target, string relative, string? source = null)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("Expected a relative target path.");
        var path = Path.GetFullPath(Path.Combine(target, relative));
        if (!Within(path, target) || Canonical(path) == Canonical(target)) throw new IOException("Target path escapes its root.");
        Writable(path, source);
        return path;
    }
}
