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
    public int Run()
    {
        var handled=0;
        foreach(var item in repository.All().Where(x=>x.Status==ProcessingStatus.NeedsReview))
        {
            var row=item.Effective;var evidence=row.Identification??new(){Original=TagMetadata.From(item.Basic)};
            var choices=evidence.Candidates.Take(9).ToList();
            while(true)
            {
                console.Write($"File: {item.CurrentPath}\nExisting tags: {JsonFormat.Serialize(TagMetadata.From(item.Basic))}\nResolved so far: {JsonFormat.Serialize(TagMetadata.From(row))}\nDuration: {row.DurationSeconds:F1}s; bitrate: {row.Bitrate} kbps\nReasons: {string.Join("; ",evidence.ReviewReasons)}\n{evidence.ReviewReason}");
                for(var i=0;i<choices.Count;i++)console.Write($"Candidate {i+1}: {JsonFormat.Serialize(choices[i].Metadata)}\nEvidence score: {choices[i].Confidence:P1} (not a probability)\nEvidence: {JsonFormat.Serialize(choices[i].Evidence)}\n{choices[i].Detail}");
                console.Write("[Enter] Accept candidate 1 | [1-9] Select | [M] Manual | [P] Play | [S] Skip\n\n\nPath: "+item.CurrentPath);
                var input=console.Read();if(input==null)return handled;
                input=input.Trim().ToUpperInvariant();
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
                if(input=="S"){repository.Save(item with{Status=ProcessingStatus.Skipped,LastProcessedUtc=DateTime.UtcNow.ToString("O")},"User skipped review");handled++;break;}
                TagMetadata? selected=null;
                if(input=="M")selected=Edit(item.CurrentPath,TagMetadata.From(row));
                else if(choices.Count>0&&(input==""||int.TryParse(input,out var n)&&n>=1&&n<=choices.Count))
                {
                    var candidate=choices[input==""?0:int.Parse(input)-1].Metadata;
                    // Missing candidate fields never clear useful current values.
                    selected=new(Useful(candidate.Artist,row.Artist),Useful(candidate.AlbumArtist,row.AlbumArtist),Useful(candidate.Album,row.Album),Useful(candidate.Title,row.Title),candidate.Track>0?candidate.Track:row.Track,candidate.Disc>0?candidate.Disc:row.Disc,candidate.Year>0?candidate.Year:row.Year);
                }
                else{console.Write("Choose an available candidate, M, P or S. No candidate is fabricated when lookup failed.");continue;}
                if(selected==null)return handled;
                if(MetadataQualityEvaluator.Invalid(selected.Artist)||MetadataQualityEvaluator.Invalid(selected.Album)||MetadataQualityEvaluator.Invalid(selected.Title)||selected.Track==0)
                {console.Write("Artist, album, meaningful title and track are required. Use M to complete these fields, or S to skip.");continue;}
                if(selected.Year!=0&&(selected.Year<1000||selected.Year>9999)){console.Write("Year must be four digits or empty.");continue;}
                console.Write($"Confirm metadata{(tags!=null?" and MP3 tag write (audio unchanged)":"")}: {JsonFormat.Serialize(selected)}\nType YES to confirm.\n\n\nPath: {item.CurrentPath}");
                if(console.Read()!="YES"){console.Write("Not saved.");continue;}
                var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(item.CurrentPath);
                if(actual.Error!=""||actual.Sha256!=item.Basic.Sha256){console.Write("File changed; run scan before reviewing this entry.");break;}
                var store=new ManualResolutionStore(workspace,item.SourceRoot);var manual=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();
                manual.Files[key]=new(){LastKnownPath=item.CurrentPath,Artist=selected.Artist,AlbumArtist=selected.AlbumArtist,Album=selected.Album,Title=selected.Title,TrackNumber=selected.Track,DiscNumber=selected.Disc==0?null:selected.Disc,Year=selected.Year==0?null:selected.Year};store.Save(manual);
                var updated=ManualMetadataResolver.Apply(row,manual.Files[key]);
                updated=updated with{Identification=evidence with{Status="ManualResolved",Source=MetadataSource.FileManualOverride,ManualOverrideHit=true,AutoRecognized=false,ReviewReasons=[],ReviewReason="",FieldSources=MetadataFieldMerger.ManualSources(evidence,null,manual.Files[key])}};
                var accepted=item with{Effective=updated,Status=ProcessingStatus.Ready,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=""};
                try
                {
                    if(tags!=null)tags.WriteAndSave(accepted,true);
                    else repository.Save(accepted,"Manual review confirmed; authoritative override saved");
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {repository.Save(accepted with{Status=ProcessingStatus.Error,ErrorMessage=ex.Message},"Review accepted, tag operation failed; backup/journal retained");console.Write(ex.Message);}
                handled++;break;
            }
        }
        return handled;
    }
    private static string Useful(string candidate,string current)=>MetadataQualityEvaluator.Invalid(candidate)?current:candidate;
    private TagMetadata? Edit(string path,TagMetadata value)
    {
        string? Text(string field,string current)
        {console.Write($"{field} [{current}]\n\n\nPath: {path}\nEnter {field}:");var text=console.Read();return text==""?current:text;}
        uint? Number(string field,uint current,bool optional)
        {
            while(true){var text=Text(field,current==0?"":current.ToString());if(text==null)return null;if(optional&&text=="")return 0;if(uint.TryParse(text,out var n)&&n>0&&n<=9999&&(field!="Year"||n>=1000))return n;console.Write("Enter a valid number; Year must have four digits.");}
        }
        var artist=Text("Artist",value.Artist);if(artist==null)return null;var albumArtist=Text("AlbumArtist",value.AlbumArtist);if(albumArtist==null)return null;
        var album=Text("Album",value.Album);if(album==null)return null;var title=Text("Title",value.Title);if(title==null)return null;
        var track=Number("Track",value.Track,false);if(track==null)return null;var disc=Number("Disc",value.Disc,true);if(disc==null)return null;var year=Number("Year",value.Year,true);if(year==null)return null;
        return new(artist,albumArtist,album,title,track.Value,disc.Value,year.Value);
    }
}
