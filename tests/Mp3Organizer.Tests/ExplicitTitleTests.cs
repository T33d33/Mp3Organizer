using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void ExplicitManualTitleOverridesPlaceholderHeuristic()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Title=""}},"title fixture");
        var input=new GuidedReviewInput("M","","Title","","","","","");new CandidateReviewService(repo,ws,input).Run();var saved=repo.All().Single();Equal("Title",saved.Effective.Title);Equal("Title",saved.Effective.ExplicitlyConfirmedTitle);Equal(ProcessingStatus.Ready,saved.Status);
        var store=new ManualResolutionStore(ws,f.Source);var decision=store.Load().Files.Values.Single();Equal("Title",decision.ExplicitlyConfirmedTitle);True(!ManualMetadataResolver.NeedsInput(ManualMetadataResolver.Apply(row.Effective,decision)));
        True(!ManualMetadataResolver.NeedsInput(ManualMetadataResolver.Apply(saved.Effective,new(){Title="Title"})));
        var run=new RunPipelineService(repo,ws);Equal(1,run.ApplyReady(run.Targets(f.Target),f.Reports));
        True(ManualMetadataResolver.NeedsInput(saved.Effective with{Title="",ExplicitlyConfirmedTitle=""}));True(ManualMetadataResolver.NeedsInput(saved.Effective with{Title="Track 01"}));
    }
}