using System.Text;
using System.Text.RegularExpressions;

namespace Mp3Organizer;

public sealed record SourceFolder(long Id,string SourceRoot,string OriginalPath,string CurrentPath,string Name)
{
    public string Code=>"FOLDER-"+Id.ToString("D4",System.Globalization.CultureInfo.InvariantCulture);
    public string PlaylistPath=>Path.Combine("_Playlists","Folders",$"[{Code}] {TargetPathBuilder.SafeName(Name)}.m3u8");
}
public sealed record SourceOccurrence(string Id,long FolderId,string FileId,string OriginalPath,string CurrentPath,bool Present);
public sealed record CanonicalMapping(string FileId,string Target,string RelativePath,string SourceHash,string TargetHash,long Size);

public sealed class NaturalPathComparer:IComparer<string>
{
    public static readonly NaturalPathComparer Instance=new();
    public int Compare(string? left,string? right)
    {
        var a=Regex.Matches(left??"",@"\d+|\D+").Select(x=>x.Value).ToArray();var b=Regex.Matches(right??"",@"\d+|\D+").Select(x=>x.Value).ToArray();
        for(var i=0;i<Math.Min(a.Length,b.Length);i++)
        {
            int c;
            if(char.IsDigit(a[i][0])&&char.IsDigit(b[i][0]))
            {
                var x=a[i].TrimStart('0');var y=b[i].TrimStart('0');c=x.Length.CompareTo(y.Length);if(c==0)c=StringComparer.Ordinal.Compare(x,y);
            }
            else c=StringComparer.OrdinalIgnoreCase.Compare(a[i],b[i]);
            if(c!=0)return c;
        }
        var length=a.Length.CompareTo(b.Length);return length!=0?length:StringComparer.Ordinal.Compare(left,right);
    }
}

