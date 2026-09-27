namespace Mp3Organizer;

public sealed class RunPipelineService(ProgressRepository repository,string workspace)
{
    public int ErrorCount {get;private set;}
    public static string DefaultTarget(string source)=>PathSafetyGuard.Canonical(source)+"-organized";
    public Dictionary<string,string> Targets(string? requestedTarget=null)
    {
        var roots=repository.All().Select(x=>x.SourceRoot).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if(roots.Count==0)throw new IOException("No indexed files. Run scan <source> with this workspace first.");
        if(requestedTarget!=null&&roots.Count!=1)throw new ArgumentException("--target requires exactly one indexed source root; use separate workspaces for custom targets.");
        var targets=roots.ToDictionary(x=>x,x=>PathSafetyGuard.Canonical(requestedTarget??repository.GetRunTarget(x)??DefaultTarget(x)),StringComparer.OrdinalIgnoreCase);
        foreach(var pair in targets)
        {
            foreach(var root in roots)PathSafetyGuard.Separate(root,pair.Value);
            PathSafetyGuard.Separate(workspace,pair.Value);ManagedTarget.Check(pair.Value,pair.Key);
            var old=repository.GetRunTarget(pair.Key);
            if(old!=null&&!old.Equals(pair.Value,StringComparison.OrdinalIgnoreCase)&&repository.All().Any(x=>x.SourceRoot==pair.Key&&x.Status==ProcessingStatus.Processed))throw new IOException("Cannot switch target while this source has Processed files. Use a separate workspace for a different target.");
        }
        var values=targets.Values.ToList();for(var i=0;i<values.Count;i++)for(var j=i+1;j<values.Count;j++)PathSafetyGuard.Separate(values[i],values[j]);
        foreach(var pair in targets)repository.SetRunTarget(pair.Key,pair.Value);
        return targets;
    }
    public int ApplyReady(IReadOnlyDictionary<string,string> targets,string reports,Action<string>? progress=null)
    {
        MetadataPolicy.Migrate(repository,workspace,progress);
        ErrorCount=0;var count=0;var yearCorrections=new AlbumYearCorrectionService(repository,workspace);yearCorrections.Propagate();
        foreach(var pair in targets.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase))
        {
            yearCorrections.Reconcile(pair.Key,pair.Value,progress);
            var ready=new List<IndexedMusic>();
            foreach(var item in repository.All().Where(x=>x.SourceRoot.Equals(pair.Key,StringComparison.OrdinalIgnoreCase)&&x.Status==ProcessingStatus.Ready).OrderBy(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase))
            {
                if(item.Effective.Identification?.Status=="Review"||ManualMetadataResolver.NeedsInput(item.Effective))
                {repository.Save(item with{Status=ProcessingStatus.NeedsReview},"Ready metadata is unresolved; apply blocked");continue;}
                try
                {
                    PathSafetyGuard.NoLinks(item.CurrentPath);
                    var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(item.CurrentPath);
                    if(actual.Error!=""||actual.Sha256!=item.Basic.Sha256)throw new IOException("Indexed source changed or cannot be read; scan before retrying.");
                    ready.Add(item);
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
                {repository.Save(item with{Status=ProcessingStatus.Error,ErrorMessage=e.Message},"Ready preflight failed");ErrorCount++;progress?.Invoke("Error: "+item.CurrentPath+": "+e.Message);}
            }
            if(ready.Count==0&&!repository.Folders().Any(x=>x.SourceRoot.Equals(pair.Key,StringComparison.OrdinalIgnoreCase)))continue;
            progress?.Invoke($"Planning {ready.Count} Ready files -> {pair.Value}");
            var manual=new ManualResolutionStore(workspace,pair.Key,pair.Value);manual.Load();var revision=manual.Revision();
            var plan=new CopyPlanBuilder().Build(pair.Key,pair.Value,progress:progress,readyInventory:ready.Select(x=>x.Effective).ToList(),workspace:workspace);
            if(revision!=manual.Revision())throw new IOException("Manual resolutions changed during run; retry run.");
            plan.ManualFilePath=manual.FilePath;plan.ManualFileRevision=revision;
            var saved=new CopyPlanStore().Save(plan,reports);new CsvReportWriter().Write(Path.GetDirectoryName(saved)!,pair.Key,plan.Library,plan);
            progress?.Invoke("Saved plan: "+saved);
            // Reload and validate the saved plan before the existing guarded executor copies anything.
            plan=new CopyPlanStore().Load(saved);
            new SafeCopyExecutor().Execute(plan,pair.Key,pair.Value,false);
            var handled=plan.Operations.Select(x=>x.Metadata.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var item in ready)
            {
                // SHA-identical duplicates are fulfilled by the selected, verified target copy.
                if(!handled.Contains(item.Basic.Sha256))continue;
                var operation=plan.Operations.First(x=>x.Metadata.Sha256==item.Basic.Sha256);
                repository.MapCanonical(item,pair.Value,operation.RelativePath,operation.Metadata);
                repository.Save(item with{Status=ProcessingStatus.Processed,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=""},"Run applied validated plan "+plan.PlanId);
                count++;progress?.Invoke("Processed: "+item.CurrentPath);
            }
            new FolderPlaylistService(repository).Regenerate(pair.Key,pair.Value,progress);
        }
        return count;
    }
}
