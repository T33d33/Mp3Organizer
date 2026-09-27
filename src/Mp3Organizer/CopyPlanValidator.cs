using System.Security.Cryptography;

namespace Mp3Organizer;

public sealed class CopyPlanValidator
{
    public void Validate(CopyPlan plan, string source, string target)
    {
        if(plan.MetadataPolicyVersion!=2)throw new IOException("Plan predates folder-independent metadata policy; run plan/run again.");
        if (plan.SchemaVersion != 1 || plan.Source != PathSafetyGuard.Canonical(source) || plan.Target != PathSafetyGuard.Canonical(target)) throw new IOException("Plan roots/version do not match this command.");
        PathSafetyGuard.Separate(source, target);
        if(plan.ManualFilePath!="")
        {
            var manual=new ManualResolutionStore(Path.GetDirectoryName(plan.ManualFilePath)!,source,target);
            if(!string.Equals(manual.FilePath,plan.ManualFilePath,StringComparison.OrdinalIgnoreCase)||manual.Revision()!=plan.ManualFileRevision)throw new IOException("Manual resolutions changed since planning; create a new plan.");
        }
        ManagedTarget.Check(target, source);
        if (plan.Conflicts.Count != 0) throw new IOException("Plan has blocking conflicts; inspect conflicts.csv and create a new plan.");
        if (TargetSnapshot.Hash(target) != plan.ExpectedTargetSnapshot) throw new IOException("Target changed since planning. Create a new plan.");
        PlaylistMapStore.Validate(plan.PlaylistMap);
        PlaylistMapStore.ValidateExtension(new PlaylistMapStore().Load(target), plan.PlaylistMap);
        var scanner = new AudioFileScanner();
        if(plan.SelectedInventoryOnly&&string.IsNullOrEmpty(plan.ProgressWorkspace))throw new IOException("Selected-inventory plan requires progress database validation.");
        var currentFiles = plan.SelectedInventoryOnly?plan.Library.Select(x=>x.FullPath).ToList():scanner.Scan(source);
        if (scanner.Errors.Count > 0 || !currentFiles.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(plan.Library.Select(x => x.FullPath).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new IOException("Source inventory changed or became inaccessible. Create a new plan.");
        foreach (var row in plan.Library)
        {
            using var stream = new ReadOnlySource().OpenRead(row.FullPath);
            if (stream.Length != row.Size || Convert.ToHexString(SHA256.HashData(stream)) != row.Sha256) throw new IOException("Source inventory content changed: " + row.FullPath);
        }
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string,IndexedMusic>? indexed=null;
        if(plan.ProgressWorkspace!="")
        {
            PathSafetyGuard.Separate(source,plan.ProgressWorkspace);
            if(!File.Exists(Path.Combine(plan.ProgressWorkspace,"music-organizer.db")))throw new IOException("Progress database missing; create a new plan.");
            using var repository=new ProgressRepository(plan.ProgressWorkspace);indexed=repository.All().ToDictionary(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase);
        }
        foreach (var op in plan.Operations)
        {
            if(op.Metadata.Identification?.Status=="Review")throw new IOException("Unresolved metadata cannot be copied: "+op.Metadata.FullPath);
            if(indexed!=null)
            {
                indexed.TryGetValue(op.Metadata.FullPath,out var item);
                if(item!=null&&item.Effective.Identification?.MetadataPolicyVersion!=2)throw new IOException("Indexed metadata requires policy migration; run again before apply.");
                if(item!=null&&(item.Status is not (ProcessingStatus.Ready or ProcessingStatus.Processed)||item.Effective.Identification?.Status=="Review")||plan.SelectedInventoryOnly&&(item==null||item.Status!=ProcessingStatus.Ready))throw new IOException("File is not eligible for apply: "+op.Metadata.FullPath);
            }
            if (!PathSafetyGuard.Within(op.Metadata.FullPath, source)) throw new IOException("Source operation escapes source root.");
            if (op.Action is not ("Copy" or "AlreadyPresent")) throw new IOException("Unknown operation.");
            if (!AudioFileScanner.Extensions.Contains(Path.GetExtension(op.RelativePath))) throw new IOException("Invalid audio destination extension.");
            var path = PathSafetyGuard.Destination(target, op.RelativePath, source);
            if (!destinations.Add(path)) throw new IOException("Duplicate target destination.");
            using var input = new ReadOnlySource().OpenRead(op.Metadata.FullPath);
            if (input.Length != op.Metadata.Size || Convert.ToHexString(SHA256.HashData(input)) != op.Metadata.Sha256) throw new IOException("Source file changed: " + op.Metadata.FullPath);
            if (op.Action == "Copy" && (File.Exists(path) || Directory.Exists(path))) throw new IOException("Copy destination already exists.");
            if (op.Action == "AlreadyPresent")
            {
                using var existing = new ReadOnlySource().OpenRead(path);
                if (Convert.ToHexString(SHA256.HashData(existing)) != op.Metadata.Sha256) throw new IOException("Existing destination does not match source.");
            }
        }
        var state = ManagedTarget.Check(target, source);
        PlaylistIndexBuilder.ValidateDestination(target, source, state);
        TargetMetadataStore.ValidateDestination(target,source,state);
        foreach (var entry in plan.TargetMetadata)
        {
            var path = PathSafetyGuard.Destination(target,entry.Key,source);
            if (!AudioFileScanner.Extensions.Contains(Path.GetExtension(path))) throw new IOException("Invalid effective-metadata target path.");
            var copy = plan.Operations.FirstOrDefault(x => string.Equals(x.RelativePath,entry.Key,StringComparison.OrdinalIgnoreCase));
            if (copy != null && (copy.Metadata.Sha256 != entry.Value.Sha256 || copy.Metadata.Size != entry.Value.Size)) throw new IOException("Effective metadata does not match planned audio.");
            if (copy == null && !File.Exists(path)) throw new IOException("Effective metadata references missing audio.");
        }
        var owned = state?.Playlists.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new();
        foreach (var playlist in plan.Playlists)
        {
            var path = PathSafetyGuard.Destination(target, playlist.RelativePath, source);
            var relative = playlist.RelativePath.Replace('\\', '/');
            if ((!relative.StartsWith("_Playlists/Artists/", StringComparison.Ordinal) && !relative.StartsWith("_Playlists/Albums/", StringComparison.Ordinal)&&!relative.StartsWith("_Playlists/Folders/",StringComparison.Ordinal)&&!relative.StartsWith("_Playlists/Original Playlists/",StringComparison.Ordinal)&&!relative.StartsWith("_Playlist-Folder/",StringComparison.Ordinal)) || !relative.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid playlist destination.");
            if (!destinations.Add(path)) throw new IOException("Duplicate playlist destination.");
            if (File.Exists(path) && !owned.Contains(playlist.RelativePath)) throw new IOException("Refusing to replace an unmanaged playlist: " + path);
            if (!playlist.Content.StartsWith("#EXTM3U\n", StringComparison.Ordinal)) throw new IOException("Invalid playlist content.");
            foreach (var entry in playlist.Content.Split('\n').Where(x => x.Length > 0 && !x.StartsWith('#')))
            {
                if (Path.IsPathRooted(entry)) throw new IOException("Playlist contains absolute path.");
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, entry));
                if (!PathSafetyGuard.Within(resolved, target) || !AudioFileScanner.Extensions.Contains(Path.GetExtension(resolved))) throw new IOException("Playlist escapes target or references a non-audio file.");
                if (!File.Exists(resolved) && !plan.Operations.Any(x => string.Equals(Path.GetFullPath(Path.Combine(target, x.RelativePath)), resolved, StringComparison.OrdinalIgnoreCase))) throw new IOException("Playlist references unknown audio.");
            }
        }
    }
}
