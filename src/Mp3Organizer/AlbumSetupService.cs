namespace Mp3Organizer;

public sealed class AlbumSetupService(ProgressRepository repository,string workspace,IReviewConsole console)
{
    public bool Ensure(IndexedMusic item)
    {
        var folder=Path.GetDirectoryName(item.CurrentPath)!;var members=Members(folder);
        var store=new ManualResolutionStore(workspace,item.SourceRoot);var data=store.Load();
        var scoped=data.Folders.FirstOrDefault(x=>x.Value.AlbumSetupConfirmed&&x.Value.ReviewFileIds?.Contains(item.Id)==true);
        if(scoped.Value!=null){Apply(scoped.Value.LastKnownPath,scoped.Value);return true;}
        var legacy=data.Folders.Where(x=>x.Value.AlbumSetupConfirmed&&x.Value.ReviewFileIds==null&&PathSafetyGuard.Within(item.CurrentPath,x.Value.LastKnownPath)).OrderByDescending(x=>x.Value.LastKnownPath.Length).FirstOrDefault();
        if(legacy.Value!=null)
        {
            var selected=Members(legacy.Value.LastKnownPath);
            console.Write($"CONFIRM REVIEW SCOPE — previous folder decision has no saved membership.\nScope: {legacy.Value.LastKnownPath}\nAlbum Artist: {legacy.Value.AlbumArtist} | Album: {legacy.Value.Album} | Year: {legacy.Value.Year}\nApply to exactly {selected.Count} currently indexed tracks including nested folders? New files will not inherit silently.\nEnter/YES = confirm | /q = stop:");
            var answer=console.Read()?.Trim();if(answer!= ""&&!string.Equals(answer,"YES",StringComparison.OrdinalIgnoreCase))return false;
            var upgraded=legacy.Value with{ReviewFileIds=selected.Select(x=>x.Id).ToArray()};data.Folders[legacy.Key]=upgraded;RemoveOverlap(data,legacy.Key,upgraded.ReviewFileIds!);store.Save(data);Apply(upgraded.LastKnownPath,upgraded);return true;
        }
        var existing=data.Folders.FirstOrDefault(x=>x.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase));
        if(existing.Value?.ReviewFileIds!=null)members=members.Where(x=>!existing.Value.ReviewFileIds.Contains(x.Id)).ToList();
        if(members.All(x=>!MetadataQualityEvaluator.Invalid(x.Effective.Album)&&!MetadataQualityEvaluator.Invalid(x.Effective.AlbumArtist)&&x.Effective.Year>=1000))return true;
        string Common(Func<AudioMetadata,string> get){var values=members.Select(x=>get(x.Effective)).Where(x=>!MetadataQualityEvaluator.Invalid(x)).GroupBy(MetadataNormalizer.Key).ToArray();return values.Length==1?values[0].First():"";}
        var albumArtist=Common(x=>x.AlbumArtist);var provisional=false;
        if(albumArtist==""){albumArtist=Common(x=>x.Artist);provisional=albumArtist!="";}
        var years=members.Select(x=>x.Effective.Year).Where(x=>x>=1000).Distinct().ToArray();
        var values=new[]{albumArtist,Common(x=>x.Album),years.Length==1?years[0].ToString():""};var names=new[]{"Album Artist","Album","Year"};
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
            console.Write($"ALBUM SUMMARY\nAlbum Artist: {values[0]}\nAlbum: {values[1]}\nYear: {values[2]}\nConfirm this album context for exactly {members.Count} indexed tracks in this scope INCLUDING nested folders, including Ready/Processed. New files are excluded until explicitly confirmed. Track Artist, Title and Track number stay unchanged.\nEnter/YES = confirm | M = edit | /q = stop\n\nFolder: {folder}\nConfirm album:");
            var confirm=console.Read()?.Trim();if(confirm==null||confirm.Equals("/q",StringComparison.OrdinalIgnoreCase))return false;if(confirm!=""&&!confirm.Equals("YES",StringComparison.OrdinalIgnoreCase))continue;
            var value=(existing.Value??new()) with{LastKnownPath=folder,ReviewFileIds=(existing.Value?.ReviewFileIds??[]).Concat(members.Select(x=>x.Id)).Distinct().ToArray(),AlbumSetupConfirmed=true,ProvisionalAlbumArtist=provisional,AlbumArtist=values[0],Album=values[1],Year=values[2]==""?0:uint.Parse(values[2]),Artist=null,Title=null,TrackNumber=null};
            var decisionKey=existing.Key??"folder:"+Guid.NewGuid().ToString("N");data.Folders[decisionKey]=value;RemoveOverlap(data,decisionKey,value.ReviewFileIds!);store.Save(data);Apply(folder,value);return true;
        }
    }
    private static void RemoveOverlap(ManualResolutions data,string key,string[] ids)
    {
        foreach(var other in data.Folders.Keys.Where(x=>x!=key).ToList())if(data.Folders[other].ReviewFileIds is {} prior)data.Folders[other]=data.Folders[other] with{ReviewFileIds=prior.Except(ids).ToArray()};
    }
    private List<IndexedMusic> Members(string folder)=>repository.All().Where(x=>PathSafetyGuard.Within(x.CurrentPath,folder)).ToList();
    private List<IndexedMusic> Selected(ManualOverride decision)=>repository.All().Where(x=>decision.ReviewFileIds?.Contains(x.Id)==true).ToList();
    public ManualOverride? Context(IndexedMusic item)=>new ManualResolutionStore(workspace,item.SourceRoot).Load().Folders.Values.FirstOrDefault(x=>x.AlbumSetupConfirmed&&x.ReviewFileIds?.Contains(item.Id)==true);
    public void Apply(string folder,ManualOverride decision)
    {
        foreach(var item in Selected(decision).Where(x=>x.Status is ProcessingStatus.NeedsReview or ProcessingStatus.Ready or ProcessingStatus.Processed))
        {
            var m=item.Effective;var fill=new ManualOverride{Album=decision.Album,AlbumArtist=decision.AlbumArtist,Year=decision.Year};
            var store=new ManualResolutionStore(workspace,item.SourceRoot);var data=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();
            if(decision.ReviewFileIds==null&&data.Files.TryGetValue(key,out var specific))
            {
                var corrected=specific with{Album=fill.Album??specific.Album,AlbumArtist=fill.AlbumArtist??specific.AlbumArtist,Year=fill.Year??specific.Year};
                if(corrected!=specific){data.Files[key]=corrected;store.Save(data);}
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
