namespace Mp3Organizer;

public sealed record ResolutionInput(bool Escape,string Text,bool Back=false);
public interface IResolutionConsole
{
    void Write(string text);
    ResolutionInput Read(string prompt,string suggestion);
}
public sealed class ResolutionConsole(Func<ConsoleKeyInfo>? readKey=null) : IResolutionConsole
{
    public void Write(string text)=>Console.WriteLine(text);
    public ResolutionInput Read(string prompt,string suggestion)
    {
        Console.Write($"{prompt} [{suggestion}]: ");
        if(readKey==null&&Console.IsInputRedirected)throw new IOException("resolve requires an interactive terminal (Enter/Esc). Use direct JSON editing for non-interactive changes.");
        var input=new System.Text.StringBuilder();
        while(true)
        {
            var key=readKey?.Invoke()??Console.ReadKey(true);
            if(key.Key==ConsoleKey.Escape){Console.WriteLine();return new(true,"");}
            if(key.Key==ConsoleKey.Enter){Console.WriteLine();return new(false,input.Length==0?suggestion:input.ToString());}
            if(key.Key==ConsoleKey.LeftArrow&&key.Modifiers.HasFlag(ConsoleModifiers.Alt)){Console.WriteLine();return new(false,"",true);}
            if(key.Key==ConsoleKey.Backspace){if(input.Length>0){input.Length--;Console.Write("\b \b");}continue;}
            else if(!char.IsControl(key.KeyChar)){input.Append(key.KeyChar);Console.Write(key.KeyChar);}
        }
    }
}
public sealed class InteractiveResolution(ManualResolutionStore store,IResolutionConsole console)
{
    public void Run(IReadOnlyList<AudioMetadata> rows,bool onlyUnresolved,bool editManual)
    {
        var data=store.Load();
        var flows=new List<(List<ResolutionStep> Steps,Action Context)>();
        foreach(var group in rows.GroupBy(x=>Path.GetDirectoryName(x.FullPath)!,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase))
        {
            var tracks=group.OrderBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase).ToList();
            var folderEntry=ManualMetadataResolver.FindFolder(data,group.Key,tracks,rows);
            AudioMetadata Effective(AudioMetadata row)=>ManualMetadataResolver.Apply(ManualMetadataResolver.Apply(row,folderEntry.Value),data.Files.FirstOrDefault(x=>x.Key.Equals("sha256:"+row.Sha256,StringComparison.OrdinalIgnoreCase)).Value);
            var effective=tracks.Select(Effective).ToList();
            var unresolved=effective.Any(x=>ManualMetadataResolver.NeedsInput(x)||x.Identification?.Status=="Review"&&!ManualMetadataResolver.HasFields(folderEntry.Value));
            if(!unresolved&&!editManual)continue;
            if(onlyUnresolved&&!unresolved)continue;
            string Suggest(Func<AudioMetadata,string> field)=>effective.Select(field).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x).OrderByDescending(x=>x.Count()).Select(x=>x.Key).FirstOrDefault()??"";
            var artist=folderEntry.Value?.Artist??Suggest(x=>x.Artist);var album=folderEntry.Value?.Album??Suggest(x=>x.Album);
            var summary=$"Folder: {group.Key}\nFiles: {tracks.Count}\nArtist suggestion: {artist}\nAlbum suggestion: {album}";
            foreach(var item in effective.Where(x=>x.Identification!=null).Take(5))summary+=$"\nIdentification: {item.Identification!.Status}; recording={item.Identification.RecordingId}; score={item.Identification.AcoustIdScore}; fingerprint cache={item.Identification.FingerprintCacheHit}; {item.Identification.ReviewReason}";
            var key=folderEntry.Value==null?"folder:"+Guid.NewGuid().ToString("N"):folderEntry.Key;
            var folder=folderEntry.Value??new ManualOverride{LastKnownPath=group.Key,AnchorSha256=tracks.Select(x=>x.Sha256.ToLowerInvariant()).Where(x=>x.Length==64).Distinct().Order().ToArray()};
            var steps=new List<ResolutionStep>();
            void SaveFolder(){data.Folders[key]=folder;store.Save(data);}
            if(folder.Artist==null||folder.Album==null||editManual)
            {
                if(folder.Artist==null||editManual)steps.Add(new("Artist",()=>folder.Artist??artist,text=>{folder=folder with{Artist=text,LastKnownPath=group.Key};SaveFolder();}));
                if(folder.Album==null||editManual)steps.Add(new("Album",()=>folder.Album??album,text=>{folder=folder with{Album=text};SaveFolder();}));
                steps.Add(new("Year (optional)",()=>folder.Year?.ToString()??effective.FirstOrDefault(x=>x.Year!=0)?.Year.ToString()??"",text=>{folder=folder with{Year=text==""?null:uint.Parse(text)};SaveFolder();},true,true));
            }
            foreach(var track in tracks)
            {
                var fileKey="sha256:"+track.Sha256.ToLowerInvariant();
                var fileEntry=data.Files.FirstOrDefault(x=>x.Key.Equals(fileKey,StringComparison.OrdinalIgnoreCase));
                var manual=fileEntry.Value??new ManualOverride{LastKnownPath=track.FullPath};
                AudioMetadata Current()=>ManualMetadataResolver.Apply(ManualMetadataResolver.Apply(track,folder),manual);
                var row=Current();
                void SaveFile(){if(fileEntry.Value!=null&&fileEntry.Key!=fileKey)data.Files.Remove(fileEntry.Key);data.Files[fileKey]=manual;store.Save(data);}
                void Context()
                {
                    var current=Current();
                    string Text(string value)=>string.IsNullOrWhiteSpace(value)?"<missing>":value;
                    console.Write($"File: {track.FileName}\n\nCurrent:\nArtist: {Text(current.Artist)}\nAlbum: {Text(current.Album)}\nTitle: {Text(current.Title)}\nTrack number: {(current.Track==0?"<missing>":current.Track.ToString())}\nDisc number: {(current.Disc==0?"<missing>":current.Disc.ToString())}\n\n\nPath: {track.FullPath}");
                }
                var ambiguous=manual.Title==null&&row.Identification?.Status=="Review"&&row.Identification.ReviewReason.Contains("title",StringComparison.OrdinalIgnoreCase);
                var editFile=editManual&&fileEntry.Value!=null;
                if(MetadataQualityEvaluator.Invalid(row.Title)||MetadataQualityEvaluator.FilenameTitle(row)||ambiguous||editFile)
                    steps.Add(new("Title (Esc = stop this folder)",()=>Current().Title,text=>{manual=manual with{Title=text,LastKnownPath=track.FullPath};SaveFile();},Context:Context));
                if(row.Track==0||editFile)
                    steps.Add(new("Track number (Esc = stop this folder)",()=>{var current=Current();var number=current.Track>0?current.Track:FilenameTrackParser.Parse(current.FileName)?.Number??0;return number==0?"":number.ToString();},text=>{manual=manual with{TrackNumber=uint.Parse(text),LastKnownPath=track.FullPath};SaveFile();},true,Context:Context));
            }
            flows.Add((steps,()=>console.Write(summary+$"\n\n\nFolder: {group.Key}")));
        }
        var navigators=flows.Select(_=>new ResolutionNavigator(console)).ToArray();
        var folderIndex=0;var resume=false;
        while(folderIndex<flows.Count)
        {
            var flow=flows[folderIndex];
            var previous=navigators[folderIndex].Run(flow.Steps,flow.Context,folderIndex>0,resume);
            if(previous){folderIndex--;resume=true;}else{folderIndex++;resume=false;}
        }
        store.WriteOverview(data);
    }
}

