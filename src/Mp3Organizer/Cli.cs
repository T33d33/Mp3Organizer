namespace Mp3Organizer;
public static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OverflowException)
        { Console.Error.WriteLine("Error: " + e.Message); return 1; }
    }
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") { Help(); return args.Length == 0 ? 2 : 0; }
        var command = args[0];
        var positionals = new List<string>();
        string reports = Path.GetFullPath("reports");
        string? planFile = null;
        var dryRun = false;
        var online = false; var offline = false; var identifyAll = false;
        string? cachePath = null;
        string workspace=Path.GetFullPath(Environment.GetEnvironmentVariable("MP3ORGANIZER_WORKSPACE")??"workspace");
        var rebuild=false;var refresh=false;var onlyUnresolved=false;var editManual=false;var retryDays=30;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--reports" when i + 1 < args.Length: reports = Path.GetFullPath(args[++i]); break;
                case "--plan" when i + 1 < args.Length: planFile = Path.GetFullPath(args[++i]); break;
                case "--dry-run": dryRun = true; break;
                case "--online": online = true; break;
                case "--offline": offline = true; break;
                case "--identify-all": identifyAll = true; break;
                case "--cache" when i + 1 < args.Length: cachePath = Path.GetFullPath(args[++i]); break;
                case "--workspace" when i+1<args.Length:workspace=Path.GetFullPath(args[++i]);break;
                case "--rebuild-fingerprints":rebuild=true;break;
                case "--refresh-identification":refresh=true;break;
                case "--only-unresolved":onlyUnresolved=true;break;
                case "--edit-manual":editManual=true;break;
                case "--identification-retry-days" when i+1<args.Length:if(!int.TryParse(args[++i],out retryDays)||retryDays<1||retryDays>3650)throw new ArgumentException("Retry age must be 1..3650 days.");break;
                default:
                    if (args[i].StartsWith("--")) throw new ArgumentException("Unknown or incomplete option: " + args[i]);
                    positionals.Add(args[i]); break;
            }
        }
        if(command=="doctor")
        {
            if(positionals.Count!=0||online||identifyAll||rebuild||refresh||onlyUnresolved||editManual||dryRun||planFile!=null)throw new ArgumentException("Use doctor [--offline] [--workspace <directory>] [--cache <path>].");
            return IdentificationDoctor.Run(workspace,cachePath,offline);
        }
        if(command=="manual")
        {
            if(positionals.Count!=1||positionals[0]!="validate")throw new ArgumentException("Use manual validate [--workspace <directory>].");
            var manual=new ManualResolutionStore(workspace,Path.Combine(Path.GetDirectoryName(workspace)!,".manual-source-guard"));
            var data=manual.Load();var result=manual.ValidateReferences(data);manual.WriteOverview(data);
            Console.WriteLine($"Folder overrides: {result.FolderCount}\nFile overrides: {result.FileCount}\nValidation errors: 0\nUnresolved references: {result.UnresolvedReferences.Count}");foreach(var warning in result.UnresolvedReferences)Console.WriteLine(warning);return 0;
        }
        if (command is not ("analyze" or "identify" or "resolve" or "plan" or "apply" or "playlists")) throw new ArgumentException("Unknown command.");
        if(refresh&&!online)throw new ArgumentException("--refresh-identification requires --online.");
        if((onlyUnresolved||editManual)&&command!="resolve")throw new ArgumentException("Manual editing flags require resolve.");
        if (online && offline) throw new ArgumentException("Choose --online or --offline, not both.");
        if ((online || offline || identifyAll || rebuild||refresh) && command is "apply" or "playlists") throw new ArgumentException("Identification options apply only to identify, resolve, analyze, or plan.");
        if (positionals.Count != (command is "plan" or "apply" ? 2 : 1)) throw new ArgumentException("Incorrect arguments. Use --help.");
        if ((dryRun || planFile != null) && command != "apply") throw new ArgumentException("--dry-run and --plan apply only to apply.");
        if(command is "identify" or "analyze" or "resolve" or "plan")
        {
            var configuration=IdentificationConfiguration.Read();
            Console.WriteLine(configuration.Status(online,offline));
            if(online&&configuration.Problem!=null)return 1;
        }
        var source = PathSafetyGuard.Canonical(positionals[0]);
        if (command == "playlists")
        {
            var state = ManagedTarget.Check(source) ?? throw new IOException("playlists requires a target created by apply.");
            var manual=new ManualResolutionStore(workspace,state.Source,source);manual.Load();
            var progress = new ConsoleProgress();
            var scanner = new AudioFileScanner();
            var rows = progress.Read(scanner,new TagLibMetadataReader(new ReadOnlySource()),source);
            if (scanner.Errors.Count != 0 || rows.Any(x => x.Error != "")) throw new IOException("Target scan is incomplete; playlists were not changed.");
            rows = TargetMetadataStore.Overlay(source,rows);
            rows=ManualMetadataResolver.ReconcileTarget(source,rows,manual);
            var map = new PlaylistMapStore().Load(source);
            var lists = new PlaylistBuilder().Build(rows.Select(x => new CopyOperation(x, Path.GetRelativePath(source, x.FullPath), "Existing")).ToList(), map);
            PlaylistBuilder.IncludeRetired(lists, state);
            new ManagedPlaylistWriter().Write(source, state.Source, lists, map,rows.ToDictionary(x=>Path.GetRelativePath(source,x.FullPath),StringComparer.OrdinalIgnoreCase));
            Console.WriteLine($"Generated {lists.Count} playlists; managed replacements are backed up."); return 0;
        }
        PathSafetyGuard.Separate(source, reports);
        new ManualResolutionStore(workspace,source,command is "plan" or "apply"?PathSafetyGuard.Canonical(positionals[1]):null).Load();
        if (command is "analyze" or "identify" or "resolve")
        {
            var progress = new ConsoleProgress();
            var scanner = new AudioFileScanner();
            var rows = progress.Read(scanner,new TagLibMetadataReader(new ReadOnlySource()),source);
            using var identification = new IdentificationSession(source,cachePath ?? Path.Combine(workspace,"cache.db"),null,online,identifyAll,workspace,rebuild,refresh,retryDays,progress.Write);
            rows = IdentificationSession.ResolveAll(rows,identification.Resolver,identifyAll||rebuild,progress.Write);
            if(command=="resolve")
            {
                if(scanner.Errors.Count>0||rows.Any(x=>x.Error!=""))throw new IOException("Source scan incomplete; resolve aborted.");
                new InteractiveResolution(identification.ManualStore!,new ResolutionConsole()).Run(rows,onlyUnresolved,editManual);
                rows=IdentificationSession.ResolveAll(rows,identification.Resolver,false,progress.Write);
            }
            progress.Write("Writing reports...");
            var folder = Path.Combine(reports, (command == "identify" ? "identification-" : "analysis-") + Guid.NewGuid().ToString("N"));
            new CsvReportWriter().Write(folder, source, rows, null, scanner.Errors.Concat(rows.Where(x => x.Error != "").Select(x => x.FullPath + ": " + x.Error)));
            CsvReportWriter.Summary(rows);
            Console.WriteLine($"Scan warnings: {scanner.Errors.Count}\nReports: {folder}");
            Console.WriteLine($"Identification review: {rows.Count(x => x.Identification?.Status == "Review")}");
            return scanner.Errors.Count == 0 && rows.All(x => x.Error == "") ? 0 : 3;
        }
        var target = PathSafetyGuard.Canonical(positionals[1]);
        PathSafetyGuard.Separate(source, target);
        PathSafetyGuard.Separate(target, reports);
        if (command == "plan")
        {
            var progress = new ConsoleProgress();
            using var identification = new IdentificationSession(source,cachePath ?? Path.Combine(workspace,"cache.db"),target,online,identifyAll,workspace,rebuild,refresh,retryDays,progress.Write);
            var manualRevision=identification.ManualStore!.Revision();
            var plan = new CopyPlanBuilder().Build(source, target,identification.Resolver,identifyAll||rebuild,progress.Write);
            if(manualRevision!=identification.ManualStore.Revision())throw new IOException("Manual resolutions changed while building the plan; run plan again.");
            plan.ManualFilePath=identification.ManualStore.FilePath;plan.ManualFileRevision=manualRevision;
            var saved = new CopyPlanStore().Save(plan, reports);
            new CsvReportWriter().Write(Path.GetDirectoryName(saved)!, source, plan.Library, plan);
            CsvReportWriter.Summary(plan.Library);
            Console.WriteLine($"Copy: {plan.Operations.Count(x => x.Action == "Copy")}\nAlready present: {plan.Operations.Count(x => x.Action == "AlreadyPresent")}\nPlaylists: {plan.Playlists.Count}\nBlocking conflicts: {plan.Conflicts.Count}\nSaved plan: {saved}");
            if (plan.Conflicts.Count == 0) new CopyPlanValidator().Validate(plan, source, target);
            return plan.Conflicts.Count == 0 ? 0 : 3;
        }
        var file = planFile ?? new CopyPlanStore().Latest(reports, source, target);
        var savedPlan = new CopyPlanStore().Load(file);
        if(savedPlan.ManualFilePath!=""&&!string.Equals(savedPlan.ManualFilePath,Path.Combine(workspace,"manual-resolutions.json"),StringComparison.OrdinalIgnoreCase))throw new IOException("Use the same --workspace as the saved plan, or create a new plan.");
        if(savedPlan.ManualFilePath==""&&File.Exists(Path.Combine(workspace,"manual-resolutions.json")))throw new IOException("Plan predates manual resolution support; create a new plan.");
        new SafeCopyExecutor().Execute(savedPlan, source, target, dryRun);
        Console.WriteLine(dryRun ? $"Dry run validated: {savedPlan.Operations.Count(x => x.Action == "Copy")} copies and {savedPlan.Playlists.Count} playlists. No files written." : "Apply completed; source untouched. Managed playlist replacements were backed up.");
        return 0;
    }
    private static void Help() => Console.WriteLine("Mp3Organizer analyze <source> [--reports <directory>]\nMp3Organizer identify <source> [--identify-all] [--online|--offline]\nMp3Organizer resolve <source> [--only-unresolved] [--edit-manual]\nMp3Organizer doctor [--offline] [--workspace <directory>] [--cache <path>]\nMp3Organizer manual validate\nMp3Organizer plan <source> <target>\nMp3Organizer apply <source> <target> [--plan <copy-plan.json>] [--dry-run]\nMp3Organizer playlists <target>\nShared: --workspace <directory> (default workspace), --reports <directory>\nIdentification: --cache <cache.db>, --rebuild-fingerprints, --refresh-identification (requires --online), --identification-retry-days <days> (default 30).\nOnline identification is opt-in. Manual JSON is authoritative; source files are immutable.");
}
