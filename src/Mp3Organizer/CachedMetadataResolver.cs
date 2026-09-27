namespace Mp3Organizer;

// Policy-versioned result cache. Context changes can refresh weak evidence, never assign folder metadata.
public sealed class CachedMetadataResolver(IMetadataResolver inner, IdentificationCache cache, bool online) : IMetadataResolver
{
    private readonly Dictionary<IReadOnlyList<AudioMetadata>,string> contexts = new(ReferenceEqualityComparer.Instance);
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        cache.Observe(file);
        if (!contexts.TryGetValue(neighbors,out var context))
        {
            context = string.Join(";",neighbors.GroupBy(x=>(MetadataNormalizer.Key(x.Album),x.Year)).OrderBy(x=>x.Key.Item1,StringComparer.Ordinal).ThenBy(x=>x.Key.Year).Select(x=>$"{x.Key}:{x.Count()}"));
            contexts.Add(neighbors,context);
        }
        var key = "resolution-v9-folder-independent:" + file.Sha256 + ":" + JsonFormat.Serialize(TagMetadata.From(file)) + ":" + context;
        var previousStatus=cache.Status(key);
        var saved = !identifyAll&&!cache.RefreshIdentification&&!cache.RebuildFingerprints ? cache.Get(key,!online) : null;
        if(saved != null)
        {
            var result=JsonFormat.Read<AudioMetadata>(saved);
            if(result.Identification?.LookupStatus is "NoMatch" or "TransientFailure" or "Ambiguous" && result.Identification.ConfidentRecording!=true)
                result=new FilenameMetadataFallback().Resolve(file) with{Identification=result.Identification};
            return result with{FullPath=file.FullPath,FileName=file.FileName,Identification=result.Identification! with{IdentificationCacheHit=true,FingerprintCacheHit=cache.Get("chromaprint-v1-length120:"+file.Sha256)!=null}};
        }
        using var refresh=cache.RefreshScope(previousStatus is "NoMatch" or "Ambiguous" or "TransientFailure");
        var resolved=await inner.ResolveAsync(file,neighbors,identifyAll||(cache.RefreshIdentification&&previousStatus!=null),ct);
        var status=resolved.Identification?.LookupStatus??"NotNeeded";
        if(status is "Success" or "NoMatch" or "Ambiguous" or "TransientFailure")cache.Put(key,JsonFormat.Serialize(resolved),status=="Success"?TimeSpan.MaxValue:status=="TransientFailure"?TimeSpan.FromMinutes(5):cache.NegativeRetryAge,status);
        return resolved;
    }
}

