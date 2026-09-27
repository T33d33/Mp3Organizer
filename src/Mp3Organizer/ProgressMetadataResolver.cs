namespace Mp3Organizer;

// Reuse accepted analysis for planning, while the existing manual layer can still override it.
public sealed class ProgressMetadataResolver(IMetadataResolver inner,IReadOnlyList<IndexedMusic> rows):IMetadataResolver,IInventoryAwareMetadataResolver
{
    public void SetInventory(IReadOnlyList<AudioMetadata> inventory){if(inner is IInventoryAwareMetadataResolver aware)aware.SetInventory(inventory);}
    public Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        var item=rows.FirstOrDefault(x=>x.CurrentPath.Equals(file.FullPath,StringComparison.OrdinalIgnoreCase)&&x.Basic.Sha256==file.Sha256);
        if(!identifyAll&&item!=null&&item.Status is ProcessingStatus.Ready or ProcessingStatus.Processed&&item.Effective.Identification?.RecognitionMethod=="Codex")
            return Task.FromResult(ProgressRepository.At(item.Effective,file.FullPath));
        return inner.ResolveAsync(file,neighbors,identifyAll,ct);
    }
}
