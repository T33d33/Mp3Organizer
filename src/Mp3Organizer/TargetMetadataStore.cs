namespace Mp3Organizer;

public static class TargetMetadataStore
{
    public const string FileName = ".mp3organizer-metadata.json";
    public static void ValidateDestination(string target,string source,ManagedTargetState? state)
    {
        var path = PathSafetyGuard.Destination(target,FileName,source);
        if (Directory.Exists(path) || File.Exists(path) && state?.MetadataManaged != true) throw new IOException("Unmanaged effective-metadata manifest collision.");
        if (state?.MetadataManaged == true && !File.Exists(path)) throw new IOException("Effective metadata manifest missing; restore its backup before proceeding.");
    }
    public static List<AudioMetadata> Overlay(string target,IReadOnlyList<AudioMetadata> rows)
    {
        var state = ManagedTarget.Check(target);
        if (state?.MetadataManaged != true) return rows.ToList();
        var path = PathSafetyGuard.Destination(target,FileName,state.Source);
        if (!File.Exists(path)) throw new IOException("Effective metadata manifest missing; restore its backup.");
        var entries = JsonFormat.Read<Dictionary<string,AudioMetadata>>(File.ReadAllText(path));
        var lookup = new Dictionary<string,AudioMetadata>(entries,StringComparer.OrdinalIgnoreCase);
        return rows.Select(row => lookup.TryGetValue(Path.GetRelativePath(target,row.FullPath),out var saved) && saved.Sha256 == row.Sha256 && saved.Size == row.Size && row.Error == ""
            ? saved with {FullPath=row.FullPath,OriginalSourcePath=saved.OriginalSourcePath==""?saved.FullPath:saved.OriginalSourcePath,FileName=row.FileName,Error=row.Error} : row).ToList();
    }
}
