namespace Mp3Organizer;

public sealed class AudioFileScanner
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".wav", ".wma" };
    public List<string> Errors { get; } = new();
    public List<string> Playlists { get; } = new();
    public IReadOnlyList<string> Scan(string root, Action<int,string>? progress = null)
    {
        Errors.Clear();Playlists.Clear();
        PathSafetyGuard.NoLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var files = new List<string>();
        Visit(Path.GetFullPath(root));
        return files.Order(StringComparer.OrdinalIgnoreCase).ToList();
        void Visit(string directory)
        {
            progress?.Invoke(files.Count,directory);
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if(Path.GetFileName(entry).StartsWith(".mp3organizer-tags-",StringComparison.OrdinalIgnoreCase)){Errors.Add("Unfinished tag staging file; inspect workspace journal: "+entry);continue;}
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { Errors.Add($"Skipped link: {entry}"); continue; }
                    if ((attributes & FileAttributes.Directory) != 0) Visit(entry);
                    else if (Extensions.Contains(Path.GetExtension(entry))) { files.Add(entry); progress?.Invoke(files.Count,directory); }
                    else if(SourcePlaylistReader.Extensions.Contains(Path.GetExtension(entry)))Playlists.Add(entry);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Errors.Add($"{directory}: {e.Message}"); }
        }
    }
}
