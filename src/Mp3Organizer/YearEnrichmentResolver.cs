namespace Mp3Organizer;

public sealed class YearEnrichmentResolver(IMetadataResolver inner,IAlbumYearClient albums,bool online,Action<string>? progress=null):IMetadataResolver,IInventoryAwareMetadataResolver
{
    public void SetInventory(IReadOnlyList<AudioMetadata> rows){if(inner is IInventoryAwareMetadataResolver aware)aware.SetInventory(rows);}
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        var row=await inner.ResolveAsync(file,neighbors,identifyAll,ct);var e=row.Identification??new(){Original=TagMetadata.From(file)};
        // An existing nonzero tag/manual year is authoritative, including a known reissue year.
        if(file.Year>0||e.FieldSources.GetValueOrDefault("Year","").Contains("Manual",StringComparison.Ordinal))return row;
        // A verified release-group first date can already have enriched this result.
        if(row.Year>0&&e.YearSource=="MusicBrainz")return row;
        // Do not present a later edition date as an original album year.
        if(row.Year>0&&e.FieldSources.GetValueOrDefault("Year")=="AcoustIdMusicBrainz")row=row with{Year=0,Date=""};
        if(row.Year>0)return row;
        var artist=MetadataQualityEvaluator.Invalid(row.AlbumArtist)?row.Artist:row.AlbumArtist;
        if(MetadataQualityEvaluator.Invalid(artist)||MetadataQualityEvaluator.Invalid(row.Album))return Review(row,e,"UnknownYear","Album identity incomplete; no reliable year lookup.");
        progress?.Invoke("MusicBrainz: original album year or cache");
        AlbumYearLookup lookup;
        try{lookup=await albums.LookupAsync(artist,row.Album,online,ct);}
        catch(Exception ex) when(ex is IOException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException||ex is OperationCanceledException&&!ct.IsCancellationRequested)
        {return Review(row,e,"UnknownYear","Album year lookup unavailable.");}
        var matches=lookup.Candidates.Where(x=>MetadataNormalizer.Key(x.Artist)==MetadataNormalizer.Key(artist)&&MetadataNormalizer.Key(x.Album)==MetadataNormalizer.Key(row.Album)).GroupBy(x=>x.GroupId).Select(x=>x.First()).ToList();
        var variants=RecognitionScorer.Variants(row.Album+" "+Path.GetFileName(Path.GetDirectoryName(row.FullPath)));
        var safe=matches.Where(x=>x.OriginalYear>=1000&&x.OriginalYear<=DateTime.UtcNow.Year+1&&x.Types.Contains("Album")&&
            !x.Types.Any(t=>t is "Live" or "Remix")&&RecognitionScorer.Variants(x.Disambiguation).Length==0).ToList();
        // A remaster label may share the original group, but a distinct explicitly named edition is not stripped to force a match.
        if(lookup.Error==""&&lookup.Complete&&matches.Count==1&&safe.Count==1&&!variants.Any(x=>x is "LIVE" or "ACOUSTIC" or "REMIX"))
        {
            var match=safe[0];var sources=new Dictionary<string,string>(e.FieldSources){["Year"]="MusicBrainz"};
            var reasons=e.ReviewReasons.Where(x=>x!="UnknownYear").ToArray();
            var complete=e.Status!="Review"&& !ManualMetadataResolver.NeedsInput(row);
            return row with{Year=match.OriginalYear,Date=match.OriginalYear.ToString(),Identification=e with{FieldSources=sources,YearSource="MusicBrainz",YearConfidence=.95,YearEnriched=true,AlbumGroupId=match.GroupId,ReviewReasons=reasons,Status=complete?e.Status:"Review",Evidence=e.Evidence+"; original album year from MusicBrainz release-group "+match.GroupId+" (exact artist/album, unique complete result)",IdentificationCacheHit=e.IdentificationCacheHit||lookup.CacheHit}};
        }
        var candidates=matches.Select(x=>new RecognitionCandidate(TagMetadata.From(row) with{Year=x.OriginalYear},e.RecordingId,"",0,new(){["ExactArtistAlbum"]=1},"MusicBrainz release-group "+x.GroupId)).ToList();
        e=e with{Candidates=e.Candidates.Concat(candidates).Take(9).ToList()};
        return Review(row,e,matches.Count>1?"AmbiguousAlbum":"UnknownYear",lookup.Error!=""?lookup.Error:"Original year not established uniquely; no year guessed.");
    }
    private static AudioMetadata Review(AudioMetadata row,IdentificationEvidence e,string reason,string detail)=>row with{Identification=e with{Status="Review",ReviewReasons=e.ReviewReasons.Concat(new[]{reason,"UnknownYear"}).Distinct().ToArray(),ReviewReason=string.Join("; ",new[]{e.ReviewReason,reason,"UnknownYear",detail}.Where(x=>x!=""))}};
}
