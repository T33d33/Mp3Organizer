namespace Mp3Organizer;

// Existing workflows can update an index created by scan, without creating an implicit second scan.
public static class ProgressResultRecorder
{
    public static void Record(string workspace,string source,IEnumerable<AudioMetadata> rows,bool copied=false)
    {
        var path=Path.Combine(workspace,"music-organizer.db");if(!File.Exists(path))return;
        PathSafetyGuard.Separate(source,workspace);var lockPath=Path.Combine(workspace,"music-organizer.lock");PathSafetyGuard.Writable(lockPath,source);
        using var gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var repository=new ProgressRepository(workspace);var indexed=repository.All().ToDictionary(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows)
        {
            if(!indexed.TryGetValue(row.FullPath,out var item)||row.Sha256!=item.Basic.Sha256)continue;
            if(item.Status is ProcessingStatus.Error or ProcessingStatus.Skipped||item.Status==ProcessingStatus.NeedsReview&&(copied||row.Identification?.Status!="ManualResolved"))continue;
            if(copied&&(row.Identification?.Status=="Review"||ManualMetadataResolver.NeedsInput(row)))continue;
            var status=row.Error!=""?ProcessingStatus.Error:copied?ProcessingStatus.Processed:
                row.Identification?.Status=="Review"||ManualMetadataResolver.NeedsInput(row)?ProcessingStatus.NeedsReview:ProcessingStatus.Ready;
            if(!copied&&item.Status==ProcessingStatus.Processed&&status==ProcessingStatus.Ready)status=ProcessingStatus.Processed;
            repository.Save(item with{Effective=row,Status=status,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=row.Error},copied?"Validated copy applied":"Resolution recorded");
        }
    }
}
