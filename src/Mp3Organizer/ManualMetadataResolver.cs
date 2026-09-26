namespace Mp3Organizer;

public sealed class ManualMetadataResolver(IMetadataResolver inner,ManualResolutionStore store,bool forceAutomaticRefresh=false) : IMetadataResolver,IInventoryAwareMetadataResolver
{
    private IReadOnlyList<AudioMetadata> inventory=[];
    private ManualResolutions data=store.Load();
    private (long Length,DateTime Time) revision=Revision(store.FilePath);
    public void SetInventory(IReadOnlyList<AudioMetadata> rows)=>inventory=rows;
    private static (long,DateTime) Revision(string path)=>File.Exists(path)?(new FileInfo(path).Length,File.GetLastWriteTimeUtc(path)):(0,DateTime.MinValue);
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        if(file.Identification?.BeforeManualMetadata is { } before)
        {
            file=Restore(file,before) with{Identification=file.Identification with{FieldSources=file.Identification.BeforeManualFieldSources??new(),BeforeManualFieldSources=null,Source=file.Identification.BeforeManualSource,ManualOverrideHit=false,BeforeManualMetadata=null,Status="Resolved"}};
        }
        var current=Revision(store.FilePath);if(current!=revision){data=store.Load();revision=current;}
        var folder=FindFolder(data,Path.GetDirectoryName(file.FullPath)!,neighbors,inventory).Value;
        var specific=data.Files.FirstOrDefault(x=>x.Key.Equals("sha256:"+file.Sha256,StringComparison.OrdinalIgnoreCase)).Value;
        var merged=Apply(Apply(file,folder),specific);
        var hasManual=HasFields(folder)||HasFields(specific);
        var effective=hasManual&&!NeedsInput(merged)&&!identifyAll&&!forceAutomaticRefresh?file:await inner.ResolveAsync(file,neighbors,identifyAll,ct);
        var result=Apply(Apply(effective,folder),specific);
        var folderHit=HasFields(folder);var fileHit=HasFields(specific);
        if(!folderHit&&!fileHit)return effective;
        var evidence=effective.Identification??new IdentificationEvidence();
        return result with{Identification=evidence with{Original=file.Identification?.Original??TagMetadata.From(file),BeforeManualMetadata=TagMetadata.From(effective),BeforeManualFieldSources=evidence.FieldSources,FieldSources=MetadataFieldMerger.ManualSources(evidence,folder,specific),BeforeManualSource=evidence.Source,ManualOverrideHit=true,Source=fileHit?MetadataSource.FileManualOverride:MetadataSource.FolderManualOverride,
            Status=NeedsInput(result)?"Review":"ManualResolved",ReviewReason=NeedsInput(result)?"Manual defaults applied; artist, album, title, or track number still needs input.":"",
            ConfidentRecording=evidence.ConfidentRecording&&result.Artist==effective.Artist&&result.Title==effective.Title}};
    }
    private static AudioMetadata Restore(AudioMetadata file,TagMetadata tags)=>file with{Artist=tags.Artist,TrackArtists=tags.Artist==""?[]:[tags.Artist],AlbumArtist=tags.AlbumArtist,Album=tags.Album,Title=tags.Title,Track=tags.Track,Disc=tags.Disc,Year=tags.Year,Date=tags.Year==0?"":tags.Year.ToString()};
    public List<AudioMetadata> ReconcileTarget(string target,IReadOnlyList<AudioMetadata> rows)=>ReconcileTarget(target,rows,store);
    public static List<AudioMetadata> ReconcileTarget(string target,IReadOnlyList<AudioMetadata> rows,ManualResolutionStore manual)
    {
        var originals=rows.Select(row=>row with{FullPath=row.OriginalSourcePath==""?row.FullPath:row.OriginalSourcePath}).ToList();
        var resolver=new ManualMetadataResolver(new ExistingMetadataResolver(),manual);
        var resolved=IdentificationSession.ResolveAll(originals,resolver,false);
        return resolved.Select((row,i)=>row with{FullPath=rows[i].FullPath,OriginalSourcePath=originals[i].FullPath}).ToList();
    }
    private sealed class ExistingMetadataResolver:IMetadataResolver
    {
        public Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)=>Task.FromResult(file);
    }
    public static bool HasFields(ManualOverride? value)=>value!=null&&(value.Artist!=null||value.AlbumArtist!=null||value.Album!=null||value.Title!=null||value.TrackNumber!=null||value.DiscNumber!=null||value.Year!=null);
    public static AudioMetadata Apply(AudioMetadata row,ManualOverride? value)=>value==null?row:row with
    {
        Artist=value.Artist??row.Artist,TrackArtists=value.Artist==null?row.TrackArtists:value.Artist==""?[]:[value.Artist],AlbumArtist=value.AlbumArtist??row.AlbumArtist,
        Album=value.Album??row.Album,Title=value.Title??row.Title,Track=value.TrackNumber??row.Track,Disc=value.DiscNumber??row.Disc,Year=value.Year??row.Year,
        Date=value.Year.HasValue?value.Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):row.Date
    };
    public static bool NeedsInput(AudioMetadata row)=>MetadataQualityEvaluator.Invalid(row.Artist)||MetadataQualityEvaluator.Invalid(row.Album)||MetadataQualityEvaluator.Invalid(row.Title)||MetadataQualityEvaluator.FilenameTitle(row)||row.Track==0;
    public static KeyValuePair<string,ManualOverride> FindFolder(ManualResolutions data,string path,IReadOnlyList<AudioMetadata> members,IReadOnlyList<AudioMetadata>? inventory=null)
    {
        var exact=data.Folders.FirstOrDefault(x=>string.Equals(PathSafetyGuard.Canonical(x.Value.LastKnownPath),PathSafetyGuard.Canonical(path),StringComparison.OrdinalIgnoreCase));
        if(exact.Value!=null)return exact;
        bool Matches(ManualOverride item,IEnumerable<AudioMetadata> files)
        {
            var anchors=item.AnchorSha256?.ToHashSet(StringComparer.OrdinalIgnoreCase);if(anchors==null||anchors.Count==0)return false;
            var overlap=files.Select(x=>x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count(anchors.Contains);
            return overlap>=Math.Min(2,anchors.Count)&&overlap>=anchors.Count*.5;
        }
        var candidates=data.Folders.Where(x=>Matches(x.Value,members)).ToList();if(candidates.Count!=1)return default;
        if(inventory!=null&&inventory.Count>0&&inventory.GroupBy(x=>Path.GetDirectoryName(x.FullPath),StringComparer.OrdinalIgnoreCase).Count(g=>Matches(candidates[0].Value,g))!=1)return default;
        return candidates[0];
    }
}