public sealed partial class ProgressRepository
{
    private void InitializeFolders()
    {
        db.Query("CREATE TABLE IF NOT EXISTS source_folders (id INTEGER PRIMARY KEY AUTOINCREMENT, source_root TEXT NOT NULL COLLATE NOCASE, original_path TEXT NOT NULL, current_path TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, UNIQUE(source_root,current_path))");
        db.Query("CREATE TABLE IF NOT EXISTS source_occurrences (id TEXT PRIMARY KEY,folder_id INTEGER NOT NULL,file_id TEXT NOT NULL,original_path TEXT NOT NULL,current_path TEXT NOT NULL COLLATE NOCASE,present INTEGER NOT NULL,UNIQUE(folder_id,current_path))");
        db.Query("CREATE TABLE IF NOT EXISTS folder_order (folder_id INTEGER NOT NULL,position INTEGER NOT NULL,occurrence_id TEXT NOT NULL,PRIMARY KEY(folder_id,position))");
        db.Query("CREATE TABLE IF NOT EXISTS canonical_mappings (file_id TEXT NOT NULL,target TEXT NOT NULL COLLATE NOCASE,relative_path TEXT NOT NULL,source_hash TEXT NOT NULL,target_hash TEXT NOT NULL,size INTEGER NOT NULL,PRIMARY KEY(file_id,target))");
        db.Query("CREATE TABLE IF NOT EXISTS metadata_policy_audit (file_id TEXT NOT NULL,policy INTEGER NOT NULL,decision TEXT NOT NULL,previous_json TEXT NOT NULL,event_utc TEXT NOT NULL,PRIMARY KEY(file_id,policy))");
    }
    public List<SourceFolder> Folders()=>db.Query("SELECT id,source_root,original_path,current_path,name FROM source_folders ORDER BY id").Select(x=>new SourceFolder(long.Parse(x[0]!),x[1]!,x[2]!,x[3]!,x[4]!)).ToList();
    public List<SourceOccurrence> Occurrences()=>db.Query("SELECT id,folder_id,file_id,original_path,current_path,present FROM source_occurrences").Select(x=>new SourceOccurrence(x[0]!,long.Parse(x[1]!),x[2]!,x[3]!,x[4]!,x[5]=="1")).ToList();
    public List<string> FolderOrder(long id)=>db.Query("SELECT occurrence_id FROM folder_order WHERE folder_id=? ORDER BY position",id).Select(x=>x[0]!).ToList();
    public List<CanonicalMapping> Mappings(string target)=>db.Query("SELECT file_id,target,relative_path,source_hash,target_hash,size FROM canonical_mappings WHERE target=?",target).Select(x=>new CanonicalMapping(x[0]!,x[1]!,x[2]!,x[3]!,x[4]!,long.Parse(x[5]!))).ToList();
    public void MapCanonical(IndexedMusic item,string target,string relative,AudioMetadata verified)=>db.Query("INSERT INTO canonical_mappings VALUES(?,?,?,?,?,?) ON CONFLICT(file_id,target) DO UPDATE SET relative_path=excluded.relative_path,source_hash=excluded.source_hash,target_hash=excluded.target_hash,size=excluded.size",item.Id,target,relative,item.Basic.Sha256,verified.Sha256,verified.Size);
    public void AuditPolicy(IndexedMusic item,string decision)=>db.Query("INSERT OR IGNORE INTO metadata_policy_audit VALUES(?,?,?,?,?)",item.Id,2,decision,JsonFormat.Serialize(item),DateTime.UtcNow.ToString("O"));
    public void RegisterFolders(string source,IReadOnlyCollection<string>? observed=null,bool complete=false,Action<string>? progress=null)
    {
        var rows=All().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();
        var observedSet=observed?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        db.Query("BEGIN IMMEDIATE");
        try
        {
            if(complete)db.Query("UPDATE source_occurrences SET present=0 WHERE folder_id IN (SELECT id FROM source_folders WHERE source_root=?)",source);
            foreach(var group in rows.Where(x=>observedSet==null||observedSet.Contains(x.CurrentPath)).GroupBy(x=>Path.GetDirectoryName(x.CurrentPath)!,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase))
            {
                // Explicit existence check avoids consuming AUTOINCREMENT IDs on routine scans.
                if(db.Query("SELECT id FROM source_folders WHERE source_root=? AND current_path=?",source,group.Key).Count==0)
                    db.Query("INSERT INTO source_folders(source_root,original_path,current_path,name) VALUES(?,?,?,?)",source,group.Key,group.Key,Path.GetFileName(group.Key));
                var id=long.Parse(db.Query("SELECT id FROM source_folders WHERE source_root=? AND current_path=?",source,group.Key)[0][0]!);
                foreach(var item in group)
                {
                    if(observed==null&&db.Query("SELECT id FROM source_occurrences WHERE folder_id=? AND current_path=?",id,item.CurrentPath).Count>0)continue;
                    db.Query("INSERT INTO source_occurrences VALUES(?,?,?,?,?,1) ON CONFLICT(folder_id,current_path) DO UPDATE SET file_id=excluded.file_id,present=1",Guid.NewGuid().ToString("N"),id,item.Id,item.OriginalPath,item.CurrentPath);
                }
            }
            db.Query("COMMIT");
        }
        catch{db.Query("ROLLBACK");throw;}
        foreach(var folder in Folders().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)))
        {
            var members=Occurrences().Where(x=>x.FolderId==folder.Id&&x.Present).OrderBy(x=>x.CurrentPath,NaturalPathComparer.Instance).ToList();
            var order=members.Select(x=>x.Id).ToList();
            // Only a real scan reads source playlists. Bootstrap from an old index is database-only.
            if(observed!=null)order=ReadOrder(folder,members,progress);
            if(observed==null&&FolderOrder(folder.Id).Count>0)continue;
            db.Query("BEGIN IMMEDIATE");try{db.Query("DELETE FROM folder_order WHERE folder_id=?",folder.Id);for(var i=0;i<order.Count;i++)db.Query("INSERT INTO folder_order VALUES(?,?,?)",folder.Id,i,order[i]);db.Query("COMMIT");}catch{db.Query("ROLLBACK");throw;}
        }
    }
    private static List<string> ReadOrder(SourceFolder folder,List<SourceOccurrence> members,Action<string>? progress)
    {
        var natural=members.Select(x=>x.Id).ToList();
        if(!Directory.Exists(folder.CurrentPath))return natural;
        try
        {
            PathSafetyGuard.NoLinks(folder.CurrentPath);
            var playlists=Directory.EnumerateFiles(folder.CurrentPath).Where(x=>Path.GetExtension(x).Equals(".m3u8",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(x).Equals(".m3u",StringComparison.OrdinalIgnoreCase)).ToList();
            if(playlists.Count==0)return natural;
            if(playlists.Count!=1)throw new IOException("Multiple order playlists; using natural order.");
            var path=playlists[0];PathSafetyGuard.NoLinks(path);if(new FileInfo(path).Length>1024*1024)throw new IOException("Order playlist exceeds 1 MiB.");
            var lookup=members.ToDictionary(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase);var order=new List<string>();
            foreach(var entry in File.ReadAllLines(path,new UTF8Encoding(false,true)).Select(x=>x.Trim()).Where(x=>x!=""&&!x.StartsWith('#')))
            {
                if(entry.Contains("://",StringComparison.Ordinal))throw new IOException("Remote playlist entry.");
                var full=PathSafetyGuard.Canonical(Path.Combine(folder.CurrentPath,entry));
                if(!lookup.TryGetValue(full,out var member)||!string.Equals(Path.GetDirectoryName(full),folder.CurrentPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Playlist entry does not match a direct audio member.");
                order.Add(member.Id);
            }
            if(order.Count==0)throw new IOException("Empty order playlist.");
            var included=order.ToHashSet();order.AddRange(natural.Where(x=>!included.Contains(x)));return order;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
        {progress?.Invoke("Folder order: "+folder.CurrentPath+": "+e.Message+" Natural filename order used.");return natural;}
    }
}

