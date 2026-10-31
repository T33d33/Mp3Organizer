namespace Mp3Organizer;

public sealed class AlbumSetupService(ProgressRepository repository,string workspace,IReviewConsole console)
{
    private string? lastAppliedScope;
    public bool Ensure(IndexedMusic item)
    {
        var folder=Path.GetDirectoryName(item.CurrentPath)!;
        var store=new ManualResolutionStore(workspace,item.SourceRoot);var data=store.Load();
        if(data.Files.TryGetValue("sha256:"+item.Basic.Sha256.ToLowerInvariant(),out var candidate)&&candidate.CandidateReviewFileIds?.Contains(item.Id)==true)return true;
        var scoped=data.Folders.FirstOrDefault(x=>x.Value.AlbumSetupConfirmed&&x.Value.ReviewFileIds?.Contains(item.Id)==true&&(!OutputLayout.KeepFolders(workspace)||x.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase)));
        if(scoped.Value!=null&&!scoped.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase))
        {
            var direct=repository.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)!.Equals(folder,StringComparison.OrdinalIgnoreCase)&&x.SourceRoot.Equals(item.SourceRoot,StringComparison.OrdinalIgnoreCase)).ToList();
            var proposal=SupportedAlbum(direct);
            if(proposal is {} album&&(MetadataNormalizer.Key(album.Album)!=MetadataNormalizer.Key(scoped.Value.Album)||MetadataNormalizer.Key(album.AlbumArtist)!=MetadataNormalizer.Key(scoped.Value.AlbumArtist)||album.Year!=scoped.Value.Year))
            {
                console.Write($"IDENTIFIED ALBUM — candidates agree within this folder.\nAlbum Artist: {album.AlbumArtist}\nAlbum: {album.Album}\nYear: {album.Year}\nReplace the inherited parent album context for these {direct.Count} files only, including already reviewed files. No child folders are included. Track Artist, Title and Track number remain unchanged; tracks without candidates still need individual review.\nEnter = confirm album | /q = stop without changes\n\n\nFolder: {folder}\nConfirm identified album:");
                var answer=console.Read()?.Trim();
                if(answer!=""&&!string.Equals(answer,"YES",StringComparison.OrdinalIgnoreCase))return false;
                var own=data.Folders.FirstOrDefault(x=>x.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase));
                var decision=new ManualOverride{LastKnownPath=folder,AlbumSetupConfirmed=true,ReviewFileIds=direct.Where(x=>!data.Files.Values.Any(v=>v.CandidateReviewFileIds?.Contains(x.Id)==true)).Select(x=>x.Id).ToArray(),Album=album.Album,AlbumArtist=album.AlbumArtist,Year=album.Year};
                var ownKey=own.Key??"folder:"+Guid.NewGuid().ToString("N");data.Folders[ownKey]=decision;RemoveOverlap(data,ownKey,decision.ReviewFileIds);store.Save(data);Apply(folder,decision);return true;
            }
        }
        if(scoped.Value!=null){var signature=JsonFormat.Serialize(scoped.Value);if(lastAppliedScope!=signature){Apply(scoped.Value.LastKnownPath,scoped.Value);lastAppliedScope=signature;}return true;}
        var legacy=data.Folders.Where(x=>x.Value.AlbumSetupConfirmed&&x.Value.ReviewFileIds==null&&PathSafetyGuard.Within(item.CurrentPath,x.Value.LastKnownPath)&&(!OutputLayout.KeepFolders(workspace)||x.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase))).OrderByDescending(x=>x.Value.LastKnownPath.Length).FirstOrDefault();
        if(legacy.Value!=null)
        {
            var selected=Members(legacy.Value.LastKnownPath);
            console.Write($"CONFIRM REVIEW SCOPE — previous folder decision has no saved membership.\nScope: {legacy.Value.LastKnownPath}\nAlbum Artist: {legacy.Value.AlbumArtist} | Album: {legacy.Value.Album} | Year: {legacy.Value.Year}\nApply to exactly {selected.Count} currently indexed tracks including nested folders? New files will not inherit silently.\nEnter/YES = confirm | /q = stop:");
            var answer=console.Read()?.Trim();if(answer!= ""&&!string.Equals(answer,"YES",StringComparison.OrdinalIgnoreCase))return false;
            var upgraded=legacy.Value with{ReviewFileIds=selected.Select(x=>x.Id).ToArray()};data.Folders[legacy.Key]=upgraded;RemoveOverlap(data,legacy.Key,upgraded.ReviewFileIds!);store.Save(data);Apply(upgraded.LastKnownPath,upgraded);return true;
        }
        var members=Members(folder);
        var existing=data.Folders.FirstOrDefault(x=>x.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase));
        if(existing.Value?.ReviewFileIds!=null)members=members.Where(x=>!existing.Value.ReviewFileIds.Contains(x.Id)).ToList();
        if(members.All(x=>!MetadataQualityEvaluator.InvalidAlbum(x.Effective.Album)&&!MetadataQualityEvaluator.Invalid(x.Effective.AlbumArtist)&&x.Effective.Year>=1000))return true;
        string Common(Func<AudioMetadata,string> get,bool album=false){var values=members.Select(x=>get(FilenameEncodingRepair.Apply(x.Effective))).Where(x=>!(album?MetadataQualityEvaluator.InvalidAlbum(x):MetadataQualityEvaluator.Invalid(x))).GroupBy(MetadataNormalizer.Key).ToArray();return values.Length==1?values[0].First():"";}
        var albumArtist=Common(x=>x.AlbumArtist);var provisional=false;
        if(albumArtist==""){albumArtist=Common(x=>x.Artist);provisional=albumArtist!="";}
        var years=members.Select(x=>x.Effective.Year).Where(x=>x>=1000).Distinct().ToArray();
        var values=new[]{albumArtist,Common(x=>x.Album,true),years.Length==1?years[0].ToString():""};var names=new[]{"Album Artist","Album","Year"};
        while(true)
        {
            console.Write("ALBUM METADATA — setup for this source folder. Suggestions come from track metadata, never the folder name.\nEnter = keep suggestion | S = leave unknown | /back = previous | /q = stop.");
            for(var i=0;i<3;)
            {
                console.Write("\n\nFolder: "+folder+"\n"+names[i]+" ["+(values[i]==""?"<missing>":values[i])+"]:");var input=console.Read()?.Trim();
                if(input==null||input.Equals("/q",StringComparison.OrdinalIgnoreCase))return false;
                if(input.Equals("/back",StringComparison.OrdinalIgnoreCase)){i=Math.Max(0,i-1);continue;}
                if(input.Equals("M",StringComparison.OrdinalIgnoreCase)){console.Write("Type the replacement at the field prompt.");continue;}
                if(input.StartsWith('/')){console.Write("Use /back or /q.");continue;}
                var answer=input.Equals("S",StringComparison.OrdinalIgnoreCase)?"":input==""?values[i]:input;
                if(i==2&&answer!=""&&(!uint.TryParse(answer,out var y)||y<1000||y>9999)){console.Write("Enter a four-digit year or S.");continue;}
                if(i==0&&input!="")provisional=false;
                values[i]=i==0&&ReviewService.CompilationAlbumArtist(answer)?"Various Artists":answer;i++;
            }
            console.Write($"ALBUM SUMMARY\nAlbum Artist: {values[0]}\nAlbum: {values[1]}\nYear: {values[2]}\nConfirm this album context for exactly {members.Count} indexed tracks in this scope {(OutputLayout.KeepFolders(workspace)?"EXCLUDING child folders":"INCLUDING nested folders")}, including Ready/Processed. New files are excluded until explicitly confirmed. Track Artist, Title and Track number stay unchanged.\nEnter/YES = confirm | M = edit | /q = stop\n\nFolder: {folder}\nConfirm album:");
            var confirm=console.Read()?.Trim();if(confirm==null||confirm.Equals("/q",StringComparison.OrdinalIgnoreCase))return false;if(confirm!=""&&!confirm.Equals("YES",StringComparison.OrdinalIgnoreCase))continue;
            var value=(existing.Value??new()) with{LastKnownPath=folder,ReviewFileIds=(existing.Value?.ReviewFileIds??[]).Concat(members.Select(x=>x.Id)).Distinct().ToArray(),AlbumSetupConfirmed=true,ProvisionalAlbumArtist=provisional,AlbumArtist=values[0],Album=values[1],Year=values[2]==""?0:uint.Parse(values[2]),Artist=null,Title=null,TrackNumber=null};
            var decisionKey=existing.Key??"folder:"+Guid.NewGuid().ToString("N");data.Folders[decisionKey]=value;RemoveOverlap(data,decisionKey,value.ReviewFileIds!);store.Save(data);Apply(folder,value);return true;
        }
    }
    private static void RemoveOverlap(ManualResolutions data,string key,string[] ids)
    {
        foreach(var other in data.Folders.Keys.Where(x=>x!=key).ToList())if(data.Folders[other].ReviewFileIds is {} prior)data.Folders[other]=data.Folders[other] with{ReviewFileIds=prior.Except(ids).ToArray()};
    }
    public static TagMetadata? SupportedAlbum(IReadOnlyList<IndexedMusic> members)
    {
        if(members.Count<2)return null;
        var evidence=new List<TagMetadata>();
        foreach(var member in members)
        {
            var candidates=member.Effective.Identification?.Candidates??[];
            var albums=candidates.Select(x=>x.Metadata).Where(x=>!MetadataQualityEvaluator.InvalidAlbum(x.Album)&&!MetadataQualityEvaluator.Invalid(x.AlbumArtist)&&x.Year>=1000)
                .GroupBy(x=>(MetadataNormalizer.Key(x.Album),MetadataNormalizer.Key(x.AlbumArtist),x.Year)).ToList();
            if(albums.Count>1)return null;
            if(albums.Count==1)evidence.Add(albums[0].First());
        }
        if(evidence.Count<2||evidence.Count*5<members.Count*4)return null;
        var first=evidence[0];
        if(evidence.Any(x=>MetadataNormalizer.Key(x.Album)!=MetadataNormalizer.Key(first.Album)||MetadataNormalizer.Key(x.AlbumArtist)!=MetadataNormalizer.Key(first.AlbumArtist)||x.Year!=first.Year))return null;
        // Missing-candidate tracks cannot establish album identity by their folder name.
        // For a single-artist proposal they must at least agree on the track artist.
        if(!ReviewService.CompilationAlbumArtist(first.AlbumArtist)&&members.Any(x=>MetadataNormalizer.Key(x.Effective.Artist)!=MetadataNormalizer.Key(first.AlbumArtist)))return null;
        return first;
    }
    private List<IndexedMusic> Members(string folder)=>repository.All().Where(x=>OutputLayout.KeepFolders(workspace)?string.Equals(Path.GetDirectoryName(x.CurrentPath),folder,StringComparison.OrdinalIgnoreCase):PathSafetyGuard.Within(x.CurrentPath,folder)).ToList();
    private List<IndexedMusic> Selected(ManualOverride decision)=>(decision.ReviewFileIds??[]).Select(repository.Find).Where(x=>x!=null).Select(x=>x!).ToList();
    public ManualOverride? Context(IndexedMusic item)=>new ManualResolutionStore(workspace,item.SourceRoot).Load().Folders.Values.FirstOrDefault(x=>x.AlbumSetupConfirmed&&x.ReviewFileIds?.Contains(item.Id)==true&&(!OutputLayout.KeepFolders(workspace)||x.LastKnownPath.Equals(Path.GetDirectoryName(item.CurrentPath),StringComparison.OrdinalIgnoreCase)));
    public void Apply(string folder,ManualOverride decision)
    {
        foreach(var item in Selected(decision).Where(x=>x.Status is ProcessingStatus.NeedsReview or ProcessingStatus.Ready or ProcessingStatus.Processed))
        {
            var m=item.Effective;var fill=new ManualOverride{Album=decision.Album,AlbumArtist=decision.AlbumArtist,Year=decision.Year};
            if(decision.ReviewFileIds==null)
            {
            var store=new ManualResolutionStore(workspace,item.SourceRoot);var data=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();
            if(data.Files.TryGetValue(key,out var specific))
            {
                var corrected=specific with{Album=fill.Album??specific.Album,AlbumArtist=fill.AlbumArtist??specific.AlbumArtist,Year=fill.Year??specific.Year};
                if(corrected!=specific){data.Files[key]=corrected;store.Save(data);}
            }
            }
            var updated=ManualMetadataResolver.Apply(m,fill);var e=m.Identification??new();updated=updated with{Identification=e with{FieldSources=MetadataFieldMerger.ManualSources(e,fill,null)}};
            if(updated.Album!=m.Album||updated.AlbumArtist!=m.AlbumArtist||updated.Year!=m.Year)repository.Save(item with{Effective=updated},"Confirmed album setup filled shared fields only");
        }
    }
    public void ReconcileArtist(IndexedMusic accepted)
    {
        var folder=Path.GetDirectoryName(accepted.CurrentPath)!;var store=new ManualResolutionStore(workspace,accepted.SourceRoot);var data=store.Load();var found=data.Folders.FirstOrDefault(x=>x.Value.ReviewFileIds?.Contains(accepted.Id)==true);
        var old=found.Value;if(old?.ProvisionalAlbumArtist!=true||ReviewService.CompilationAlbumArtist(old.AlbumArtist??"")||MetadataQualityEvaluator.Invalid(accepted.Effective.Artist)||MetadataNormalizer.Key(accepted.Effective.Artist)==MetadataNormalizer.Key(old.AlbumArtist))return;
        console.Write("ALBUM RECONCILIATION: this confirmed track artist differs from the suggested Album Artist.\nSet Album Artist to Various Artists? Album, Year and individual track artists remain unchanged.\n\nFolder: "+folder+"\nYES = Various Artists | Enter = keep:");
        if(!string.Equals(console.Read()?.Trim(),"YES",StringComparison.OrdinalIgnoreCase))return;
        data.Folders[found.Key]=old with{AlbumArtist="Various Artists",ProvisionalAlbumArtist=false};store.Save(data);
        foreach(var item in Selected(old).Where(x=>MetadataNormalizer.Key(x.Effective.AlbumArtist)==MetadataNormalizer.Key(old.AlbumArtist)))
        {
            var manual=new ManualResolutionStore(workspace,item.SourceRoot);var all=manual.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();
            if(all.Files.TryGetValue(key,out var specific)&&specific.AlbumArtist==old.AlbumArtist){all.Files[key]=specific with{AlbumArtist="Various Artists"};manual.Save(all);}
            repository.Save(item with{Effective=item.Effective with{AlbumArtist="Various Artists"}},"Confirmed Various Artists reconciliation");
        }
    }
}
