using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Mp3Organizer;

public sealed record SourcePlaylist(long Id,string SourceRoot,string Path,string Name,bool Present,string Error)
{
    public string Code=>"Playlist "+Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string TargetPath=>System.IO.Path.Combine("_Playlist-Folder",$"Code {Code} - {TargetPathBuilder.SafeName(Name)}.m3u8");
}
public sealed record SourcePlaylistEntry(int Position,string OriginalEntry,string FileId,string Problem,string Result,string TargetPath);

public static class SourcePlaylistReader
{
    public static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".m3u",".m3u8",".pls",".wpl",".xspf",".asx",".zpl",".b4s",".fpl",".cue"};
    public static List<string> Read(string path)
    {
        PathSafetyGuard.NoLinks(path);
        using var stream=new ReadOnlySource().OpenRead(path);
        if(stream.Length>8*1024*1024)throw new IOException("Playlist exceeds 8 MiB limit.");
        var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);
        var extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(extension is ".wpl" or ".xspf" or ".asx")
        {
            using var input=new MemoryStream(bytes);
            using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});
            var doc=XDocument.Load(reader);var root=doc.Root?.Name.LocalName.ToLowerInvariant();
            if(root!=(extension==".wpl"?"smil":extension==".xspf"?"playlist":"asx"))throw new IOException("Unexpected XML playlist root.");
            var entries=extension==".xspf"?doc.Descendants().Where(x=>x.Name.LocalName=="track").Select(x=>{
                var locations=x.Elements().Where(y=>y.Name.LocalName=="location").ToList();
                if(locations.Count!=1)throw new IOException("XSPF track must have exactly one location.");var location=locations[0].Value;
                return Uri.TryCreate(location,UriKind.Absolute,out _)?location:Uri.UnescapeDataString(location);
            }):doc.Descendants().Where(x=>x.Name.LocalName.Equals(extension==".wpl"?"media":"ref",StringComparison.OrdinalIgnoreCase)).Select(x=>x.Attributes().FirstOrDefault(a=>a.Name.LocalName.Equals(extension==".wpl"?"src":"href",StringComparison.OrdinalIgnoreCase))?.Value??throw new IOException("Missing XML playlist location."));
            return entries.Select(x=>x.Trim()).ToList();
        }
        if(extension is not (".m3u" or ".m3u8" or ".pls"))throw new IOException("Unsupported playlist format "+extension+"; export it as M3U8 to import its entries.");
        string text;
        try
        {
            if(extension!=".m3u8"&&bytes.Length>=2&&bytes[0]==255&&bytes[1]==254)text=new UnicodeEncoding(false,true,true).GetString(bytes.AsSpan(2));
            else if(extension!=".m3u8"&&bytes.Length>=2&&bytes[0]==254&&bytes[1]==255)text=new UnicodeEncoding(true,true,true).GetString(bytes.AsSpan(2));
            else text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch(DecoderFallbackException) when(extension!=".m3u8")
        {
            // Legacy Windows M3U/PLS commonly use the Western Windows code page.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding(1252).GetString(bytes);
        }
        var lines=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x!="").ToList();
        if(extension==".pls")
        {
            if(!lines.Any(x=>x.Equals("[playlist]",StringComparison.OrdinalIgnoreCase)))throw new IOException("Missing PLS playlist header.");
            var entries=new SortedDictionary<int,string>();int? declared=null;
            foreach(var line in lines)
            {
                var file=Regex.Match(line,@"^File(\d+)\s*=(.*)$",RegexOptions.IgnoreCase);
                if(file.Success){if(!int.TryParse(file.Groups[1].Value,out var number)||number<1||!entries.TryAdd(number,file.Groups[2].Value.Trim()))throw new IOException("Invalid/duplicate PLS entry number.");}
                if(line.StartsWith("NumberOfEntries=",StringComparison.OrdinalIgnoreCase)){if(!int.TryParse(line[(line.IndexOf('=')+1)..],out var count)||count<0)throw new IOException("Invalid PLS count.");declared=count;}
            }
            if(declared.HasValue&&declared.Value!=entries.Count)throw new IOException("PLS count does not match entries.");
            return entries.Values.ToList();
        }
        if(lines.Any(x=>x.StartsWith("#EXT-X-",StringComparison.OrdinalIgnoreCase)))throw new IOException("HLS streaming playlist is not a local music playlist.");
        return lines.Where(x=>!x.StartsWith('#')).ToList();
    }
    public static string ResolvePath(string playlist,string entry)
    {
        var value=entry.Trim().Trim('"');
        if(value.StartsWith("file:",StringComparison.OrdinalIgnoreCase))
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||!uri.IsFile)throw new IOException("Invalid file URI.");value=uri.LocalPath;
        }
        else if(Regex.IsMatch(value,@"^[A-Za-z][A-Za-z0-9+.-]+:")&&!Regex.IsMatch(value,@"^[A-Za-z]:[\\/]"))throw new IOException("Remote/unsupported URI; no network lookup performed.");
        if(string.IsNullOrWhiteSpace(value)||value.Contains('\0'))throw new IOException("Empty or invalid playlist entry.");
        if(value.Length>=2&&value[1]==':'&&!Path.IsPathFullyQualified(value))throw new IOException("Drive-relative playlist entry is ambiguous.");
        return PathSafetyGuard.Canonical(Path.Combine(System.IO.Path.GetDirectoryName(playlist)!,value.Replace('/',System.IO.Path.DirectorySeparatorChar)));
    }
}

