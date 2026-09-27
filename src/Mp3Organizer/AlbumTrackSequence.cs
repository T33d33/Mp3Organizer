namespace Mp3Organizer;

public static class AlbumTrackSequence
{
    public static uint Suggest(IndexedMusic current,IReadOnlyList<IndexedMusic> inventory,IReadOnlyCollection<string>? scopeIds=null)
    {
        if(current.Effective.Track>0)return current.Effective.Track;
        var folder=Path.GetDirectoryName(current.CurrentPath);
        var peers=inventory.Where(x=>x.Id!=current.Id&&x.SourceRoot.Equals(current.SourceRoot,StringComparison.OrdinalIgnoreCase)
            &&(scopeIds!=null?scopeIds.Contains(x.Id):string.Equals(Path.GetDirectoryName(x.CurrentPath),folder,StringComparison.OrdinalIgnoreCase))
            &&MetadataNormalizer.Key(x.Effective.Album)==MetadataNormalizer.Key(current.Effective.Album)
            &&MetadataNormalizer.Key(AlbumGrouper.Owner(x.Effective))==MetadataNormalizer.Key(AlbumGrouper.Owner(current.Effective))
            &&x.Effective.Track>0&&x.Status!=ProcessingStatus.Skipped&&x.Status!=ProcessingStatus.Error
            &&(x.Status is ProcessingStatus.Ready or ProcessingStatus.Processed||x.Effective.Identification?.FieldSources.GetValueOrDefault("TrackNumber") is "FileManualOverride" or "FolderManualOverride"))
            .OrderBy(x=>x.CurrentPath,NaturalPathComparer.Instance).ToList();
        var occupied=peers.Select(x=>x.Effective.Track).ToHashSet();
        var previous=peers.LastOrDefault(x=>NaturalPathComparer.Instance.Compare(x.CurrentPath,current.CurrentPath)<0);
        var next=(previous?.Effective.Track??0)+1;
        while(next<=9999&&occupied.Contains(next))next++;
        if(next>9999)throw new InvalidOperationException("No sequential track number available below 10000; edit the numbering manually.");
        return next;
    }
}
