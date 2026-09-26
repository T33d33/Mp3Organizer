using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mp3Organizer;

public sealed class IdentificationCache
{
    private readonly string source;
    private readonly ILookupClock clock;
    public string DatabasePath {get;}
    public bool RebuildFingerprints {get;init;}
    public bool RefreshIdentification {get;init;}
    public TimeSpan NegativeRetryAge {get;init;}=TimeSpan.FromDays(30);
    private readonly HashSet<string> refreshed=new(StringComparer.Ordinal);
    private readonly AsyncLocal<bool> retryExpired=new();
    public IdentificationCache(string root,string source,string? target=null,ILookupClock? clock=null)
    {
        PathSafetyGuard.Separate(source,root);if(target!=null)PathSafetyGuard.Separate(target,root);
        DatabasePath=PathSafetyGuard.Canonical(Path.GetExtension(root).Equals(".db",StringComparison.OrdinalIgnoreCase)?root:Path.Combine(root,"cache.db"));
        this.source=source;this.clock=clock??new LookupClock();
    }
    private SqliteDatabase Open()
    {
        foreach(var suffix in new[]{"","-wal","-shm","-journal"})PathSafetyGuard.Writable(DatabasePath+suffix,source);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var db=new SqliteDatabase(DatabasePath);
        try
        {
            db.Query("PRAGMA journal_mode=WAL");db.Query("PRAGMA synchronous=FULL");
            db.Query("CREATE TABLE IF NOT EXISTS cache_entries (cache_key TEXT PRIMARY KEY, kind TEXT NOT NULL, status TEXT NOT NULL, payload TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, retry_after_utc TEXT)");
            db.Query("CREATE TABLE IF NOT EXISTS source_files (stable_identity TEXT NOT NULL, source_path TEXT NOT NULL, size_bytes INTEGER NOT NULL, last_write_utc TEXT NOT NULL, sha256 TEXT NOT NULL, duration REAL NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, PRIMARY KEY(stable_identity,source_path))");
            db.Query("CREATE TABLE IF NOT EXISTS fingerprints (sha256 TEXT PRIMARY KEY, duration REAL NOT NULL, fingerprint TEXT NOT NULL, algorithm TEXT NOT NULL, generator_version TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL)");
            db.Query("CREATE TABLE IF NOT EXISTS identifications (cache_key TEXT PRIMARY KEY, stable_identity TEXT NOT NULL, lookup_status TEXT NOT NULL, acoustid_score REAL, acoustid TEXT, recording_id TEXT, release_id TEXT, artist TEXT, album_artist TEXT, album TEXT, title TEXT, track_number INTEGER, disc_number INTEGER, year INTEGER, evidence TEXT, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, retry_after_utc TEXT)");
            var version=db.Query("PRAGMA user_version")[0][0];if(version is not ("0" or "1"))throw new IOException("Unsupported SQLite cache schema "+version);
            db.Query("PRAGMA user_version=1");return db;
        }
        catch {db.Dispose();throw;}
    }
    private static string Key(string key)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public void CheckWritable()
    {
        using var db=Open();db.Query("BEGIN IMMEDIATE");
        try{db.Query("INSERT INTO cache_entries VALUES(?,?,?,?,?,?,?)",Guid.NewGuid().ToString("N"),"Diagnostic","Probe","",Now,Now,null);}
        finally{db.Query("ROLLBACK");}
    }
    private string Now=>clock.Now.ToString("O",CultureInfo.InvariantCulture);
    public string? Get(string key,bool allowExpired=false)
    {
        if(!File.Exists(DatabasePath))return ImportLegacy(key);
        using var db=Open();var rows=db.Query("SELECT payload,retry_after_utc FROM cache_entries WHERE cache_key=?",Key(key));
        if(rows.Count==0)return ImportLegacy(key);
        // Expired negatives and failures are never silently reused, even when offline.
        return rows[0][1]==null || DateTimeOffset.Parse(rows[0][1]!,CultureInfo.InvariantCulture)>clock.Now?rows[0][0]:null;
    }
    private sealed record LegacyEntry(DateTimeOffset Expires,string Value);
    private string? ImportLegacy(string key)
    {
        var path=PathSafetyGuard.Destination(Path.GetDirectoryName(DatabasePath)!,Key(key)+".json",source);
        if(!File.Exists(path))return null;
        var entry=JsonFormat.Read<LegacyEntry>(File.ReadAllText(path));if(entry.Expires<=clock.Now)return null;
        Put(key,entry.Value,key.StartsWith("chromaprint-")?TimeSpan.MaxValue:entry.Expires-clock.Now,"Legacy");return entry.Value;
    }
    public string? Status(string key)
    {
        if(!File.Exists(DatabasePath))return null;using var db=Open();return db.Query("SELECT status FROM cache_entries WHERE cache_key=?",Key(key)).FirstOrDefault()?[0];
    }
    public bool ShouldRebuild(string key)=>RebuildFingerprints&&!refreshed.Contains(key);
    public string? External(string key)=> (RefreshIdentification||retryExpired.Value)&&!refreshed.Contains(key)?null:Get(key);
    public IDisposable RefreshScope(bool refresh)
    {
        var previous=retryExpired.Value;retryExpired.Value=refresh||previous;return new Scope(()=>retryExpired.Value=previous);
    }
    private sealed class Scope(Action end):IDisposable {public void Dispose()=>end();}
    public void Observe(AudioMetadata file)
    {
        if(file.Sha256.Length!=64)return;
        using var db=Open();db.Query("INSERT INTO source_files VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(stable_identity,source_path) DO UPDATE SET size_bytes=excluded.size_bytes,last_write_utc=excluded.last_write_utc,duration=excluded.duration,updated_utc=excluded.updated_utc",
            "sha256:"+file.Sha256,file.FullPath,file.Size,file.LastWriteTimeUtc.ToString("O",CultureInfo.InvariantCulture),file.Sha256,file.DurationSeconds,Now,Now);
    }
    public void Put(string key,string value,TimeSpan lifetime,string? status=null)
    {
        var kind=key.StartsWith("chromaprint-")?"Fingerprint":key.StartsWith("http-")?"ExternalApi":"AutomaticIdentification";
        status??="Success";
        var expires=lifetime==TimeSpan.MaxValue?null:clock.Now.Add(lifetime).ToString("O",CultureInfo.InvariantCulture);
        using var db=Open();db.Query("BEGIN IMMEDIATE");
        try
        {
            db.Query("INSERT INTO cache_entries VALUES(?,?,?,?,?,?,?) ON CONFLICT(cache_key) DO UPDATE SET kind=excluded.kind,status=excluded.status,payload=excluded.payload,updated_utc=excluded.updated_utc,retry_after_utc=excluded.retry_after_utc",Key(key),kind,status,value,Now,Now,expires);
            if(kind=="Fingerprint")
            {
                var fp=JsonFormat.Read<AudioFingerprint>(value);var sha=key[(key.LastIndexOf(':')+1)..];
                db.Query("INSERT INTO fingerprints VALUES(?,?,?,?,?,?,?) ON CONFLICT(sha256) DO UPDATE SET duration=excluded.duration,fingerprint=excluded.fingerprint,algorithm=excluded.algorithm,generator_version=excluded.generator_version,updated_utc=excluded.updated_utc",sha,fp.Duration,fp.Fingerprint,fp.Algorithm,fp.GeneratorVersion,Now,Now);
            }
            if(kind=="AutomaticIdentification")
            {
                var row=JsonFormat.Read<AudioMetadata>(value);var e=row.Identification;
                db.Query("INSERT INTO identifications VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(cache_key) DO UPDATE SET lookup_status=excluded.lookup_status,acoustid_score=excluded.acoustid_score,acoustid=excluded.acoustid,recording_id=excluded.recording_id,release_id=excluded.release_id,artist=excluded.artist,album_artist=excluded.album_artist,album=excluded.album,title=excluded.title,track_number=excluded.track_number,disc_number=excluded.disc_number,year=excluded.year,evidence=excluded.evidence,updated_utc=excluded.updated_utc,retry_after_utc=excluded.retry_after_utc",Key(key),"sha256:"+row.Sha256,status,e?.AcoustIdScore,e?.AcoustId,e?.RecordingId,e?.ReleaseId,row.Artist,row.AlbumArtist,row.Album,row.Title,row.Track,row.Disc,row.Year,e?.Evidence,Now,Now,expires);
            }
            db.Query("COMMIT");refreshed.Add(key);
        }
        catch {db.Query("ROLLBACK");throw;}
    }
}