public sealed partial class ProgressRepository
{
    private void InitializeSourcePlaylists()
    {
        db.Query("CREATE TABLE IF NOT EXISTS source_playlists (id INTEGER PRIMARY KEY AUTOINCREMENT,source_root TEXT NOT NULL COLLATE NOCASE,path TEXT NOT NULL COLLATE NOCASE,name TEXT NOT NULL,present INTEGER NOT NULL,error TEXT NOT NULL,UNIQUE(source_root,path))");
        db.Query("CREATE TABLE IF NOT EXISTS source_playlist_entries (playlist_id INTEGER NOT NULL,position INTEGER NOT NULL,original_entry TEXT NOT NULL,file_id TEXT NOT NULL,problem TEXT NOT NULL,result TEXT NOT NULL,target_path TEXT NOT NULL,PRIMARY KEY(playlist_id,position))");
    }
    public List<SourcePlaylist> SourcePlaylists()=>db.Query("SELECT id,source_root,path,name,present,error FROM source_playlists ORDER BY id").Select(x=>new SourcePlaylist(long.Parse(x[0]!),x[1]!,x[2]!,x[3]!,x[4]=="1",x[5]!)).ToList();
    public List<SourcePlaylistEntry> SourcePlaylistEntries(long id)=>db.Query("SELECT position,original_entry,file_id,problem,result,target_path FROM source_playlist_entries WHERE playlist_id=? ORDER BY position",id).Select(x=>new SourcePlaylistEntry(int.Parse(x[0]!),x[1]!,x[2]!,x[3]!,x[4]!,x[5]!)).ToList();
    public void PlaylistEntryResult(long id,int position,string result,string target)=>db.Query("UPDATE source_playlist_entries SET result=?,target_path=? WHERE playlist_id=? AND position=?",result,target,id,position);
    public void RegisterSourcePlaylists(string source,IReadOnlyList<string> paths,bool complete,Action<string>? progress)
    {
        var files=All().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();
        var current=files.ToDictionary(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase);
        var original=files.GroupBy(x=>x.OriginalPath,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.OrdinalIgnoreCase);
        db.Query("BEGIN IMMEDIATE");
        try
        {
        if(complete)db.Query("UPDATE source_playlists SET present=0 WHERE source_root=?",source);
        foreach(var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var entries=new List<(string Raw,string Id,string Problem)>();var error="";
            try
            {
                foreach(var raw in SourcePlaylistReader.Read(path))
                {
                    var id="";var problem="";
                    try
                    {
                        var resolved=SourcePlaylistReader.ResolvePath(path,raw);
                        if(current.TryGetValue(resolved,out var item))id=item.Id;
                        else if(original.TryGetValue(resolved,out var candidates)&&candidates.Count==1)id=candidates[0].Id;
                        else problem="No unique indexed source path matches this entry; no filename guessing.";
                    }
                    catch(Exception e) when(e is IOException or ArgumentException or NotSupportedException){problem=e.Message;}
                    entries.Add((raw,id,problem));
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or XmlException){error=e.Message;}
                if(db.Query("SELECT id FROM source_playlists WHERE source_root=? AND path=?",source,path).Count==0)
                    db.Query("INSERT INTO source_playlists(source_root,path,name,present,error) VALUES(?,?,?,1,?)",source,path,Path.GetFileNameWithoutExtension(path),error);
                else db.Query("UPDATE source_playlists SET present=1,error=? WHERE source_root=? AND path=?",error,source,path);
                var playlistId=long.Parse(db.Query("SELECT id FROM source_playlists WHERE source_root=? AND path=?",source,path)[0][0]!);
                // Failed parsing retains the last entry snapshot for diagnosis, but never plays that stale snapshot.
                if(error==""){db.Query("DELETE FROM source_playlist_entries WHERE playlist_id=?",playlistId);for(var i=0;i<entries.Count;i++)db.Query("INSERT INTO source_playlist_entries VALUES(?,?,?,?,?,?,?)",playlistId,i+1,entries[i].Raw,entries[i].Id,entries[i].Problem,"Pending regeneration","");}
                progress?.Invoke($"Playlist {playlistId}: {path} ({(error==""?$"{entries.Count} entries; {entries.Count(x=>x.Problem!="")} unresolved references":error)})");
        }
        db.Query("COMMIT");
        }
        catch{db.Query("ROLLBACK");throw;}
    }
}

