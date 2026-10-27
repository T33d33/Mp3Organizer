namespace Mp3Organizer;

public sealed class FolderAlbumEditor(ProgressRepository repository,string workspace,IReviewConsole console)
{
    public (bool Stop,bool Saved) Edit(IndexedMusic current)
    {
        var folder=Path.GetDirectoryName(current.CurrentPath)!;
        var members=repository.All().Where(x=>x.SourceRoot.Equals(current.SourceRoot,StringComparison.OrdinalIgnoreCase)&&Path.GetDirectoryName(x.CurrentPath)!.Equals(folder,StringComparison.OrdinalIgnoreCase)&&x.Status is ProcessingStatus.NeedsReview or ProcessingStatus.Ready or ProcessingStatus.Processed).ToList();
        var context=new AlbumSetupService(repository,workspace,console).Context(current);
        var local=context?.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase)==true;
        string Common(Func<IndexedMusic,string> get,bool album=false)
        {
            var values=members.Select(get).Where(x=>!(album?MetadataQualityEvaluator.InvalidAlbum(x):MetadataQualityEvaluator.Invalid(x))).GroupBy(MetadataNormalizer.Key).ToList();return values.Count==1?values[0].First():"";
        }
        var artist=Common(x=>x.Basic.AlbumArtist);if(artist=="")artist=Common(x=>x.Effective.Artist);
        var years=members.Select(x=>x.Basic.Year).Where(x=>x>=1000&&x<=9999).Distinct().ToArray();
        var values=new[]{local?context!.AlbumArtist??"":artist,local?context!.Album??"":Common(x=>x.Basic.Album,true),local?(context!.Year is >0?context.Year.ToString()!:""):years.Length==1?years[0].ToString():""};
        var names=new[]{"Album Artist","Album","Year (optional)"};
        console.Write("EDIT ALBUM FOR THIS FOLDER — Enter keeps the suggestion; text replaces it. /clear = clear | /back = previous | /cancel = return without saving | /q = stop review. No changes until confirmation.");
        for(var field=0;;)
        {
            if(field<3)
            {
                Prompt($"{names[field]} [{(values[field]==""?"<unknown>":values[field])}]:");var input=console.Read()?.Trim();
                if(input==null||input.Equals("/q",StringComparison.OrdinalIgnoreCase))return(true,false);
                if(input.Equals("/cancel",StringComparison.OrdinalIgnoreCase))return(false,false);
                if(input.Equals("/back",StringComparison.OrdinalIgnoreCase)){field=Math.Max(0,field-1);continue;}
                var clear=input.Equals("/clear",StringComparison.OrdinalIgnoreCase);
                if(input.StartsWith('/')&&!clear){console.Write("Use /back, /clear, /cancel or /q.");continue;}
                var value=clear?"":input==""?values[field]:input;
                if(field==2&&value!=""&&(!uint.TryParse(value,out var year)||year<1000||year>9999)){console.Write("Enter a four-digit year or /clear.");continue;}
                if(field<2&&(value.Length>500||value.Any(char.IsControl))){console.Write("Enter at most 500 characters without control characters.");continue;}
                values[field]=field==0&&ReviewService.CompilationAlbumArtist(value)?"Various Artists":value;field++;continue;
            }
            console.Write($"ALBUM SUMMARY\nAlbum Artist: {values[0]}\nAlbum: {values[1]}\nYear: {(values[2]==""?"<unknown>":values[2])}\nApply to {members.Count} indexed tracks DIRECTLY in this folder, including Ready/Processed. No child or sibling folders. Replaces earlier album choices, including candidate album fields. Track Artist, Title and Track number stay unchanged. NeedsReview remains NeedsReview. Organized copies are corrected on the next run.\nEnter/YES = save album | M = edit again | /back = year | /cancel = discard | /q = stop");
            Prompt("Confirm album:");var confirm=console.Read()?.Trim();
            if(confirm==null||confirm.Equals("/q",StringComparison.OrdinalIgnoreCase))return(true,false);
            if(confirm.Equals("/cancel",StringComparison.OrdinalIgnoreCase))return(false,false);
            if(confirm.Equals("M",StringComparison.OrdinalIgnoreCase)){field=0;continue;}
            if(confirm.Equals("/back",StringComparison.OrdinalIgnoreCase)){field=2;continue;}
            if(confirm!=""&&!confirm.Equals("YES",StringComparison.OrdinalIgnoreCase)){console.Write("Use Enter/YES to save, M to edit, /cancel or /q.");continue;}
            var store=new ManualResolutionStore(workspace,current.SourceRoot);var data=store.Load();var ids=members.Select(x=>x.Id).ToHashSet();
            foreach(var key in data.Folders.Keys.ToArray())
            {
                var old=data.Folders[key];
                if(old.ReviewFileIds!=null)data.Folders[key]=old with{ReviewFileIds=old.ReviewFileIds.Where(x=>!ids.Contains(x)).ToArray()};
            }
            // Reuse an emptied local scope; retain disjoint memberships from older recursive scopes.
            var existing=data.Folders.FirstOrDefault(x=>x.Value.LastKnownPath.Equals(folder,StringComparison.OrdinalIgnoreCase)&&x.Value.ReviewFileIds?.Length==0);
            var decision=new ManualOverride{LastKnownPath=folder,AlbumSetupConfirmed=true,ReviewFileIds=ids.ToArray(),AlbumArtist=values[0],Album=values[1],Year=values[2]==""?0:uint.Parse(values[2])};
            data.Folders[existing.Key??"folder:"+Guid.NewGuid().ToString("N")]=decision;
            foreach(var key in data.Files.Keys.ToArray())
            {
                var value=data.Files[key];if(value.AcceptedCandidateMetadata==null)continue;
                var accepted=new Dictionary<string,TagMetadata>(value.AcceptedCandidateMetadata);
                foreach(var id in accepted.Keys.Where(ids.Contains).ToArray())accepted[id]=accepted[id] with{Album=decision.Album!,AlbumArtist=decision.AlbumArtist!,Year=decision.Year!.Value};
                data.Files[key]=value with{AcceptedCandidateMetadata=accepted};
            }
            store.Save(data);new AlbumSetupService(repository,workspace,console).Apply(folder,decision);
            console.Write($"Saved album fields for {members.Count} tracks. Continue track review; run processing afterward to update organized copies.");return(false,true);
        }
        void Prompt(string prompt)=>console.Write("\n\nFolder: "+folder+"\n"+prompt);
    }
}
