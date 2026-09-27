using System.Diagnostics;

namespace Mp3Organizer;

public interface IReviewConsole {void Write(string text);string? Read();}
public sealed class ReviewConsole:IReviewConsole
{
    public void Write(string text)=>Console.WriteLine(text);
    public string? Read()=>Console.ReadLine();
}
public sealed class ReviewService(ProgressRepository repository,string workspace,IReviewConsole console,Action<string>? play=null,Mp3TagWriteService? tags=null)
{
    private readonly HashSet<string> manuallyReviewed=new(StringComparer.OrdinalIgnoreCase);
    public int ManuallyReviewedCount=>manuallyReviewed.Count;
    public int AutomaticallyResolvedCount {get;private set;}
    public int SkippedCount {get;private set;}
    private sealed class EditState(string[] values,int index)
    {
        public string[] Values=values;public int Index=index;public List<int> Visited=new();
    }
    private readonly Dictionary<string,EditState> drafts=new(StringComparer.OrdinalIgnoreCase);
    private bool previousSong;
    private bool canGoBack;
    private readonly HashSet<string> finishFolders=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,TagMetadata> albums=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,uint> albumYears=new(StringComparer.OrdinalIgnoreCase);
    private static string YearKey(string path,TagMetadata value)=>Path.GetDirectoryName(path)+"|"+MetadataNormalizer.Key(value.AlbumArtist==""?value.Artist:value.AlbumArtist)+"|"+MetadataNormalizer.Key(value.Album);
    public int Run()
    {
        var handled=0;var counted=new HashSet<string>();
        foreach(var invalid in repository.All().Where(x=>x.Status==ProcessingStatus.Ready&&MetadataPlaceholderDetector.IsGenericTitle(x.Effective.Title)&&!(ManualMetadataResolver.ConfirmedNumericTitle(x.Effective.Title)&&x.Effective.Identification?.FieldSources.GetValueOrDefault("Title") is "FileManualOverride" or "FolderManualOverride")))
        {
            var old=invalid.Effective.Identification??new IdentificationEvidence();
            repository.Save(invalid with{Status=ProcessingStatus.NeedsReview,Effective=invalid.Effective with{Identification=old with{Status="Review",ReviewReasons=old.ReviewReasons.Append("GenericTrackTitle").Distinct().ToArray(),ReviewReason="Track title is missing or generic; enter the actual song title."}}},"Review validation reopened generic title");
        }
        // Reuse only explicit manual album years, never infer a year from a folder name.
        foreach(var group in repository.All().Where(x=>x.Effective.Year>0&&x.Effective.Identification?.FieldSources.GetValueOrDefault("Year") is "FileManualOverride" or "FolderManualOverride").GroupBy(x=>YearKey(x.CurrentPath,TagMetadata.From(x.Effective)),StringComparer.OrdinalIgnoreCase))
        {
            var years=group.Select(x=>x.Effective.Year).Distinct().ToArray();if(years.Length==1)albumYears[group.Key]=years[0];
        }
        var reviewItems=repository.All().Where(x=>x.Status==ProcessingStatus.NeedsReview).OrderBy(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase).ToList();
        for(var position=0;position<reviewItems.Count;position++)
        {
            var item=repository.All().First(x=>x.Id==reviewItems[position].Id);
            var previousPosition=Enumerable.Range(0,position).Reverse().FirstOrDefault(i=>drafts.ContainsKey(reviewItems[i].CurrentPath),-1);
            canGoBack=previousPosition>=0;
            previousSong=false;
            var row=item.Effective;var evidence=row.Identification??new(){Original=TagMetadata.From(item.Basic)};
            if(row.Year==0&&albumYears.TryGetValue(YearKey(item.CurrentPath,TagMetadata.From(row)),out var knownYear))row=row with{Year=knownYear};
            if(tags==null&&!drafts.ContainsKey(item.CurrentPath)&&!albums.ContainsKey(Path.GetDirectoryName(item.CurrentPath)!)&&row.Year>=1000&&row.Year<=9999&&albumYears.TryGetValue(YearKey(item.CurrentPath,TagMetadata.From(row)),out var approvedYear)&&approvedYear==row.Year
                &&evidence.ReviewReasons.Length>0&&evidence.ReviewReasons.All(x=>x=="UnknownYear")&&!ManualMetadataResolver.NeedsInput(row))
            {
                var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(item.CurrentPath);
                if(actual.Error!=""||actual.Sha256!=item.Basic.Sha256){console.Write("File changed; run scan before reviewing: "+item.CurrentPath);continue;}
                var yearStore=new ManualResolutionStore(workspace,item.SourceRoot);var yearManual=yearStore.Load();var yearFileKey="sha256:"+item.Basic.Sha256.ToLowerInvariant();
                yearManual.Files.TryGetValue(yearFileKey,out var existing);
                yearManual.Files[yearFileKey]=(existing??new ManualOverride()) with{LastKnownPath=item.CurrentPath,Year=row.Year};yearStore.Save(yearManual);
                var sources=new Dictionary<string,string>(evidence.FieldSources){["Year"]="FileManualOverride"};
                var ready=row with{Date=row.Year.ToString(),Identification=evidence with{Status="Resolved",ReviewReasons=[],ReviewReason="",ManualOverrideHit=true,FieldSources=sources}};
                repository.Save(item with{Effective=ready,Status=ProcessingStatus.Ready,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=""},"UnknownYear resolved by confirmed manual album year; Ready");
                console.Write($"Ready: {item.CurrentPath} — Title: {row.Title}; saved album year {row.Year} resolved UnknownYear; no review needed.");if(counted.Add(item.CurrentPath)){handled++;AutomaticallyResolvedCount++;}continue;
            }
            var choices=evidence.Candidates.Take(9).ToList();
            while(true)
            {
                console.Write($"File: {Path.GetFileName(item.CurrentPath)}\nCurrent suggestions (not yet confirmed):\n{Display(TagMetadata.From(row))}\nDuration: {row.DurationSeconds:F1}s; bitrate: {row.Bitrate} kbps\nNeeds review: {string.Join("; ",evidence.ReviewReasons)}\n{evidence.ReviewReason}");
                for(var i=0;i<choices.Count;i++)console.Write($"Option {i+1} — online metadata suggestion:\n{Display(choices[i].Metadata)}\nEvidence score: {choices[i].Confidence:P1} (not a probability)\n{choices[i].Detail}");
                if(!drafts.ContainsKey(item.CurrentPath)&&!albums.ContainsKey(Path.GetDirectoryName(item.CurrentPath)!))console.Write("Enter / A = accept and save the displayed values | M = guided questions, Album artist first | F = finish this album using current defaults.\nP = play this file | S = skip this file permanently | Q = stop review; remaining files stay pending."+
                    (choices.Count>0?$"\nNumbers 1–{choices.Count} = choose the matching option shown above, and save it.":"\nNo online options are available. Enter accepts the displayed suggestions; M edits them.")+"\n\n\nPath: "+item.CurrentPath+"\nYour choice [Enter = accept | M = guided questions]:");
                var input=drafts.ContainsKey(item.CurrentPath)||albums.ContainsKey(Path.GetDirectoryName(item.CurrentPath)!)?"M":console.Read();if(input==null)return handled;
                input=input.Trim().ToUpperInvariant();
                if(input=="Q")return handled;
                if(input=="P")
                {
                    try
                    {
                        PathSafetyGuard.NoLinks(item.CurrentPath);if(!File.Exists(item.CurrentPath))throw new FileNotFoundException("File missing");
                        if(play!=null)play(item.CurrentPath);else Process.Start(new ProcessStartInfo(item.CurrentPath){UseShellExecute=true});
                    }
                    catch(Exception ex) when(ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){console.Write("Playback failed: "+ex.Message);}
                    continue;
                }
                if(input=="S"){repository.Save(item with{Status=ProcessingStatus.Skipped,LastProcessedUtc=DateTime.UtcNow.ToString("O")},"User skipped review");if(counted.Add(item.CurrentPath)){handled++;SkippedCount++;}break;}
                TagMetadata? selected=null;
                if(input is "" or "A")selected=TagMetadata.From(row);
                else if(input=="F"){finishFolders.Add(Path.GetDirectoryName(item.CurrentPath)!);selected=Edit(item.CurrentPath,TagMetadata.From(row),true);}
                else if(input=="M")selected=Edit(item.CurrentPath,TagMetadata.From(row),true);
                else if(choices.Count>0&&int.TryParse(input,out var n)&&n>=1&&n<=choices.Count)
                {
                    var candidate=choices[int.Parse(input)-1].Metadata;
                    // Missing candidate fields never clear useful current values.
                    selected=new(Useful(candidate.Artist,row.Artist),Useful(candidate.AlbumArtist,row.AlbumArtist),Useful(candidate.Album,row.Album),Useful(candidate.Title,row.Title),candidate.Track>0?candidate.Track:row.Track,candidate.Disc>0?candidate.Disc:row.Disc,candidate.Year>0?candidate.Year:row.Year);
                }
                else{console.Write("Press Enter or A to accept, M for guided questions, P to play, S to skip or Q to stop. Numbers only select an option actually displayed above.");continue;}
                if(previousSong)
                {
                    var previousPath=reviewItems[previousPosition].CurrentPath;var previous=drafts[previousPath];
                    previous.Index=previous.Visited[^1];previous.Visited.RemoveAt(previous.Visited.Count-1);
                    finishFolders.Clear();position=previousPosition-1;break;
                }
                if(selected==null)return handled;
                if(MetadataQualityEvaluator.Invalid(selected.Artist)||MetadataQualityEvaluator.Invalid(selected.Album)||(MetadataQualityEvaluator.Invalid(selected.Title)&&!ManualMetadataResolver.ConfirmedNumericTitle(selected.Title))||selected.Track==0)
                {
                    var missing=new List<string>();
                    if(MetadataQualityEvaluator.Invalid(selected.Artist))missing.Add("Artist");
                    if(MetadataQualityEvaluator.Invalid(selected.Album))missing.Add("Album");
                    if(MetadataQualityEvaluator.Invalid(selected.Title)&&!ManualMetadataResolver.ConfirmedNumericTitle(selected.Title))missing.Add("Title");
                    if(selected.Track==0)missing.Add("Track number");
                    console.Write("Not saved. Missing or suspicious: "+string.Join(", ",missing)+". Press M for guided questions, or Q to leave unresolved.");continue;
                }
                if(selected.Year==0&&input!="M")
                {
                    var albumKey=YearKey(item.CurrentPath,selected);
                    if(!albumYears.TryGetValue(albumKey,out var year))
                    {
                        while(true)
                        {
                            console.Write($"Album: {selected.Album}\nEnter the album year once; Enter = unknown, /q = stop.\n\n\nPath: {Path.GetDirectoryName(item.CurrentPath)}\nAlbum year []:");
                            var answer=console.Read()?.Trim();if(answer==null||answer.Equals("/q",StringComparison.OrdinalIgnoreCase))return handled;
                            if(answer==""){year=0;break;}
                            if(uint.TryParse(answer,out year)&&year>=1000&&year<=9999)break;
                            console.Write("Enter a four-digit album year.");
                        }
                        albumYears[albumKey]=year;
                    }
                    selected=selected with{Year=year};
                }
                if(selected.Year!=0&&(selected.Year<1000||selected.Year>9999)){console.Write("Year must be four digits or empty.");continue;}
                row=row with{Artist=selected.Artist,AlbumArtist=selected.AlbumArtist,Album=selected.Album,Title=selected.Title,Track=selected.Track,Disc=selected.Disc,Year=selected.Year};
                if(tags!=null)
                {
                console.Write($"Review your answers:\n{Display(selected)}\nYES = save these answers{(tags!=null?" and write MP3 tags (audio unchanged)":" as manual metadata")}; anything else returns to editing.\n\n\nPath: {item.CurrentPath}\nSave [YES]:");
                if(!string.Equals(console.Read()?.Trim(),"YES",StringComparison.OrdinalIgnoreCase)){console.Write("Not saved. Your edited suggestions are retained for this file.");continue;}
                }
                var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(item.CurrentPath);
                if(actual.Error!=""||actual.Sha256!=item.Basic.Sha256){console.Write("File changed; run scan before reviewing this entry.");break;}
                var store=new ManualResolutionStore(workspace,item.SourceRoot);var manual=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();
                manual.Files[key]=new(){LastKnownPath=item.CurrentPath,Artist=selected.Artist,AlbumArtist=selected.AlbumArtist,Album=selected.Album,Title=selected.Title,TrackNumber=selected.Track,DiscNumber=selected.Disc==0?null:selected.Disc,Year=selected.Year==0?null:selected.Year};store.Save(manual);
                var updated=ManualMetadataResolver.Apply(row,manual.Files[key]);
                updated=updated with{Identification=evidence with{MetadataPolicyVersion=2,Status="ManualResolved",RecognitionMethod="Manual",Source=MetadataSource.FileManualOverride,ManualOverrideHit=true,AutoRecognized=false,ReviewReasons=[],ReviewReason="",FieldSources=MetadataFieldMerger.ManualSources(evidence,null,manual.Files[key])}};
                var accepted=item with{Effective=updated,Status=ProcessingStatus.Ready,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=""};
                try
                {
                    if(tags!=null)tags.WriteAndSave(accepted,true);
                    else repository.Save(accepted,"Manual review confirmed; authoritative override saved");
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
                {repository.Save(accepted with{Status=ProcessingStatus.Error,ErrorMessage=ex.Message},"Review accepted, tag operation failed; backup/journal retained");console.Write(ex.Message);}
                new AlbumYearCorrectionService(repository,workspace).Propagate();
                manuallyReviewed.Add(item.CurrentPath);if(counted.Add(item.CurrentPath))handled++;break;
            }
        }
        return handled;
    }
    private static string Useful(string candidate,string current)=>MetadataQualityEvaluator.Invalid(candidate)?current:candidate;
    private static string Display(TagMetadata value)=>$"Artist: {value.Artist}\nAlbum: {value.Album}\nYear: {(value.Year==0?"<missing>":value.Year)}\nTrack number: {(value.Track==0?"<missing>":value.Track)}\nTitle: {value.Title}\nAlbum artist: {value.AlbumArtist}";
    private TagMetadata? Edit(string path,TagMetadata value,bool editAll)
    {
        string Number(uint n)=>n==0?"":n.ToString();
        var folder=Path.GetDirectoryName(path)!;
        var reused=albums.TryGetValue(folder,out var shared);
        var albumArtist=shared?.AlbumArtist??value.AlbumArtist;
        if(string.IsNullOrWhiteSpace(albumArtist))albumArtist=value.Artist;
        var fields=new[]{"Album artist","Album title","Album year (optional)","Title","Track number","Artist"};
        var initialValues=new[]{albumArtist,shared?.Album??value.Album,Number(shared?.Year??value.Year),value.Title,Number(value.Track),value.Artist};
        if(!drafts.TryGetValue(path,out var state)){state=new(initialValues,reused?3:0);drafts[path]=state;}
        var values=state.Values;
        console.Write("Album fields apply to this folder's reviewed songs. Enter accepts [suggestion]; type a replacement. /back = previous; /q = stop review; F = finish album using current defaults (not numbered online options). Backspace edits text.");
        var visited=state.Visited;
        for(var index=state.Index;index<fields.Length;)
        {
            if(index==2&&values[2]!=""){index++;continue;}
            if(index==5&&!CompilationAlbumArtist(values[0])){values[5]=values[0];index++;continue;}
            state.Index=index;
            var context=index<3?folder:path;
            console.Write($"Question — {fields[index]}\n\n\nPath: {context}\n{fields[index]} [{values[index]}]:");
            bool SafeDefault()=>index switch
            {
                2=>uint.TryParse(values[index],out var y)&&y>=1000&&y<=9999,
                4=>uint.TryParse(values[index],out var n)&&n>0&&n<=9999,
                0=>CompilationAlbumArtist(values[index])||!MetadataQualityEvaluator.Invalid(values[index]),
                5=>!CompilationAlbumArtist(values[index])&&!MetadataQualityEvaluator.Invalid(values[index]),
                _=>!MetadataQualityEvaluator.Invalid(values[index])
            };
            if(finishFolders.Contains(folder)&&!SafeDefault()){finishFolders.Remove(folder);console.Write("Auto-finish paused: this field needs your input. F can resume afterwards.");}
            var input=finishFolders.Contains(folder)?"":console.Read();if(input==null)return null;input=input.Trim();
            if(input.Equals("F",StringComparison.OrdinalIgnoreCase))
            {
                if(!SafeDefault()){console.Write("Cannot auto-finish this field: enter a meaningful value first.");continue;}
                finishFolders.Add(folder);console.Write("Auto-finish enabled for this album folder; current defaults will be saved.");input="";
            }
            if(input=="/"||input.StartsWith("/")&&!input.Equals("/back",StringComparison.OrdinalIgnoreCase)&&!input.Equals("/q",StringComparison.OrdinalIgnoreCase))
            {console.Write("Commands: /back = previous question (including previous song); /q = stop; F = finish album. Enter accepts [suggestion].");continue;}
            if(input.Equals("/q",StringComparison.OrdinalIgnoreCase))return null;
            if(input.Equals("/back",StringComparison.OrdinalIgnoreCase))
            {
                if(visited.Count>0){index=visited[^1];visited.RemoveAt(visited.Count-1);}
                else if(canGoBack){previousSong=true;return null;}
                else console.Write("This is the first question in this review session.");
                continue;
            }
            var answer=input==""?values[index]:input;
            if(index is 2 or 4)
            {
                if(!(index==2&&answer=="")&&(!uint.TryParse(answer,out var n)||n==0||n>9999||index==2&&n<1000)){console.Write("Enter a positive track number or a four-digit album year (optional)." );continue;}
            }
            else if(index==3&&MetadataQualityEvaluator.Invalid(answer)&&!ManualMetadataResolver.ConfirmedNumericTitle(answer))
            {console.Write("This is a placeholder title. Enter the actual song title, or /q to leave it unresolved.");continue;}
            else if(string.IsNullOrWhiteSpace(answer)||index==5&&CompilationAlbumArtist(answer)||index is 0 or 1 or 5&&MetadataQualityEvaluator.Invalid(answer)&&!(index==0&&CompilationAlbumArtist(answer)))
            {console.Write("Please enter a meaningful "+fields[index]+".");continue;}
            values[index]=index==0&&CompilationAlbumArtist(answer)?"Various Artists":answer;visited.Add(index);index++;
        }
        state.Index=fields.Length;
        uint Parse(int i)=>values[i]==""?0:uint.Parse(values[i]);
        var result=new TagMetadata(values[5],values[0],values[1],values[3],Parse(4),value.Disc,Parse(2));
        albums[folder]=result;albumYears[YearKey(path,result)]=result.Year;
        return result;
    }

    public static bool CompilationAlbumArtist(string value)
    {
        var key=System.Text.RegularExpressions.Regex.Replace(MetadataNormalizer.Key(value),@"[^\p{L}\p{N}]","");
        return key is "VARIOUS" or "VARIOUSARTISTS" or "VA" or "VARIOUSPERFORMERS" or "MULTIPLEARTISTS" or "DIVERSE" or "DIVERSEINTERPRETEN" or "VERSCHIEDENEINTERPRETEN";
    }
}
