namespace Mp3Organizer;

// Selection, editing and confirmation are separate states; no implicit entry into editing.
public sealed class CandidateReviewService(ProgressRepository repository,string workspace,IReviewConsole console,Action<string>? play=null,Mp3TagWriteService? tags=null,bool albumFirst=false)
{
    private readonly Dictionary<string,TagMetadata> drafts=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,uint> suggestedNumbers=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reviewed=new(StringComparer.OrdinalIgnoreCase);
    public int ManuallyReviewedCount=>reviewed.Count;
    public int AutomaticallyResolvedCount=>0;
    public int SkippedCount {get;private set;}
    public int Run()
    {
        var queue=repository.All().Where(x=>x.Status==ProcessingStatus.NeedsReview).OrderBy(x=>x.CurrentPath,NaturalPathComparer.Instance).ToList();
        for(var position=0;position<queue.Count;position++)
        {
            var item=repository.All().Single(x=>x.Id==queue[position].Id);
            if(albumFirst&&!new AlbumSetupService(repository,workspace,console).Ensure(item))return ManuallyReviewedCount;
            item=repository.All().Single(x=>x.Id==item.Id);
            var row=item.Effective;var candidates=(row.Identification?.Candidates??[]).Take(9).ToList();
            var recommended=candidates.Count==0?0:candidates.Select((x,i)=>(x.Confidence,Index:i+1)).OrderByDescending(x=>x.Confidence).First().Index;
            while(true)
            {
                if(albumFirst&&item.Effective.Track==0)
                {
                    var next=AlbumTrackSequence.Suggest(item,repository.All(),new AlbumSetupService(repository,workspace,console).Context(item)?.ReviewFileIds);
                    if(drafts.TryGetValue(item.CurrentPath,out var oldDraft)&&suggestedNumbers.TryGetValue(item.CurrentPath,out var oldSuggestion)&&oldDraft.Track==oldSuggestion)drafts[item.CurrentPath]=oldDraft with{Track=next};
                    suggestedNumbers[item.CurrentPath]=next;row=row with{Track=next};
                    console.Write($"Track number suggestion: {next} (sequential fallback; saved only when you accept).");
                }
                if(drafts.TryGetValue(item.CurrentPath,out var draft))row=ManualMetadataResolver.Apply(row,new(){Artist=draft.Artist,AlbumArtist=draft.AlbumArtist,Album=draft.Album,Title=draft.Title,TrackNumber=draft.Track,Year=draft.Year});
                console.Write("Album context:\nAlbum Artist: "+row.AlbumArtist+"\nAlbum: "+row.Album+"\nYear: "+row.Year+"\nTrack metadata:\nArtist: "+row.Artist+"\nTitle: "+row.Title+"\nTrack: "+row.Track);
                for(var i=0;i<candidates.Count;i++)console.Write($"\n[{i+1}] Candidate\n{Display(candidates[i].Metadata)}");
                console.Write((recommended>0?$"Recommended candidate: {recommended} (highest evidence score; not verified)\n[Enter] Save recommended candidate and continue | [1-{candidates.Count}] Select candidate":"No online candidates. [Enter] Save current metadata and continue")+"\n[M] Manual editing | [P] Play | [D] Defer | [/q] Stop");
                Prompt(item.CurrentPath,"Action [Enter = save and next; M = edit fields] | [P] Play | [D] Defer | [/back] Previous file | [/q] Stop:");var command=console.Read()?.Trim();if(command==null||command.Equals("/q",StringComparison.OrdinalIgnoreCase)||command.Equals("q",StringComparison.OrdinalIgnoreCase))return ManuallyReviewedCount;
                if(command.Equals("/back",StringComparison.OrdinalIgnoreCase))
                {
                    if(position==0){console.Write("This is the first file in this review session.");continue;}
                    position-=2;break;
                }
                if(command.Equals("D",StringComparison.OrdinalIgnoreCase))break;
                if(command.Equals("P",StringComparison.OrdinalIgnoreCase))
                {
                    try{PathSafetyGuard.NoLinks(item.CurrentPath);if(play!=null)play(item.CurrentPath);else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.CurrentPath){UseShellExecute=true});}
                    catch(Exception e) when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){console.Write("Playback failed: "+e.Message);}continue;
                }
                TagMetadata selected;var manual=command.Equals("M",StringComparison.OrdinalIgnoreCase);
                if(manual)
                {
                    var edited=Edit(item.CurrentPath,TagMetadata.From(row));if(edited.Stop)return ManuallyReviewedCount;if(edited.Value==null)continue;selected=edited.Value;
                }
                else
                {
                    if(command=="")command=recommended.ToString();
                    if(!int.TryParse(command,out var number)||number<0||number>candidates.Count||number==0&&candidates.Count>0){console.Write("This is an action menu, not a metadata field. Press Enter to save, or M to edit Artist/Title. Other actions: candidate number, P, D, /q.");continue;}
                    if(number==0)selected=TagMetadata.From(row);
                    else
                    {
                        var c=candidates[number-1].Metadata;
                        string Keep(string higher,string lower)=>MetadataQualityEvaluator.Invalid(higher)?lower:higher;
                        selected=new(Keep(c.Artist,row.Artist),Keep(c.AlbumArtist,row.AlbumArtist),Keep(c.Album,row.Album),Keep(c.Title,row.Title),c.Track>0?c.Track:row.Track,c.Disc>0?c.Disc:row.Disc,c.Year>0?c.Year:row.Year);
                    }
                }
                if(albumFirst&&new AlbumSetupService(repository,workspace,console).Context(item) is {} context)
                    selected=selected with{Album=context.Album??selected.Album,AlbumArtist=context.AlbumArtist??selected.AlbumArtist,Year=context.Year??selected.Year};
                drafts[item.CurrentPath]=selected;
                var albumDecision=false;
                if(manual||tags!=null)
                {
                console.Write("Proposed result:\n"+Display(selected)+"\nScope: this track only. Artist, Title and Track number never propagate. Album/Album Artist are stored for this track; Year is not an album-wide decision here.\n"+(tags!=null?"YES also authorizes source MP3 tag writing; audio is unchanged.\n":"")+"Enter / YES = save this track | /cancel = selection (keep draft) | ALBUM = save and apply this Year to matching missing-year tracks in this source folder (same artist/album identity only) | unknown input = return to selection (keep draft)");
                Prompt(item.CurrentPath,"Confirm:");var confirm=console.Read()?.Trim();if(confirm==null||confirm.Equals("/q",StringComparison.OrdinalIgnoreCase))return ManuallyReviewedCount;albumDecision=confirm.Equals("ALBUM",StringComparison.OrdinalIgnoreCase);if(confirm!=""&&!confirm.Equals("YES",StringComparison.OrdinalIgnoreCase)&&!albumDecision)continue;
                if(tags!=null&&confirm==""){console.Write("Source tag writing requires YES. Draft retained; no changes saved.");continue;}
                }
                var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(item.CurrentPath);if(actual.Error!=""||actual.Sha256!=item.Basic.Sha256){console.Write("File changed; scan before reviewing.");break;}
                var store=new ManualResolutionStore(workspace,item.SourceRoot);var data=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();
                // Empty strings explicitly clear text; zero represents a deliberate unknown number.
                var value=new ManualOverride{LastKnownPath=item.CurrentPath,ConfirmedLooseTrack=string.IsNullOrWhiteSpace(selected.Album),Artist=selected.Artist,AlbumArtist=selected.AlbumArtist,Album=selected.Album,Title=selected.Title,TrackNumber=selected.Track,Year=selected.Year,YearTrackOnly=!albumDecision,DiscNumber=selected.Disc==0?null:selected.Disc};
                if(albumFirst&&new AlbumSetupService(repository,workspace,console).Context(item)!=null)
                {
                    // Album context is keyed by file IDs, not content hash: do not leak it to an identical file outside the scope.
                    data.Files.TryGetValue(key,out var prior);
                    value=value with{Album=prior?.Album,AlbumArtist=prior?.AlbumArtist,Year=prior?.Year};
                }
                data.Files[key]=value;store.Save(data);
                var updated=ManualMetadataResolver.Apply(row,value);if(albumFirst&&new AlbumSetupService(repository,workspace,console).Context(item) is {} savedContext)updated=updated with{Album=savedContext.Album??updated.Album,AlbumArtist=savedContext.AlbumArtist??updated.AlbumArtist,Year=savedContext.Year??updated.Year};var evidence=row.Identification??new();var needs=ManualMetadataResolver.NeedsInput(updated,selected.Title);
                updated=updated with{Identification=evidence with{MetadataPolicyVersion=2,Status=needs?"Review":"ManualResolved",ManualOverrideHit=true,Source=MetadataSource.FileManualOverride,RecognitionMethod="Manual",ReviewReasons=needs?["ManualMetadataIncomplete"]:[],ReviewReason=needs?"Manually cleared or incomplete required fields.":"",FieldSources=MetadataFieldMerger.ManualSources(evidence,null,value)}};
                var accepted=item with{Effective=updated,Status=needs?ProcessingStatus.NeedsReview:ProcessingStatus.Ready,ErrorMessage="",LastProcessedUtc=DateTime.UtcNow.ToString("O")};
                if(tags!=null&&!needs)tags.WriteAndSave(accepted,true);else repository.Save(accepted,"Explicit review confirmation; track metadata saved");
                if(albumDecision)new AlbumYearCorrectionService(repository,workspace).Propagate();
                if(albumFirst)new AlbumSetupService(repository,workspace,console).ReconcileArtist(accepted);
                reviewed.Add(item.Id);console.Write(needs?"Saved; incomplete fields remain NeedsReview.":"Saved; Ready.");break;
            }
        }
        return ManuallyReviewedCount;
    }
    private (TagMetadata? Value,bool Stop) Edit(string path,TagMetadata initial)
    {
        var names=new[]{"Artist (track)","Title (track)","Album (album)","Year (album metadata, this track)","Track number (track)","Album Artist (album)"};
        string Number(uint n)=>n==0?"":n.ToString();var values=new[]{initial.Artist,initial.Title,initial.Album,Number(initial.Year),Number(initial.Track),initial.AlbumArtist};
        void Remember()
        {
            uint N(int i)=>uint.TryParse(values[i],out var n)?n:0;
            drafts[path]=new(values[0],values[5],values[2],values[1],N(4),initial.Disc,N(3));
        }
        for(var i=0;i<names.Length;)
        {
            if(albumFirst&&i is 2 or 3 or 5){i++;continue;}
            console.Write("TRACK METADATA — Manual mode: Enter = keep | /clear | /back | /cancel | /q. Numbers here are field values, not candidate choices.");
            Prompt(path,$"{names[i]} [{(values[i]==""?"<unknown>":values[i])}]:");var input=console.Read()?.Trim();if(input==null||input.Equals("/q",StringComparison.OrdinalIgnoreCase))return(null,true);
            if(input.Equals("/cancel",StringComparison.OrdinalIgnoreCase)){Remember();return(null,false);}
            if(input.Equals("/back",StringComparison.OrdinalIgnoreCase)){if(i>0){i--;if(albumFirst&&i==3)i=1;}else return(null,false);continue;}
            if(input.Equals("/clear",StringComparison.OrdinalIgnoreCase)){values[i]="";Remember();i++;continue;}
            if(input.StartsWith('/')){console.Write("Unknown command. Use /back, /cancel, /clear or /q.");continue;}
            var answer=input==""?values[i]:input;
            if(i is 3 or 4&&answer!=""&&(!uint.TryParse(answer,out var n)||n==0||n>9999||i==3&&n<1000)){console.Write("Enter a valid number or /clear.");continue;}
            values[i]=answer;Remember();i++;
        }
        uint Parse(int i)=>values[i]==""?0:uint.Parse(values[i]);return(new(values[0],values[5],values[2],values[1],Parse(4),initial.Disc,Parse(3)),false);
    }
    private void Prompt(string path,string question)=>console.Write("\n\nPath: "+path+"\n"+question);
    private static string Display(TagMetadata v)=>$"Artist: {v.Artist}\nTitle: {v.Title}\nAlbum: {v.Album}\nYear: {v.Year}\nTrack: {v.Track}\nAlbum Artist: {v.AlbumArtist}";
}
