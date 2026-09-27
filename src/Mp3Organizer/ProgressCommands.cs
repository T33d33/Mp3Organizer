using System.Net.Http;
namespace Mp3Organizer;

public static class ProgressCommands
{
    public static bool Handles(string[] args)=>args.Length>0&&(args[0] is "run" or "review" or "scan" or "status" or "reset-file" or "reset-folder" or "reset-errors" or "reset-review" or "reset-progress" or "reset-all"||args[0]=="analyze"&&args.Contains("--limit"));
    public static int Run(string[] args)
    {
        var command=args[0];var workspace=Path.GetFullPath(Environment.GetEnvironmentVariable("MP3ORGANIZER_WORKSPACE")??"workspace");
        var paths=new List<string>();var force=false;var writeTags=false;var online=false;var offline=false;var limit=0;string? reports=null;string? target=null;bool? codex=null;
        for(var i=1;i<args.Length;i++)switch(args[i])
        {
            case "--workspace" when i+1<args.Length:workspace=Path.GetFullPath(args[++i]);break;
            case "--reports" when i+1<args.Length:reports=Path.GetFullPath(args[++i]);break;
            case "--limit" when i+1<args.Length:if(!int.TryParse(args[++i],out limit)||limit<1)throw new ArgumentException("--limit must be a positive integer.");break;
            case "--target" when i+1<args.Length:target=Path.GetFullPath(args[++i]);break;
            case "--write-tags":writeTags=true;break;
            case "--codex":if(codex==false)throw new ArgumentException("Choose --codex or --no-codex.");codex=true;break;
            case "--no-codex":if(codex==true)throw new ArgumentException("Choose --codex or --no-codex.");codex=false;break;
            case "--force":force=true;break;
            case "--online":online=true;break;
            case "--offline":offline=true;break;
            default:if(args[i].StartsWith("--"))throw new ArgumentException("Unknown or incomplete progress option: "+args[i]);paths.Add(args[i]);break;
        }
        var expected=command is "scan" or "reset-file" or "reset-folder"?1:0;
        if(paths.Count!=expected)throw new ArgumentException("Incorrect arguments. Incremental analyze uses analyze --limit N without a source path; run scan first.");
        if(force&&command!="scan"||limit>0&&command is not ("analyze" or "run")||(online||offline||reports!=null)&&command is not ("analyze" or "run")||online&&offline)throw new ArgumentException("Options do not apply to this command.");
        if(writeTags&&command is not ("analyze" or "review" or "run"))throw new ArgumentException("--write-tags applies to analyze --limit and review.");
        if(codex.HasValue&&command is not ("analyze" or "run")||codex==true&&offline)throw new ArgumentException("Codex applies to incremental analyze and cannot be combined with --offline.");
        if(command=="analyze"&&limit==0)throw new ArgumentException("Incremental analysis requires --limit N.");
        if(target!=null&&command!="run")throw new ArgumentException("--target applies only to run.");
        if(command=="run"&&limit==0)limit=int.MaxValue;
        if(command=="scan")PathSafetyGuard.Separate(paths[0],workspace);
        if(command is "analyze" or "run")
        {
            var configuration=IdentificationConfiguration.Read();Console.WriteLine(configuration.Status(online,offline));if(online&&configuration.Problem!=null)return 1;
        }
        // Single writer across all progress commands, including reset-all's backup/confirmation window.
        var lockPath=Path.Combine(workspace,"music-organizer.lock");PathSafetyGuard.Writable(lockPath);Directory.CreateDirectory(workspace);
        using var gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var repository=new ProgressRepository(workspace);
        foreach(var source in repository.All().Select(x=>x.SourceRoot).Distinct(StringComparer.OrdinalIgnoreCase))PathSafetyGuard.Separate(source,workspace);
        var tagWriter=new Mp3TagWriteService(workspace,repository);tagWriter.Recover();
        MetadataPolicy.Migrate(repository,workspace,Console.WriteLine);
        if(command=="review")
        {
            if(Console.IsInputRedirected)throw new IOException("review requires an interactive terminal.");
            var review=new CandidateReviewService(repository,workspace,new ReviewConsole(),tags:writeTags?tagWriter:null,albumFirst:true);
            review.Run();
            if(review.ManuallyReviewedCount>0)Console.WriteLine("Manually reviewed this session: "+review.ManuallyReviewedCount);
            if(review.AutomaticallyResolvedCount>0)Console.WriteLine("Automatically resolved this session: "+review.AutomaticallyResolvedCount);
            if(review.SkippedCount>0)Console.WriteLine("Skipped this session: "+review.SkippedCount);
            Console.WriteLine("Pending Review: "+repository.All().Count(x=>x.Status==ProcessingStatus.NeedsReview));return 0;
        }
        if(command=="scan")
        {
            var result=new IncrementalProgressService(repository).Scan(paths[0],force,Console.WriteLine);
            Console.WriteLine($"Added: {result.Added}\nUpdated: {result.Updated}\nUnchanged: {result.Unchanged}\nScan errors: {result.Errors.Count}");foreach(var error in result.Errors)Console.Error.WriteLine(error);
            return result.Errors.Count==0?0:3;
        }
        if(command=="status")
        {
            PrintStatus(repository.All());
            Console.WriteLine($"Source folders: {repository.Folders().Count}\nSource occurrences: {repository.Occurrences().Count(x=>x.Present)}");
            repository.PrintPlaylistStatus(Console.WriteLine);
            Console.WriteLine("Cumulative LLM activity (since observability was enabled):");repository.LlmTotals().Print(Console.WriteLine);return 0;
        }
        if(command.StartsWith("reset-",StringComparison.Ordinal))
        {
            var service=new ProgressResetService(repository);
            if(command=="reset-all")return service.ResetAll(Console.ReadLine,Console.WriteLine)?0:2;
            Console.WriteLine("Reset files: "+service.Reset(command,paths.FirstOrDefault()));return 0;
        }
        var pipeline=command=="run"?new RunPipelineService(repository,workspace):null;
        var targets=pipeline?.Targets(target);
        if(pipeline!=null){reports??=Path.Combine(workspace,"reports");foreach(var destination in targets!.Values)PathSafetyGuard.Separate(destination,reports);}
        var sessions=new Dictionary<string,IdentificationSession>(StringComparer.OrdinalIgnoreCase);
        var resolvers=new Dictionary<string,IMetadataResolver>(StringComparer.OrdinalIgnoreCase);
        var useCodex=!offline&&(codex??(online&&!string.Equals(Environment.GetEnvironmentVariable("MP3ORGANIZER_CODEX_ENABLED"),"false",StringComparison.OrdinalIgnoreCase)));
        var codexState=new CodexRunState();using var codexHttp=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};
        var observer=new LlmObservability(Console.WriteLine,repository);
        var codexClient=new CodexClient(codexHttp,Environment.GetEnvironmentVariable("OPENAI_API_KEY")??"",Environment.GetEnvironmentVariable("MP3ORGANIZER_CODEX_MODEL")??"",observer:observer);
        Console.WriteLine(useCodex?"Codex reasoning enabled (OpenAI API; factual candidates only).":"Codex reasoning disabled (offline or explicit configuration; deterministic analysis only).");
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
                if(resolvers.TryGetValue(source,out var existing))return existing;
                if(!sessions.TryGetValue(source,out var session))sessions[source]=session=new(source,Path.Combine(workspace,"cache.db"),null,online,false,workspace,progress:progress.Write);
                return resolvers[source]=useCodex?new CodexMetadataResolver(session.Resolver,codexClient,codexState,progress.Write,observer):new DeterministicObservedResolver(session.Resolver,observer);
            }
            bool ContinueWithoutCodex(CodexServiceException error)
            {
                Console.WriteLine("LLM unavailable: "+error.Kind+" — "+error.Message);
                Console.WriteLine("Continuing automatically without LLM; unresolved tracks will remain NeedsReview.");
                observer.DisableForRun();return true;
            }
            var analyzer=new IncrementalProgressService(repository,tagWriter:writeTags?tagWriter:null,codexState:codexState,continueWithoutCodex:ContinueWithoutCodex);
            var count=analyzer.AnalyzeAsync(limit,Resolver,progress.Write).GetAwaiter().GetResult();
            Console.WriteLine("Analyzed this batch: "+count);
            if(pipeline!=null&&!analyzer.StoppedForCodex)Console.WriteLine("Processed this run: "+pipeline.ApplyReady(targets!,reports!,progress.Write));
            var all=repository.All();PrintStatus(all);
            if(reports!=null)
                foreach(var group in all.GroupBy(x=>x.SourceRoot,StringComparer.OrdinalIgnoreCase))
                    new IdentificationReportWriter().Write(Path.Combine(reports,"progress-"+Guid.NewGuid().ToString("N")),group.Key,group.Select(x=>x.Effective));
            if(analyzer.StoppedForCodex){Console.WriteLine("Stopped with progress saved. Rerun the same "+command+" command to resume; no reset is needed.");return 4;}
            return analyzer.LastErrorCount+(pipeline?.ErrorCount??0)==0?0:3;
        }
        finally{Console.WriteLine("Batch LLM activity:");observer.Statistics.Print(Console.WriteLine);foreach(var session in sessions.Values)session.Dispose();}
    }
    private static void PrintStatus(IReadOnlyList<IndexedMusic> rows)
    {
        var done=rows.Count(x=>x.Status is ProcessingStatus.Processed or ProcessingStatus.Skipped);
        Console.WriteLine($"Total files: {rows.Count}\nProcessed: {rows.Count(x=>x.Status==ProcessingStatus.Processed)}\nReady to apply: {rows.Count(x=>x.Status==ProcessingStatus.Ready)}\nSkipped: {rows.Count(x=>x.Status==ProcessingStatus.Skipped)}\nNeeds review: {rows.Count(x=>x.Status==ProcessingStatus.NeedsReview)}\nPending analysis: {rows.Count(x=>x.Status is ProcessingStatus.Discovered or ProcessingStatus.Analyzed)}\nErrors: {rows.Count(x=>x.Status==ProcessingStatus.Error)}\nComplete (Processed/Skipped): {(rows.Count==0?0:100.0*done/rows.Count):F1}%");
        Console.WriteLine($"Auto recognized: {rows.Count(x=>x.Effective.Identification?.AutoRecognized==true)}\nYear enriched: {rows.Count(x=>x.Effective.Identification?.YearEnriched==true)}\nNo match: {rows.Count(x=>x.Effective.Identification?.ReviewReasons.Contains("NoFingerprintMatch")==true)}");
        Console.WriteLine($"Codex examined: {rows.Count(x=>x.Effective.Identification?.RecognitionMethod=="Codex")}\nCodex unavailable: {rows.Count(x=>x.Effective.Identification?.CodexUnavailable==true)}\nWaiting for Codex: {rows.Count(x=>x.Status==ProcessingStatus.Analyzed&&x.Effective.Identification?.CodexUnavailable==true)}");
        foreach(var group in rows.Where(x=>x.Status==ProcessingStatus.NeedsReview).SelectMany(x=>x.Effective.Identification?.ReviewReasons??[]).GroupBy(x=>x).OrderBy(x=>x.Key))Console.WriteLine($"{group.Key}: {group.Count()}");
        Console.WriteLine($"Missing/unreliable Artist: {rows.Count(x=>MetadataQualityEvaluator.Invalid(x.Effective.Artist))}\nMissing/unreliable Album: {rows.Count(x=>MetadataQualityEvaluator.Invalid(x.Effective.Album))}\nMissing Track Number: {rows.Count(x=>x.Effective.Track==0)}\nMissing Year: {rows.Count(x=>x.Effective.Year==0)}");
    }
}
