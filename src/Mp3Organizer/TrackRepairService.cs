namespace Mp3Organizer;
public sealed class TrackRepairService(ProgressRepository repository,string workspace,IReviewConsole console)
{
    public int Run(string folder)
    {
        folder=Path.TrimEndingDirectorySeparator(PathSafetyGuard.Canonical(folder));
        var rows=repository.All().Where(x=>string.Equals(Path.GetDirectoryName(x.CurrentPath),folder,StringComparison.OrdinalIgnoreCase)&&x.Status is ProcessingStatus.Processed or ProcessingStatus.Ready or ProcessingStatus.NeedsReview).OrderBy(x=>x.CurrentPath,NaturalPathComparer.Instance).ToList();
        if(rows.Count==0){console.Write("No eligible tracks directly in this source folder.");return 0;}
        var saved=0;
        foreach(var item in rows)
        {
            var draft=TagMetadata.From(item.Effective);
            while(true)
            {
                console.Write($"\nPath: {item.CurrentPath}\nArtist: {draft.Artist}\nTitle: {draft.Title}\nTrack: {draft.Track}\n[N] Suggest from original filename | [M] Edit Artist/Title/Track | [Enter] Keep and next | [D] Defer | [/q] Stop");
                var action=console.Read()?.Trim();if(action==null||action.Equals("/q",StringComparison.OrdinalIgnoreCase))return saved;
                if(action==""||action.Equals("D",StringComparison.OrdinalIgnoreCase))break;
                if(action.Equals("N",StringComparison.OrdinalIgnoreCase))
                {
                    var parsed=new FilenameMetadataFallback().Resolve(item.Basic with{Artist="",TrackArtists=[],Title="",Track=0,Album=item.Effective.Album});
                    var title=parsed.Title;var tail=title.LastIndexOf(" - ",StringComparison.Ordinal);
                    string Key(string x)=>MetadataNormalizer.Key(x).Replace("THE ","");
                    if(tail>0&&Key(title[(tail+3)..])==Key(item.Effective.Album))title=title[..tail];
                    draft=draft with{Artist=parsed.Artist,Title=title,Track=parsed.Track};
                }
                else if(action.Equals("M",StringComparison.OrdinalIgnoreCase))
                {
                    var values=new[]{draft.Artist,draft.Title,draft.Track.ToString()};var names=new[]{"Artist","Title","Track"};var cancelled=false;
                    for(var i=0;i<3;)
                    {
                        console.Write($"{names[i]} [{values[i]}]: Enter keeps; /back previous; /cancel menu; /q stop");var answer=console.Read()?.Trim();
                        if(answer==null||answer.Equals("/q",StringComparison.OrdinalIgnoreCase))return saved;
                        if(answer.Equals("/cancel",StringComparison.OrdinalIgnoreCase)){cancelled=true;break;}
                        if(answer.Equals("/back",StringComparison.OrdinalIgnoreCase)){i=Math.Max(0,i-1);continue;}
                        if(answer.StartsWith('/'))continue;
                        var next=answer==""?values[i]:answer;
                        if(i==2&&(!uint.TryParse(next,out var number)||number>9999)){console.Write("Enter a track number from 0 to 9999.");continue;}
                        values[i++]=next;
                    }
                    if(cancelled)continue;draft=draft with{Artist=values[0],Title=values[1],Track=uint.Parse(values[2])};
                }
                else continue;
                console.Write($"Proposed correction:\nArtist: {draft.Artist}\nTitle: {draft.Title}\nTrack: {draft.Track}\nAlbum stays: {draft.AlbumArtist} / {draft.Album} / {draft.Year}\nEnter/YES = save | M = edit proposal | /cancel = menu | /q = stop");
                var confirm=console.Read()?.Trim();if(confirm==null||confirm.Equals("/q",StringComparison.OrdinalIgnoreCase))return saved;
                if(confirm!=""&&!confirm.Equals("YES",StringComparison.OrdinalIgnoreCase))continue;
                if(new[]{draft.Artist,draft.Title}.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>500||x.Any(char.IsControl))){console.Write("Not saved: Artist and Title must be nonempty, at most 500 characters, without control characters.");continue;}
                var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(item.CurrentPath);if(actual.Error!=""||actual.Sha256!=item.Basic.Sha256){console.Write("Source changed; scan before repair.");break;}
                var store=new ManualResolutionStore(workspace,item.SourceRoot);var data=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();data.Files.TryGetValue(key,out var prior);
                var value=(prior??new()) with{LastKnownPath=item.CurrentPath,Artist=draft.Artist,Title=draft.Title,TrackNumber=draft.Track,ExplicitlyConfirmedTitle=draft.Title};
                if(value.AcceptedCandidateMetadata!=null){var accepted=new Dictionary<string,TagMetadata>(value.AcceptedCandidateMetadata);if(accepted.ContainsKey(item.Id))accepted[item.Id]=draft;value=value with{AcceptedCandidateMetadata=accepted};}
                data.Files[key]=value;store.Save(data);
                var evidence=item.Effective.Identification??new();var fields=new Dictionary<string,string>(evidence.FieldSources){["Artist"]="FileManualOverride",["Title"]="FileManualOverride",["Track"]="FileManualOverride",["TrackRepair"]="FileManualOverride"};
                var updated=item.Effective with{Artist=draft.Artist,TrackArtists=[draft.Artist],Title=draft.Title,Track=draft.Track,ExplicitlyConfirmedTitle=draft.Title,Identification=evidence with{FieldSources=fields}};
                repository.Save(item with{Effective=updated,Status=item.Status==ProcessingStatus.Processed?ProcessingStatus.Processed:ManualMetadataResolver.NeedsInput(updated)?ProcessingStatus.NeedsReview:ProcessingStatus.Ready},"Explicit track repair; output update pending");
                saved++;console.Write("Saved track correction. Run 04 to update organized files and playlists.");break;
            }
        }
        return saved;
    }
}