public sealed record ResolutionStep(string Prompt,Func<string> Default,Action<string> Save,bool Numeric=false,bool Optional=false,Action? Context=null);

// Index -1 is the folder selection screen; 0..Count-1 are stable, revisitable questions.
public sealed class ResolutionNavigator(IResolutionConsole console)
{
    private int lastQuestion=-1;
    private readonly Dictionary<int,string> accepted=new();
    public bool Run(IReadOnlyList<ResolutionStep> steps,Action selectionContext,bool canGoPrevious=false,bool resume=false)
    {
        var index=resume?lastQuestion:-1;
        console.Write("Enter = accept | Backspace = delete text | Alt+Left = previous | Esc = skip/abort folder");
        while(index<steps.Count)
        {
            if(index<0)
            {
                selectionContext();var selection=console.Read("Enter = resolve folder; Esc = skip","");
                if(selection.Escape)return false;
                if(selection.Back){if(canGoPrevious)return true;console.Write("This is the first folder in this session.");continue;}
                index=0;continue;
            }
            lastQuestion=index;
            var step=steps[index];
            if(step.Context!=null)step.Context();else selectionContext();
            var suggestion=accepted.GetValueOrDefault(index,step.Default());
            var answer=console.Read(step.Prompt,suggestion);
            if(answer.Escape)return false;
            if(answer.Back){index--;continue;}
            var value=answer.Text==""?suggestion:answer.Text;
            if(step.Numeric&&!(step.Optional&&value=="")&&(!uint.TryParse(value,out var number)||number is 0 or >9999))
            {console.Write("Enter a number from 1 to 9999"+(step.Optional?", or leave empty.":"."));continue;}
            step.Save(value);accepted[index]=value;index++;
        }
        return false;
    }
}