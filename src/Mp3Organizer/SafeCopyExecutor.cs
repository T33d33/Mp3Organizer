using System.Security.Cryptography;

namespace Mp3Organizer;

public sealed class SafeCopyExecutor
{
    public void Execute(CopyPlan plan, string source, string target, bool dryRun)
    {
        new CopyPlanValidator().Validate(plan, source, target);
        if (dryRun) return;
        PathSafetyGuard.Writable(target, source);
        Directory.CreateDirectory(target);
        // Mark ownership before the first copy so an interrupted run can be replanned safely.
        var old = ManagedTarget.Check(target, source);
        if (old == null)
        {
            new M3u8Writer().WriteNew(Path.Combine(target, ManagedTarget.Marker), JsonFormat.Serialize(new ManagedTargetState(1, source, new())), source);
            new M3u8Writer().WriteNew(Path.Combine(target, "playlist-map.json"), JsonFormat.Serialize(plan.PlaylistMap), source);
        }
        foreach (var op in plan.Operations.Where(x => x.Action == "Copy"))
        {
            var destination = PathSafetyGuard.Destination(target, op.RelativePath, source);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
            PathSafetyGuard.Writable(temporary, source);
            // Open source without write/delete sharing; recheck hash during the copy.
            using (var input = new ReadOnlySource().OpenRead(op.Metadata.FullPath))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
                output.Position = 0;
                if (output.Length != op.Metadata.Size || Convert.ToHexString(SHA256.HashData(output)) != op.Metadata.Sha256) throw new IOException("Copy verification failed; partial target retained for inspection.");
            }
            PathSafetyGuard.Writable(destination, source);
            PathSafetyGuard.Writable(temporary, source);
            File.Move(temporary, destination, false);
        }
        new ManagedPlaylistWriter().Write(target, source, plan.Playlists, plan.PlaylistMap, plan.TargetMetadata.Count > 0 ? plan.TargetMetadata : plan.Operations.ToDictionary(x => x.RelativePath,x => x.Metadata,StringComparer.OrdinalIgnoreCase));
    }
}
public sealed class ManagedPlaylistWriter
{
    public void Write(string target, string source, List<PlaylistDefinition> playlists, PlaylistMap map, Dictionary<string,AudioMetadata>? metadata = null)
    {
        PathSafetyGuard.Separate(source, target);
        var state = ManagedTarget.Check(target, source) ?? throw new IOException("Not a managed target.");
        PlaylistMapStore.Validate(map);
        PlaylistMapStore.ValidateExtension(new PlaylistMapStore().Load(target), map);
        PlaylistIndexBuilder.ValidateDestination(target, source, state);
        if (metadata != null) TargetMetadataStore.ValidateDestination(target,source,state);
        var index = new PlaylistIndexBuilder().Csv(map, playlists);
        // Validate ownership for the entire set before changing any generated file.
        foreach (var playlist in playlists)
        {
            var path = PathSafetyGuard.Destination(target, playlist.RelativePath, source);
            if ((!PathSafetyGuard.Within(path, Path.Combine(target, "_Playlists"))&&!PathSafetyGuard.Within(path,Path.Combine(target,"_Playlist-Folder"))) || Path.GetExtension(path) != ".m3u8") throw new IOException("Invalid managed playlist path.");
            if (File.Exists(path) && !state.Playlists.Contains(playlist.RelativePath, StringComparer.OrdinalIgnoreCase)) throw new IOException("Unmanaged playlist collision: " + path);
        }
        Replace("playlist-map.json", JsonFormat.Serialize(map));
        if (metadata != null) Replace(TargetMetadataStore.FileName,JsonFormat.Serialize(metadata));
        foreach (var playlist in playlists) Replace(playlist.RelativePath, playlist.Content);
        Replace(PlaylistIndexBuilder.FileName, index);
        Replace(ManagedTarget.Marker, JsonFormat.Serialize(new ManagedTargetState(1, source, state.Playlists.Concat(playlists.Select(x => x.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), true, state.MetadataManaged || metadata != null)));
        void Replace(string relative, string content)
        {
            var path = PathSafetyGuard.Destination(target, relative, source);
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            var temporary = path + ".new-" + Guid.NewGuid().ToString("N");
            new M3u8Writer().WriteNew(temporary, content, source);
            if (File.Exists(path))
            {
                var backup = PathSafetyGuard.Destination(target, Path.Combine(".mp3organizer-backups", Guid.NewGuid().ToString("N"), relative), source);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                PathSafetyGuard.Writable(backup, source);
                File.Copy(path, backup, false);
            }
            PathSafetyGuard.Writable(path, source);
            File.Move(temporary, path, true);
        }
    }
}
