using System.Net.Http;

namespace Mp3Organizer;

public sealed class IdentificationSession : IDisposable
{
    private readonly HttpClient http;
    public IMetadataResolver Resolver { get; }
    public ManualResolutionStore? ManualStore {get;}
    private static readonly ILookupClock Clock = new LookupClock();
    private static readonly RequestRateLimiter AcoustLimit = new(TimeSpan.FromMilliseconds(350), Clock);
    private static readonly RequestRateLimiter BrainzLimit = new(TimeSpan.FromMilliseconds(1100), Clock);
    public IdentificationSession(string source, string cachePath, string? target, bool online, bool identifyAll,string? workspace=null,bool rebuildFingerprints=false,bool refreshIdentification=false,int negativeRetryDays=30,Action<string>? progress=null)
    {
        var configuration=IdentificationConfiguration.Read();
        var key = configuration.ApiKey;
        var contact = configuration.Contact;
        if (online && configuration.Problem!=null) throw new ArgumentException(configuration.Status(true,false));
        var agent = "Mp3Organizer/1.1 (" + (online ? contact : "offline") + ")";
        if (contact.Contains('\r') || contact.Contains('\n')) throw new ArgumentException("Invalid contact value.");
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
        var cache = new IdentificationCache(cachePath, source, target){RebuildFingerprints=rebuildFingerprints,RefreshIdentification=refreshIdentification,NegativeRetryAge=TimeSpan.FromDays(negativeRetryDays)};
        Resolver = new CachedMetadataResolver(new MetadataResolver(new ChromaprintFingerprintService(configuration.Fpcalc ?? "fpcalc", cache),
            new AcoustIdClient(new IdentificationHttp(http,cache,AcoustLimit,Clock),key),
            new MusicBrainzClient(new IdentificationHttp(http,cache,BrainzLimit,Clock),agent), online, online || identifyAll||rebuildFingerprints,progress),cache,online);
        if(workspace!=null){ManualStore=new(workspace,source,target);Resolver=new ManualMetadataResolver(Resolver,ManualStore,refreshIdentification);}
        Resolver=new YearEnrichmentResolver(Resolver,new AlbumYearClient(new IdentificationHttp(http,cache,BrainzLimit,Clock),cache,agent),online,progress);
        List<IndexedMusic> persisted=[];
        if(workspace!=null&&File.Exists(Path.Combine(workspace,"music-organizer.db"))){using var repository=new ProgressRepository(workspace);persisted=repository.All();}
        Resolver=new PolicyInputResolver(Resolver,persisted);
    }
    public static List<AudioMetadata> ResolveAll(IReadOnlyList<AudioMetadata> rows, IMetadataResolver resolver, bool identifyAll, Action<string>? progress = null)
    {
        if(resolver is IInventoryAwareMetadataResolver aware)aware.SetInventory(rows);
        var folders = rows.GroupBy(x => Path.GetDirectoryName(x.FullPath) ?? "",StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key,x => (IReadOnlyList<AudioMetadata>)x.ToList(),StringComparer.OrdinalIgnoreCase);
        var result = new List<AudioMetadata>();
        foreach (var row in rows)
        {
            var label=$"[{result.Count+1}/{rows.Count}] {row.FileName}";
            progress?.Invoke("Identifying "+label);
            var started=System.Diagnostics.Stopwatch.StartNew();
            var task=Task.Run(()=>resolver.ResolveAsync(row, folders[Path.GetDirectoryName(row.FullPath) ?? ""],identifyAll));
            if(progress!=null)
                while(!Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult().Equals(task))
                    progress("Still identifying "+label+$" ({started.Elapsed.TotalSeconds:F0}s; fingerprint/cache/API work may include rate-limit waits)");
            var resolved=task.GetAwaiter().GetResult();result.Add(resolved);
            var evidence=resolved.Identification;
            progress?.Invoke($"Completed {label}: {evidence?.Status??"Unknown"}; source={evidence?.Source}; fingerprint cache={evidence?.FingerprintCacheHit==true}; identification cache={evidence?.IdentificationCacheHit==true}; manual={evidence?.ManualOverrideHit==true}");
        }
        progress?.Invoke($"Identification complete: {result.Count}/{rows.Count}; {result.Count(x=>x.Identification?.Status=="Review")} need review.");
        return result;
    }
    public void Dispose() => http.Dispose();
}
