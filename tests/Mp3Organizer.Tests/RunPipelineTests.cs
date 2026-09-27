using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static IndexedMusic MakeReady(IndexedMusic row)=>row with{Status=ProcessingStatus.Ready,Effective=row.Basic with{Track=1,Identification=new(){Status="Resolved",MetadataPolicyVersion=2}}};
    static void RunExistingReadyAndBlockedStates()
    {
        var f=ProgressFixture(5);var workspace=ProgressWorkspace(f);var before=Snapshot(f.Source);
        using(var repo=new ProgressRepository(workspace))
        {
            new IncrementalProgressService(repo).Scan(f.Source);var rows=repo.All();repo.Save(MakeReady(rows[0]),"Chubby Checker-style already Ready");
            repo.Save(rows[1] with{Status=ProcessingStatus.NeedsReview,Effective=rows[1].Effective with{Identification=new(){Status="Review"}}},"review");
            repo.Save(rows[2] with{Status=ProcessingStatus.Error},"error");repo.Save(rows[3] with{Status=ProcessingStatus.Skipped},"skip");repo.Save(rows[4] with{Status=ProcessingStatus.Processed,Effective=MakeReady(rows[4]).Effective},"done");
        }
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace,"--target",f.Target]));
        using(var repo=new ProgressRepository(workspace)){Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.Processed));Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.NeedsReview));Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Error));Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Skipped));Equal(f.Target,repo.GetRunTarget(f.Source));}
        Equal(1,new AudioFileScanner().Scan(f.Target).Count);var targetBefore=Snapshot(f.Target);
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace]));Equal(targetBefore,Snapshot(f.Target));Equal(before,Snapshot(f.Source));
    }
    static void RunAnalysisThroughProcessedAndRescan()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);Equal(0,Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",workspace]));
        using(var repo=new ProgressRepository(workspace))
        {
            var item=repo.All().Single();var manual=new ManualResolutionStore(workspace,f.Source);var data=manual.Load();data.Files["sha256:"+item.Basic.Sha256.ToLowerInvariant()]=new(){TrackNumber=1,LastKnownPath=f.File};manual.Save(data);
        }
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace]));
        using(var repo=new ProgressRepository(workspace))Equal(ProcessingStatus.Processed,repo.All().Single().Status);
        var folder=Path.Combine(f.Source,"Album C","Disc 1");Directory.CreateDirectory(folder);var added=Path.Combine(folder,"new.wav");File.Copy(f.File,added);
        Equal(0,Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",workspace]));
        using(var repo=new ProgressRepository(workspace)){Equal(ProcessingStatus.Processed,repo.All().Single(x=>x.CurrentPath==f.File).Status);Equal(ProcessingStatus.Discovered,repo.All().Single(x=>x.CurrentPath==added).Status);}
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace]));
        using(var repo=new ProgressRepository(workspace))True(repo.All().All(x=>x.Status==ProcessingStatus.Processed));
    }
    static void RunDeterministicPendingOrder()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);var z=Path.Combine(f.Source,"Z.wav");File.Copy(f.File,z);using var repo=new ProgressRepository(workspace);var service=new IncrementalProgressService(repo);service.Scan(f.Source);
        var a=Path.Combine(f.Source,"A.wav");File.Copy(f.File,a);service.Scan(f.Source);var visited=new List<string>();
        service.AnalyzeAsync(100,_=>new ProgressResolver(),s=>{if(s.StartsWith("Analyzing ["))visited.Add(s[(s.IndexOf(": ")+2)..]);}).GetAwaiter().GetResult();
        True(visited.SequenceEqual(visited.Order(StringComparer.OrdinalIgnoreCase)));Equal(a,visited.First());
    }
    static void RunPlanBlocksReviewAndStateChanges()
    {
        var f=ProgressFixture(2);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var rows=repo.All();var good=MakeReady(rows[0]);repo.Save(good,"ready");repo.Save(rows[1] with{Status=ProcessingStatus.NeedsReview},"review");
        var plan=new CopyPlanBuilder().Build(f.Source,f.Target,readyInventory:[good.Effective],workspace:workspace);Equal(1,plan.Operations.Count);repo.Save(good with{Status=ProcessingStatus.NeedsReview},"Changed to review after planning");
        Throws<IOException>(()=>new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false));True(!Directory.Exists(f.Target));
        ProgressResultRecorder.Record(workspace,f.Source,[good.Effective],true);Equal(ProcessingStatus.NeedsReview,repo.All().Single(x=>x.Id==good.Id).Status);
        var ordinary=new CopyPlanBuilder().Build(f.Source,f.Target,workspace:workspace);Equal(0,ordinary.Operations.Count);
    }
    static void RunChangedReadyDoesNotBlockOtherReady()
    {
        var f=ProgressFixture(2);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var items=repo.All();foreach(var item in items)repo.Save(MakeReady(item),"ready");
        using(var file=new FileStream(items[0].CurrentPath,FileMode.Open,FileAccess.Write)){file.Position=file.Length-1;file.WriteByte(1);}
        var pipeline=new RunPipelineService(repo,workspace);Equal(1,pipeline.ApplyReady(pipeline.Targets(f.Target),Path.Combine(workspace,"reports")));Equal(1,pipeline.ErrorCount);Equal(ProcessingStatus.Error,repo.All().Single(x=>x.Id==items[0].Id).Status);Equal(ProcessingStatus.Processed,repo.All().Single(x=>x.Id==items[1].Id).Status);
    }
    static void RunDuplicateReadyCompletesAndTargetsSafe()
    {
        var f=ProgressFixture(2);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);foreach(var item in repo.All())repo.Save(MakeReady(item),"ready");
        var pipeline=new RunPipelineService(repo,workspace);Throws<IOException>(()=>pipeline.Targets(f.Source));Equal(2,pipeline.ApplyReady(pipeline.Targets(f.Target),Path.Combine(workspace,"reports")));Equal(1,new AudioFileScanner().Scan(f.Target).Count);True(repo.All().All(x=>x.Status==ProcessingStatus.Processed));Throws<IOException>(()=>pipeline.Targets(f.Target+"-other"));
    }
    static void RunAfterManualReview()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);
        using(var repo=new ProgressRepository(workspace))
        {
            new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();var metadata=TagMetadata.From(item.Basic) with{Track=1};
            repo.Save(item with{Status=ProcessingStatus.NeedsReview,Effective=item.Effective with{Identification=new(){Status="Review",Candidates=[new(metadata,"","",0,new())]}}},"review");
            Equal(1,new ReviewService(repo,workspace,new ReviewInput("1","YES")).Run());Equal(ProcessingStatus.Ready,repo.All().Single().Status);
        }
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace,"--target",f.Target]));
        using(var repo=new ProgressRepository(workspace))Equal(ProcessingStatus.Processed,repo.All().Single().Status);
    }
    static void RunContradictoryReadyBlockedAndPlanFailureRetainsReady()
    {
        var f=ProgressFixture(2);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var rows=repo.All();
        repo.Save(MakeReady(rows[0]) with{Effective=rows[0].Effective with{Identification=new(){Status="Review",MetadataPolicyVersion=2}}},"legacy inconsistent Ready");
        repo.Save(MakeReady(rows[1]) with{Effective=MakeReady(rows[1]).Effective with{Title=new string('X',300)}},"overlong destination");
        var pipeline=new RunPipelineService(repo,workspace);Throws<IOException>(()=>pipeline.ApplyReady(pipeline.Targets(f.Target),Path.Combine(workspace,"reports")));
        Equal(ProcessingStatus.NeedsReview,repo.All().Single(x=>x.Id==rows[0].Id).Status);Equal(ProcessingStatus.Ready,repo.All().Single(x=>x.Id==rows[1].Id).Status);True(!Directory.Exists(f.Target));
    }
}

