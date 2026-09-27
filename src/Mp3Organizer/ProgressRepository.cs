namespace Mp3Organizer;

public enum ProcessingStatus { Discovered, Analyzed, NeedsReview, Ready, Processed, Skipped, Error }
public sealed record IndexedMusic(string Id,string SourceRoot,string OriginalPath,string CurrentPath,AudioMetadata Basic,AudioMetadata Effective,ProcessingStatus Status,string? LastProcessedUtc,string ErrorMessage);

public sealed partial class ProgressRepository : IDisposable
{
    public string DatabasePath {get;}
    private readonly SqliteDatabase db;
    public ProgressRepository(string workspace)
    {
        DatabasePath=PathSafetyGuard.Canonical(Path.Combine(workspace,"music-organizer.db"));
        foreach(var suffix in new[]{"","-wal","-shm","-journal"})PathSafetyGuard.Writable(DatabasePath+suffix);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);db=new(DatabasePath);
        try
        {
            var version=db.Query("PRAGMA user_version")[0][0];if(version is not ("0" or "1" or "2" or "3" or "4"))throw new IOException("Unsupported progress database version: "+version);
            db.Query("PRAGMA journal_mode=WAL");db.Query("PRAGMA synchronous=FULL");
            db.Query("CREATE TABLE IF NOT EXISTS music_files (id TEXT PRIMARY KEY, source_root TEXT NOT NULL, original_path TEXT NOT NULL, current_path TEXT NOT NULL COLLATE NOCASE UNIQUE, file_size INTEGER NOT NULL, last_write_time_utc TEXT NOT NULL, sha256 TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, title TEXT NOT NULL, track_number INTEGER NOT NULL, year INTEGER NOT NULL, bitrate INTEGER NOT NULL, status TEXT NOT NULL, last_processed_utc TEXT, error_message TEXT NOT NULL, basic_json TEXT NOT NULL, effective_json TEXT NOT NULL)");
            db.Query("CREATE TABLE IF NOT EXISTS processing_history (sequence INTEGER PRIMARY KEY AUTOINCREMENT, file_id TEXT NOT NULL, event_utc TEXT NOT NULL, status TEXT NOT NULL, reason TEXT NOT NULL)");
            db.Query("CREATE TABLE IF NOT EXISTS llm_events (sequence INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL, event_utc TEXT NOT NULL, kind TEXT NOT NULL, source_path TEXT NOT NULL, detail TEXT NOT NULL)");
            db.Query("CREATE TABLE IF NOT EXISTS run_targets (source_root TEXT PRIMARY KEY COLLATE NOCASE, target TEXT NOT NULL)");
            db.Query("CREATE TABLE IF NOT EXISTS recognition_evidence (file_id TEXT PRIMARY KEY, recognition_confidence REAL NOT NULL, auto_recognized INTEGER NOT NULL, year_source TEXT NOT NULL, year_confidence REAL NOT NULL, year_enriched INTEGER NOT NULL, candidates_json TEXT NOT NULL, review_reasons_json TEXT NOT NULL)");
            db.Query("CREATE INDEX IF NOT EXISTS ix_music_status ON music_files(status)");db.Query("CREATE INDEX IF NOT EXISTS ix_music_sha ON music_files(sha256)");
            db.Query("BEGIN IMMEDIATE");
            try{InitializeFolders();InitializeSourcePlaylists();db.Query("PRAGMA user_version=4");db.Query("COMMIT");}
            catch{db.Query("ROLLBACK");throw;}
        }
        catch{db.Dispose();throw;}
    }
    public List<IndexedMusic> All()=>db.Query("SELECT id,source_root,original_path,current_path,basic_json,effective_json,status,last_processed_utc,error_message FROM music_files ORDER BY rowid").Select(Read).ToList();
    public string? GetRunTarget(string source)=>db.Query("SELECT target FROM run_targets WHERE source_root=?",source).FirstOrDefault()?[0];
    public void SetRunTarget(string source,string target)=>db.Query("INSERT INTO run_targets VALUES(?,?) ON CONFLICT(source_root) DO UPDATE SET target=excluded.target",source,target);
    public void RecordLlmEvent(string runId,string kind,string path,string detail)=>db.Query("INSERT INTO llm_events(run_id,event_utc,kind,source_path,detail) VALUES(?,?,?,?,?)",runId,DateTime.UtcNow.ToString("O"),kind,path,detail);
    public LlmStatistics LlmTotals()
    {
        var counts=db.Query("SELECT kind,COUNT(*) FROM llm_events GROUP BY kind").ToDictionary(x=>x[0]!,x=>long.Parse(x[1]!,System.Globalization.CultureInfo.InvariantCulture));
        return new(counts.GetValueOrDefault("Request"),counts.GetValueOrDefault("Resolved"),counts.GetValueOrDefault("NeedsReview"),counts.GetValueOrDefault("Unavailable"),counts.GetValueOrDefault("DeterministicOnly"));
    }
    private static IndexedMusic Read(string?[] r)=>new(r[0]!,r[1]!,r[2]!,r[3]!,JsonFormat.Read<AudioMetadata>(r[4]!),JsonFormat.Read<AudioMetadata>(r[5]!),Enum.Parse<ProcessingStatus>(r[6]!),r[7],r[8]!);
    public void Save(IndexedMusic item,string reason)
    {
        db.Query("BEGIN IMMEDIATE");
        try
        {
            var m=item.Effective;
            db.Query("INSERT INTO music_files VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET source_root=excluded.source_root,current_path=excluded.current_path,file_size=excluded.file_size,last_write_time_utc=excluded.last_write_time_utc,sha256=excluded.sha256,artist=excluded.artist,album=excluded.album,title=excluded.title,track_number=excluded.track_number,year=excluded.year,bitrate=excluded.bitrate,status=excluded.status,last_processed_utc=excluded.last_processed_utc,error_message=excluded.error_message,basic_json=excluded.basic_json,effective_json=excluded.effective_json",
                item.Id,item.SourceRoot,item.OriginalPath,item.CurrentPath,item.Basic.Size,item.Basic.LastWriteTimeUtc.ToString("O"),item.Basic.Sha256,m.Artist,m.Album,m.Title,m.Track,m.Year,m.Bitrate,item.Status.ToString(),item.LastProcessedUtc,item.ErrorMessage,JsonFormat.Serialize(item.Basic),JsonFormat.Serialize(m));
            var e=m.Identification??new IdentificationEvidence();
            db.Query("INSERT INTO recognition_evidence VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(file_id) DO UPDATE SET recognition_confidence=excluded.recognition_confidence,auto_recognized=excluded.auto_recognized,year_source=excluded.year_source,year_confidence=excluded.year_confidence,year_enriched=excluded.year_enriched,candidates_json=excluded.candidates_json,review_reasons_json=excluded.review_reasons_json",item.Id,e.RecognitionConfidence,e.AutoRecognized?1:0,e.YearSource,e.YearConfidence,e.YearEnriched?1:0,JsonFormat.Serialize(e.Candidates),JsonFormat.Serialize(e.ReviewReasons));
            db.Query("INSERT INTO processing_history(file_id,event_utc,status,reason) VALUES(?,?,?,?)",item.Id,DateTime.UtcNow.ToString("O"),item.Status.ToString(),reason);
            db.Query("COMMIT");
        }
        catch{db.Query("ROLLBACK");throw;}
    }
    // State-only relocation hook for a future authorized relocation service; does not move audio.
    public void UpdateCurrentPath(string id,string path)
    {
        var item=All().Single(x=>x.Id==id);var current=PathSafetyGuard.Canonical(path);PathSafetyGuard.NoLinks(current);
        Save(item with{CurrentPath=current,Basic=At(item.Basic,current),Effective=At(item.Effective,current)},"Location updated");
    }
    public static AudioMetadata At(AudioMetadata m,string path)=>m with{FullPath=path,FileName=Path.GetFileName(path)};
    public int Reset(Func<IndexedMusic,bool> predicate)
    {
        var items=All().Where(predicate).ToList();
        // Each reset is durable; a stopped reset can safely be rerun.
        foreach(var item in items)Save(item with{Status=ProcessingStatus.Discovered,LastProcessedUtc=null,ErrorMessage=""},"Progress reset");
        return items.Count;
    }
    public string Backup()
    {
        var folder=Path.Combine(Path.GetDirectoryName(DatabasePath)!,"backups");PathSafetyGuard.Writable(folder);Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"music-organizer-"+DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N")+".db");PathSafetyGuard.Writable(path);
        db.Query("VACUUM INTO ?",path);return path;
    }
    public void Clear()
    {
        db.Query("BEGIN IMMEDIATE");try{db.Query("DELETE FROM recognition_evidence");db.Query("DELETE FROM music_files");db.Query("DELETE FROM processing_history");db.Query("COMMIT");}catch{db.Query("ROLLBACK");throw;}
    }
    public void Dispose()=>db.Dispose();
}
