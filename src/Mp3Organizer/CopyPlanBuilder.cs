namespace Mp3Organizer;

public sealed class CopyPlanBuilder
{
    public CopyPlan Build(string source, string target, IMetadataResolver? resolver = null, bool identifyAll = false, Action<string>? progress = null)
    {
        source = PathSafetyGuard.Canonical(source); target = PathSafetyGuard.Canonical(target);
        PathSafetyGuard.Separate(source, target);
        ManagedTarget.Check(target, source);
        var plan = new CopyPlan { Source = source, Target = target, ExpectedTargetSnapshot = TargetSnapshot.Hash(target) };
        var scanner = new AudioFileScanner();
        var reader = new TagLibMetadataReader(new ReadOnlySource());
        progress?.Invoke("Scanning source and reading metadata/SHA-256...");
        var inventory = scanner.Scan(source).Select(path=>{progress?.Invoke("Reading: "+Path.GetFileName(path));return reader.Read(path);}).ToList();
        plan.Conflicts.AddRange(scanner.Errors);
        plan.Conflicts.AddRange(inventory.Where(x => x.Error != "").Select(x => "Metadata read failed: " + x.FullPath + ": " + x.Error));
        if (resolver != null) inventory = IdentificationSession.ResolveAll(inventory,resolver,identifyAll,progress);
        progress?.Invoke("Calculating duplicates, target paths and playlists...");
        var selection = new DuplicateDetector(new Sha256EquivalenceVerifier()).Select(inventory.Where(x => x.Error == "").ToList());
        plan.Duplicates = selection.Decisions;
        var existing = Directory.Exists(target) ? scanner.Scan(target).Select(reader.Read).ToList() : new List<AudioMetadata>();
        plan.Conflicts.AddRange(scanner.Errors);
        plan.Conflicts.AddRange(existing.Where(x => x.Error != "").Select(x => "Target metadata read failed: " + x.FullPath));
        if (Directory.Exists(target)) existing = TargetMetadataStore.Overlay(target,existing);
        if(resolver is ManualMetadataResolver manualResolver)existing=manualResolver.ReconcileTarget(target,existing);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = existing.Select(x => new CopyOperation(x, Path.GetRelativePath(target, x.FullPath), "Existing")).ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var row in selection.Selected)
        {
            var relative = TargetPathBuilder.Build(row);
            var initial = relative;
            var suffix = 1;
            while (occupied.Contains(relative) || Directory.Exists(Path.Combine(target, relative)) || (File.Exists(Path.Combine(target, relative)) && (!tracks.TryGetValue(relative, out var found) || found.Metadata.Sha256 != row.Sha256)))
            {
                relative = Path.Combine(Path.GetDirectoryName(initial)!, Path.GetFileNameWithoutExtension(initial) + $" [{row.Sha256[..8]}-{suffix++}]" + row.Extension);
            }
            occupied.Add(relative);
            if (relative != initial) plan.ResolvedConflicts.Add($"Filename collision: {initial}; retained as {relative}");
            var destination = PathSafetyGuard.Destination(target, relative, source);
            if (destination.Length >= 240 || relative.Split(Path.DirectorySeparatorChar).Any(x => x.Length > 180)) plan.Conflicts.Add("Target path exceeds conservative Windows limits: " + relative);
            var operation = new CopyOperation(row, relative, File.Exists(destination) ? "AlreadyPresent" : "Copy");
            plan.Operations.Add(operation);
            tracks[relative] = operation;
        }
        plan.PlaylistMap = new PlaylistMapStore().Load(target);
        plan.Playlists = new PlaylistBuilder().Build(tracks.Values.ToList(), plan.PlaylistMap);
        plan.TargetMetadata = tracks.ToDictionary(x => x.Key,x => x.Value.Metadata,StringComparer.OrdinalIgnoreCase);
        PlaylistBuilder.IncludeRetired(plan.Playlists, ManagedTarget.Check(target, source));
        foreach (var playlist in plan.Playlists)
            if (PathSafetyGuard.Destination(target, playlist.RelativePath, source).Length >= 240) plan.Conflicts.Add("Playlist path exceeds conservative Windows limits: " + playlist.RelativePath);
        // Inventory includes unreadable and skipped files, independently of selected operations.
        plan.Library = inventory;
        return plan;
    }
}
public sealed record ManagedTargetState(int SchemaVersion, string Source, List<string> Playlists, bool IndexManaged = false, bool MetadataManaged = false);
public static class ManagedTarget
{
    public const string Marker = ".mp3organizer.json";
    public static ManagedTargetState? Check(string target, string? source = null)
    {
        PathSafetyGuard.Writable(target, source);
        var marker = Path.Combine(target, Marker);
        if (!File.Exists(marker))
        {
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Target is nonempty and not managed by Mp3Organizer. Use an empty target.");
            return null;
        }
        PathSafetyGuard.NoLinks(marker);
        var state = JsonFormat.Read<ManagedTargetState>(File.ReadAllText(marker));
        if (state.SchemaVersion != 1 || string.IsNullOrWhiteSpace(state.Source)) throw new IOException("Invalid target marker.");
        PathSafetyGuard.Separate(state.Source, target);
        if (source != null && !string.Equals(PathSafetyGuard.Canonical(state.Source), PathSafetyGuard.Canonical(source), StringComparison.OrdinalIgnoreCase)) throw new IOException("Target belongs to a different source library.");
        return state;
    }
}
