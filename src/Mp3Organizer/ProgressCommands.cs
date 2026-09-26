namespace Mp3Organizer;

public static class ProgressCommands
{
    public static bool Handles(string[] args)=>args.Length>0&&(args[0] is "review" or "scan" or "status" or "reset-file" or "reset-folder" or "reset-errors" or "reset-review" or "reset-progress" or "reset-all"||args[0]=="analyze"&&args.Contains("--limit"));
    public static int Run(string[] args)
    {
        var command=args[0];var workspace=Path.GetFullPath(Environment.GetEnvironmentVariable("MP3ORGANIZER_WORKSPACE")??"workspace");
        var paths=new List<string>();var force=false;var writeTags=false;var online=false;var offline=false;var limit=0;string? reports=null;
        for(var i=1;i<args.Length;i++)switch(args[i])
        {
            case "--workspace" when i+1<args.Length:workspace=Path.GetFullPath(args[++i]);break;
            case "--reports" when i+1<args.Length:reports=Path.GetFullPath(args[++i]);break;
            case "--limit" when i+1<args.Length:if(!int.TryParse(args[++i],out limit)||limit<1)throw new ArgumentException("--limit must be a positive integer.");break;
            case "--write-tags":writeTags=true;break;
            case "--force":force=true;break;
            case "--online":online=true;break;
            case "--offline":offline=true;break;
            default:if(args[i].StartsWith("--"))throw new ArgumentException("Unknown or incomplete progress option: "+args[i]);paths.Add(args[i]);break;
        }
        var expected=command is "scan" or "reset-file" or "reset-folder"?1:0;
        if(paths.Count!=expected)throw new ArgumentException("Incorrect arguments. Incremental analyze uses analyze --limit N without a source path; run scan first.");
        if(force&&command!="scan"||limit>0&&command!="analyze"||(online||offline||reports!=null)&&command!="analyze"||online&&offline)throw new ArgumentException("Options do not apply to this command.");
        if(writeTags&&command is not ("analyze" or "review"))throw new ArgumentException("--write-tags applies to analyze --limit and review.");
        if(command=="analyze"&&limit==0)throw new ArgumentException("Incremental analysis requires --limit N.");
        if(command=="scan")PathSafetyGuard.Separate(paths[0],workspace);
        if(command=="analyze")
        {
            var configuration=IdentificationConfiguration.Read();Console.WriteLine(configuration.Status(online,offline));if(online&&configuration.Problem!=null)return 1;
        }
        // Single writer across all progress commands, including reset-all's backup/confirmation window.
        var lockPath=Path.Combine(workspace,"music-organizer.lock");PathSafetyGuard.Writable(lockPath);Directory.CreateDirectory(workspace);
        using var gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var repository=new ProgressRepository(workspace);
        foreach(var source in repository.All().Select(x=>x.SourceRoot).Distinct(StringComparer.OrdinalIgnoreCase))PathSafetyGuard.Separate(source,workspace);
        var tagWriter=new Mp3TagWriteService(workspace,repository);tagWriter.Recover();
        if(command=="review")
        {
            if(Console.IsInputRedirected)throw new IOException("review requires an interactive terminal.");
            Console.WriteLine("Reviewed: "+new ReviewService(repository,workspace,new ReviewConsole(),tags:writeTags?tagWriter:null).Run());return 0;
        }
        if(command=="scan")
        {
            var result=new IncrementalProgressService(repository).Scan(paths[0],force,Console.WriteLine);
            Console.WriteLine($"Added: {result.Added}\nUpdated: {result.Updated}\nUnchanged: {result.Unchanged}\nScan errors: {result.Errors.Count}");foreach(var error in result.Errors)Console.Error.WriteLine(error);
            return result.Errors.Count==0?0:3;
        }
        if(command=="status"){PrintStatus(repository.All());return 0;}
        if(command.StartsWith("reset-",StringComparison.Ordinal))
        {
            var service=new ProgressResetService(repository);
            if(command=="reset-all")return service.ResetAll(Console.ReadLine,Console.WriteLine)?0:2;
            Console.WriteLine("Reset files: "+service.Reset(command,paths.FirstOrDefault()));return 0;
        }
        var sessions=new Dictionary<string,IdentificationSession>(StringComparer.OrdinalIgnoreCase);
        var progress=new ConsoleProgress();
        try
        {
            // Fail on invalid authoritative manual JSON before marking individual files as errors.
            foreach(var source in repository.All().Select(x=>x.SourceRoot).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                new ManualResolutionStore(workspace,source).Load();if(reports!=null)PathSafetyGuard.Separate(source,reports);
            }
            IMetadataResolver Resolver(string source)
            {
                if(!sessions.TryGetValue(source,out var session))sessions[source]=session=new(source,Path.Combine(workspace,"cache.db"),null,online,false,workspace,progress:progress.Write);
                return session.Resolver;
            }
            var analyzer=new IncrementalProgressService(repository,tagWriter:writeTags?tagWriter:null);
            var count=analyzer.AnalyzeAsync(limit,Resolver,progress.Write).GetAwaiter().GetResult();
            Console.WriteLine("Analyzed this batch: "+count);var all=repository.All();PrintStatus(all);
            if(reports!=null)
                foreach(var group in all.GroupBy(x=>x.SourceRoot,StringComparer.OrdinalIgnoreCase))
                    new IdentificationReportWriter().Write(Path.Combine(reports,"progress-"+Guid.NewGuid().ToString("N")),group.Key,group.Select(x=>x.Effective));
            return analyzer.LastErrorCount==0?0:3;
        }
        finally{foreach(var session in sessions.Values)session.Dispose();}
    }
    private static void PrintStatus(IReadOnlyList<IndexedMusic> rows)
    {
        var done=rows.Count(x=>x.Status is ProcessingStatus.Ready or ProcessingStatus.Processed or ProcessingStatus.Skipped);
        Console.WriteLine($"Total files: {rows.Count}\nProcessed: {rows.Count(x=>x.Status==ProcessingStatus.Processed)}\nReady: {rows.Count(x=>x.Status==ProcessingStatus.Ready)}\nSkipped: {rows.Count(x=>x.Status==ProcessingStatus.Skipped)}\nNeeds review: {rows.Count(x=>x.Status==ProcessingStatus.NeedsReview)}\nNot processed: {rows.Count(x=>x.Status is ProcessingStatus.Discovered or ProcessingStatus.Analyzed)}\nErrors: {rows.Count(x=>x.Status==ProcessingStatus.Error)}\nComplete (Ready/Processed/Skipped): {(rows.Count==0?0:100.0*done/rows.Count):F1}%");
        Console.WriteLine($"Auto recognized: {rows.Count(x=>x.Effective.Identification?.AutoRecognized==true)}\nYear enriched: {rows.Count(x=>x.Effective.Identification?.YearEnriched==true)}\nNo match: {rows.Count(x=>x.Effective.Identification?.ReviewReasons.Contains("NoFingerprintMatch")==true)}");
        foreach(var group in rows.Where(x=>x.Status==ProcessingStatus.NeedsReview).SelectMany(x=>x.Effective.Identification?.ReviewReasons??[]).GroupBy(x=>x).OrderBy(x=>x.Key))Console.WriteLine($"{group.Key}: {group.Count()}");
        Console.WriteLine($"Missing/unreliable Artist: {rows.Count(x=>MetadataQualityEvaluator.Invalid(x.Effective.Artist))}\nMissing/unreliable Album: {rows.Count(x=>MetadataQualityEvaluator.Invalid(x.Effective.Album))}\nMissing Track Number: {rows.Count(x=>x.Effective.Track==0)}\nMissing Year: {rows.Count(x=>x.Effective.Year==0)}");
    }
}
