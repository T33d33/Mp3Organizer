namespace Mp3Organizer;

public static class MetadataPolicy
{
    public const int Version=2;
    public static AudioMetadata Sanitize(AudioMetadata row)
    {
        var fields=row.SuppressedAutomaticFields.ToHashSet();
        return row with{Artist=fields.Contains("Artist")?"":row.Artist,AlbumArtist=fields.Contains("AlbumArtist")?"":row.AlbumArtist,
            Album=fields.Contains("Album")?"":row.Album,Title=fields.Contains("Title")?"":row.Title,Track=fields.Contains("TrackNumber")?0:row.Track,
            Disc=fields.Contains("DiscNumber")?0:row.Disc,Year=fields.Contains("Year")?0:row.Year,Date=fields.Contains("Year")?"":row.Date,
            TrackArtists=fields.Contains("Artist")?[]:row.TrackArtists};
    }
    public static int Migrate(ProgressRepository repository,string workspace,Action<string>? progress=null)
    {
        var all=repository.All();
        foreach(var root in all.Select(x=>x.SourceRoot).Distinct(StringComparer.OrdinalIgnoreCase))repository.RegisterFolders(root);
        var pending=all.Where(x=>x.Status is ProcessingStatus.Ready or ProcessingStatus.Processed&&x.Effective.Identification?.MetadataPolicyVersion!=Version).ToList();
        if(pending.Count==0)return 0;
        var overrides=all.Select(x=>x.SourceRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x,x=>new ManualResolutionStore(workspace,x).Load(),StringComparer.OrdinalIgnoreCase);
        var backup=repository.Backup();progress?.Invoke("Metadata policy migration backup: "+backup);var invalidated=0;
        foreach(var item in pending)
        {
            var e=item.Effective.Identification??new(){Original=TagMetadata.From(item.Basic)};
            var data=overrides[item.SourceRoot];var neighbors=all.Where(x=>Path.GetDirectoryName(x.CurrentPath)==Path.GetDirectoryName(item.CurrentPath)).Select(x=>x.Basic).ToList();
            var folder=ManualMetadataResolver.FindFolder(data,Path.GetDirectoryName(item.CurrentPath)!,neighbors,all.Select(x=>x.Basic).ToList()).Value;
            var specific=data.Files.FirstOrDefault(x=>x.Key.Equals("sha256:"+item.Basic.Sha256,StringComparison.OrdinalIgnoreCase)).Value;
            var effective=ManualMetadataResolver.Apply(ManualMetadataResolver.Apply(item.Effective,folder),specific);
            var sources=MetadataFieldMerger.ManualSources(new(){FieldSources=new()},folder,specific);
            var basic=Sanitize(item.Basic);
            // Prove filename evidence from the filename, not from tags that older versions may have written.
            var filename=new FilenameMetadataFallback().Resolve(basic with{Artist="",AlbumArtist="",Album="",Title="",Track=0,Disc=0,Year=0});
            string[] Values(AudioMetadata x)=>[x.Artist,x.AlbumArtist,x.Album,x.Title,x.Track.ToString(),x.Disc.ToString(),x.Year.ToString()];
            var actual=Values(effective);var embedded=Values(basic);var named=Values(filename);var bad=new List<string>();
            bool Same(string a,string b)=>MetadataNormalizer.Key(a)==MetadataNormalizer.Key(b);
            string Origin(string field)=>e.FieldSources.GetValueOrDefault(field)??(e.Source is MetadataSource.FolderFallback or MetadataSource.AcoustIdMusicBrainz?e.Source.ToString():"");
            var originalAlbum=e.Original.Album;
            var independentAlbum=!MetadataQualityEvaluator.Invalid(originalAlbum)&&Same(originalAlbum,effective.Album);
            var candidate=e.Candidates.FirstOrDefault(x=>x.RecordingId==e.RecordingId&&x.ReleaseId==e.ReleaseId&&x.Evidence.GetValueOrDefault("Fingerprint")>=.95*.55&&x.Evidence.GetValueOrDefault("Duration")>=.15&&x.Confidence-x.Evidence.GetValueOrDefault("Folder")-x.Evidence.GetValueOrDefault("Neighbors")>=.78);
            // Older versions did not always store candidates. Their explicit unique-recording/duration evidence is still usable when the embedded album supplied the release context.
            var verified=e.ConfidentRecording&&e.AcoustIdScore>=.95&&e.RecordingId!=""&&
                (independentAlbum&&(candidate!=null||e.Evidence.Contains("duration consistent",StringComparison.Ordinal))||candidate!=null&&candidate.Confidence-candidate.Evidence.GetValueOrDefault("Folder")-candidate.Evidence.GetValueOrDefault("Neighbors")>=.90);
            for(var i=0;i<MetadataFieldMerger.Fields.Length;i++)
            {
                var field=MetadataFieldMerger.Fields[i];if(sources.ContainsKey(field))continue;
                if(actual[i]==""||i>=4&&actual[i]=="0")continue;
                var oldSource=Origin(field);
                var explicitFolder=oldSource=="FolderFallback"&&field is "Artist" or "Album" or "AlbumArtist" or "Year";
                var originalValues=new[]{e.Original.Artist,e.Original.AlbumArtist,e.Original.Album,e.Original.Title,e.Original.Track.ToString(),e.Original.Disc.ToString(),e.Original.Year.ToString()};
                var wasAutomatic=oldSource is "AcoustIdMusicBrainz" or "CodexValidatedMusicBrainz" or "MusicBrainz" or "FolderFallback";
                if((!wasAutomatic||Same(originalValues[i],embedded[i]))&&Same(actual[i],embedded[i])&&(i>=4||!MetadataQualityEvaluator.Invalid(embedded[i]))) {sources[field]="Tags";continue;}
                if(field is "Artist" or "Title" or "TrackNumber"&&Same(actual[i],named[i])&&(i>=4||!MetadataQualityEvaluator.Invalid(named[i]))) {sources[field]="FilenameFallback";continue;}
                var candidateValues=candidate==null?null:new[]{candidate.Metadata.Artist,candidate.Metadata.AlbumArtist,candidate.Metadata.Album,candidate.Metadata.Title,candidate.Metadata.Track.ToString(),candidate.Metadata.Disc.ToString(),candidate.Metadata.Year.ToString()};
                if(!explicitFolder&&verified&&(candidateValues==null||Same(actual[i],candidateValues[i]))&&oldSource is "AcoustIdMusicBrainz" or "CodexValidatedMusicBrainz") {sources[field]=oldSource;continue;}
                if(field=="Year"&&independentAlbum&&e.YearEnriched&&e.YearConfidence>=.95&&e.YearSource=="MusicBrainz") {sources[field]="MusicBrainz";continue;}
                bad.Add(field);
            }
            if(e.Status=="Review"||ManualMetadataResolver.NeedsInput(effective))bad.Add("IncompleteMetadata");
            repository.AuditPolicy(item,bad.Count==0?"Retained independently valid metadata":"Reassessment: "+string.Join(",",bad));
            if(bad.Count>0)
            {
                var suppressed=item.Basic.SuppressedAutomaticFields.Concat(bad.Where(field=>{
                    var index=Array.IndexOf(MetadataFieldMerger.Fields,field);return index>=0&&Same(actual[index],embedded[index])&&Origin(field) is "FolderFallback" or "AcoustIdMusicBrainz" or "CodexValidatedMusicBrainz" or "MusicBrainz";
                })).Distinct().ToArray();
                // Keep the old result for diagnostics. Analysis always starts from original tags (with any known auto-written contaminated tags suppressed), then reapplies manual JSON.
                repository.Save(item with{Basic=item.Basic with{SuppressedAutomaticFields=suppressed},Effective=effective with{Identification=e with{MetadataPolicyVersion=Version,Status="Review",AutoRecognized=false,ReviewReasons=e.ReviewReasons.Append("MetadataPolicyReassessment").Distinct().ToArray(),ReviewReason="Folder-independent policy requires reassessment: "+string.Join(", ",bad)}},Status=item.Status==ProcessingStatus.Processed?ProcessingStatus.NeedsReview:ProcessingStatus.Discovered},"Policy 2 reassessment; old result retained in metadata_policy_audit");
                invalidated++;progress?.Invoke("Reassess: "+item.CurrentPath+" ("+string.Join(", ",bad)+")");
            }
            else repository.Save(item with{Effective=effective with{Identification=e with{MetadataPolicyVersion=Version,FieldSources=sources}}},"Policy 2: independent tags/manual/verified metadata retained");
        }
        return invalidated;
    }
}

public sealed class PolicyInputResolver(IMetadataResolver inner,IReadOnlyList<IndexedMusic> persisted):IMetadataResolver,IInventoryAwareMetadataResolver
{
    public void SetInventory(IReadOnlyList<AudioMetadata> rows){if(inner is IInventoryAwareMetadataResolver aware)aware.SetInventory(rows);}
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        var stored=persisted.FirstOrDefault(x=>x.CurrentPath.Equals(file.FullPath,StringComparison.OrdinalIgnoreCase)&&x.Basic.Sha256==file.Sha256);
        if(stored!=null)file=file with{SuppressedAutomaticFields=stored.Basic.SuppressedAutomaticFields};
        var result=await inner.ResolveAsync(MetadataPolicy.Sanitize(file),neighbors.Select(MetadataPolicy.Sanitize).ToList(),identifyAll,ct);
        return result with{Identification=(result.Identification??new()) with{MetadataPolicyVersion=MetadataPolicy.Version}};
    }
}



