using System.Xml;
namespace Mp3Organizer;

public sealed partial class ProgressRepository
{
    private void InitializeSourcePlaylists()
    {
        db.Query("CREATE TABLE IF NOT EXISTS source_playlists (id INTEGER PRIMARY KEY AUTOINCREMENT,source_root TEXT NOT NULL COLLATE NOCASE,path TEXT NOT NULL COLLATE NOCASE,name TEXT NOT NULL,present INTEGER NOT NULL,error TEXT NOT NULL,UNIQUE(source_root,path))");
        db.Query("CREATE TABLE IF NOT EXISTS source_playlist_entries (playlist_id INTEGER NOT NULL,position INTEGER NOT NULL,original_entry TEXT NOT NULL,file_id TEXT NOT NULL,problem TEXT NOT NULL,result TEXT NOT NULL,target_path TEXT NOT NULL,PRIMARY KEY(playlist_id,position))");
        // Additive upgrade from the initial import schema; retain assigned IDs and entry bindings.
        AddColumn("source_playlists","original_path","TEXT NOT NULL DEFAULT ''");
        AddColumn("source_playlists","sha256","TEXT NOT NULL DEFAULT ''");
        AddColumn("source_playlists","header_json","TEXT NOT NULL DEFAULT '[]'");
        AddColumn("source_playlists","warnings_json","TEXT NOT NULL DEFAULT '[]'");
        AddColumn("source_playlists","last_generated_utc","TEXT NOT NULL DEFAULT ''");
        AddColumn("source_playlist_entries","metadata_json","TEXT NOT NULL DEFAULT '[]'");
        AddColumn("source_playlist_entries","category","TEXT NOT NULL DEFAULT 'Pending/error'");
        db.Query("UPDATE source_playlists SET original_path=path WHERE original_path=''");
        void AddColumn(string table,string name,string definition)
        {
            if(!db.Query("PRAGMA table_info("+table+")").Any(x=>x[1]==name))db.Query("ALTER TABLE "+table+" ADD COLUMN "+name+" "+definition);
        }
    }
    public List<SourcePlaylist> SourcePlaylists()=>db.Query("SELECT id,source_root,path,name,present,error,original_path,sha256,header_json,warnings_json,last_generated_utc FROM source_playlists ORDER BY id")
        .Select(x=>new SourcePlaylist(long.Parse(x[0]!),x[1]!,x[2]!,x[3]!,x[4]=="1",x[5]!){OriginalPath=x[6]!,Sha256=x[7]!,Header=JsonFormat.Read<string[]>(x[8]!),Warnings=JsonFormat.Read<string[]>(x[9]!),LastGeneratedUtc=x[10]!}).ToList();
    public List<SourcePlaylistEntry> SourcePlaylistEntries(long id)=>db.Query("SELECT position,original_entry,file_id,problem,result,target_path,metadata_json,category FROM source_playlist_entries WHERE playlist_id=? ORDER BY position",id)
        .Select(x=>new SourcePlaylistEntry(int.Parse(x[0]!),x[1]!,x[2]!,x[3]!,x[4]!,x[5]!){Metadata=JsonFormat.Read<string[]>(x[6]!),Category=x[7]!}).ToList();
    public void PlaylistEntryResults(long id,List<(int Position,string Result,string Target,string Category)> values)
    {
        db.Query("BEGIN IMMEDIATE");
        try{foreach(var value in values)db.Query("UPDATE source_playlist_entries SET result=?,target_path=?,category=? WHERE playlist_id=? AND position=?",value.Result,value.Target,value.Category,id,value.Position);db.Query("COMMIT");}
        catch{db.Query("ROLLBACK");throw;}
    }
    public void MarkPlaylistsGenerated(string source)=>db.Query("UPDATE source_playlists SET last_generated_utc=? WHERE source_root=? AND present=1 AND error=''",DateTime.UtcNow.ToString("O"),source);
    public void RegisterSourcePlaylists(string source,IReadOnlyList<string> paths,bool complete,Action<string>? progress)
    {
        var files=All().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();var fileIds=files.Select(x=>x.Id).ToHashSet();
        var current=files.ToDictionary(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase);
        var original=files.GroupBy(x=>x.OriginalPath,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.OrdinalIgnoreCase);
        var known=SourcePlaylists().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();
        var observed=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);var used=new HashSet<long>();
        var documents=new List<(string Path,ParsedPlaylist? Document,string Error)>();
        foreach(var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            try{documents.Add((path,SourcePlaylistReader.ReadDocument(path),""));}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or XmlException){documents.Add((path,null,e.Message));}
        }
        db.Query("BEGIN IMMEDIATE");
        try
        {
            if(complete)db.Query("UPDATE source_playlists SET present=0,last_generated_utc='' WHERE source_root=?",source);
            foreach(var input in documents)
            {
                var old=known.SingleOrDefault(x=>x.Path.Equals(input.Path,StringComparison.OrdinalIgnoreCase));
                if(old==null&&complete&&input.Document!=null)
                {
                    var matches=known.Where(x=>!observed.Contains(x.Path)&&!used.Contains(x.Id)&&x.Sha256==input.Document.Sha256).ToList();
                    var newMatches=documents.Count(x=>x.Document?.Sha256==input.Document.Sha256&&!known.Any(k=>k.Path.Equals(x.Path,StringComparison.OrdinalIgnoreCase)));
                    if(matches.Count==1&&newMatches==1)old=matches[0];
                }
                var previous=old==null?new List<SourcePlaylistEntry>():SourcePlaylistEntries(old.Id);var entries=new List<SourcePlaylistEntry>();
                if(input.Document!=null)
                {
                    foreach(var parsed in input.Document.Entries)
                    {
                        var position=entries.Count+1;var id="";var problem="";
                        var bound=previous.FirstOrDefault(x=>x.Position==position&&x.OriginalEntry==parsed.Path);
                        // Once bound, unchanged playlist bytes continue to refer to source identities after relocation.
                        if(old?.Sha256==input.Document.Sha256&&bound!=null&&fileIds.Contains(bound.FileId))id=bound.FileId;
                        else try
                        {
                            var resolved=SourcePlaylistReader.ResolvePath(input.Path,parsed.Path);
                            if(current.TryGetValue(resolved,out var item))id=item.Id;
                            else if(original.TryGetValue(resolved,out var candidates)&&candidates.Count==1)id=candidates[0].Id;
                            else problem="No unique indexed source path matches this entry; no filename guessing.";
                        }
                        catch(Exception e) when(e is IOException or ArgumentException or NotSupportedException){problem=e.Message;}
                        var entry=new SourcePlaylistEntry(position,parsed.Path,id,problem,"Pending regeneration",""){Metadata=parsed.Metadata};
                        if(old?.Sha256==input.Document.Sha256&&bound!=null&&bound.FileId==id&&bound.Problem==problem)entry=entry with{Result=bound.Result,TargetPath=bound.TargetPath,Category=bound.Category};
                        entries.Add(entry);
                    }
                }
                var unchanged=old!=null&&old.Present&&old.Error==input.Error&&old.Sha256==input.Document?.Sha256&&entries.Count==previous.Count&&entries.Zip(previous).All(x=>x.First.FileId==x.Second.FileId&&x.First.Problem==x.Second.Problem);
                if(old==null)
                {
                    db.Query("INSERT INTO source_playlists(source_root,path,name,present,error,original_path,sha256,header_json,warnings_json,last_generated_utc) VALUES(?,?,?,1,?,?,?,?,?,'')",source,input.Path,Path.GetFileNameWithoutExtension(input.Path),input.Error,input.Path,input.Document?.Sha256??"",JsonFormat.Serialize(input.Document?.Header??[]),JsonFormat.Serialize(input.Document?.Warnings??[]));
                }
                else
                {
                    used.Add(old.Id);
                    db.Query("UPDATE source_playlists SET path=?,present=1,error=?,sha256=?,header_json=?,warnings_json=?,last_generated_utc=? WHERE id=?",input.Path,input.Error,input.Document?.Sha256??old.Sha256,JsonFormat.Serialize(input.Document?.Header??old.Header),JsonFormat.Serialize(input.Document?.Warnings??old.Warnings),unchanged?old.LastGeneratedUtc:"",old.Id);
                }
                var playlistId=old?.Id??long.Parse(db.Query("SELECT id FROM source_playlists WHERE source_root=? AND path=?",source,input.Path)[0][0]!);
                if(input.Document!=null)
                {
                    db.Query("DELETE FROM source_playlist_entries WHERE playlist_id=?",playlistId);
                    foreach(var entry in entries)db.Query("INSERT INTO source_playlist_entries(playlist_id,position,original_entry,file_id,problem,result,target_path,metadata_json,category) VALUES(?,?,?,?,?,?,?,?,?)",playlistId,entry.Position,entry.OriginalEntry,entry.FileId,entry.Problem,entry.Result,entry.TargetPath,JsonFormat.Serialize(entry.Metadata),entry.Category);
                }
                progress?.Invoke($"PL-{playlistId:D4}: {input.Path} ({(input.Error==""?$"{entries.Count} entries; {entries.Count(x=>x.Problem!="")} unresolved references":input.Error)})");
            }
            db.Query("COMMIT");
        }
        catch{db.Query("ROLLBACK");throw;}
    }
    public void PrintPlaylistStatus(Action<string> print)
    {
        var playlists=SourcePlaylists().Where(x=>x.Present).ToList();var files=All().ToDictionary(x=>x.Id);var categories=new Dictionary<string,int>();var count=0;
        foreach(var playlist in playlists)foreach(var entry in SourcePlaylistEntries(playlist.Id))
        {
            count++;var category="Missing/unresolved";
            if(playlist.Error==""&&entry.Problem==""&&files.TryGetValue(entry.FileId,out var file))
                category=file.Status==ProcessingStatus.NeedsReview?"Awaiting review":file.Status!=ProcessingStatus.Processed?"Pending/error":playlist.LastGeneratedUtc!=""&&entry.Category=="Migrated"?"Migrated":"Missing/unresolved";
            categories[category]=categories.GetValueOrDefault(category)+1;
        }
        print($"Source playlists: {playlists.Count}\nMigrated playlists: {playlists.Count(x=>x.LastGeneratedUtc!=""&&x.Error=="")}\nPlaylist read errors: {playlists.Count(x=>x.Error!="")}\nPlaylist entries: {count}\nMigrated entries: {categories.GetValueOrDefault("Migrated")}\nAwaiting review: {categories.GetValueOrDefault("Awaiting review")}\nPending/error: {categories.GetValueOrDefault("Pending/error")}\nMissing/unresolved: {categories.GetValueOrDefault("Missing/unresolved")}");
    }
}
