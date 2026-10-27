using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void PaddedOutputNamesCollapseWithoutTruncation()
    {
        Equal("Song mediagrant.com",TargetPathBuilder.SafeName(" Song"+new string(' ',240)+"mediagrant.com "));
        Equal(new string('a',250),TargetPathBuilder.SafeName(new string('a',250)));
        var f=Fixture();var row=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File) with{Title="Song"+new string(' ',240)+"mediagrant.com",Album="Album",Artist="Singer",AlbumArtist="Singer"};
        var path=TargetPathBuilder.Build(row);True(path.Length<180);True(path.Contains("Song mediagrant.com"));True(row.Title.Contains(new string(' ',240)));
    }
    static void NumericAlbumAcceptedAndIncompleteFieldsNamed()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();
        var metadata=MakeReady(row).Effective with{Artist="Grzegorz Turnau",AlbumArtist="Grzegorz Turnau",Album="11 11",Title="Czas blekitu",Year=2005,Track=1};
        True(!ManualMetadataResolver.NeedsInput(metadata));Equal("11 11",MetadataFieldMerger.Merge(metadata,metadata).Album);
        repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=metadata},"numeric album");
        var input=new GuidedReviewInput("");new CandidateReviewService(repo,ws,input).Run();Equal(ProcessingStatus.Ready,repo.All().Single().Status);True(input.Output.Any(x=>x.Contains("Saved; Ready.")));
        var run=new RunPipelineService(repo,ws);Equal(1,run.ApplyReady(run.Targets(f.Target),f.Reports));Equal(ProcessingStatus.Processed,repo.All().Single().Status);
        True(!ManualMetadataResolver.NeedsInput(metadata with{Year=0,Album="1989"}));
        True(ManualMetadataResolver.IncompleteFields(metadata with{Album="",Year=0}).SequenceEqual(new[]{"Album"}));
        repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=metadata with{Artist="",AlbumArtist="Various Artists"}},"missing artist");
        var missing=new GuidedReviewInput("");new CandidateReviewService(repo,ws,missing).Run();True(missing.Output.Any(x=>x.Contains("missing or invalid: Artist")));
    }
    static void ConfirmedNumericSongWithOptionalYearSavesAndApplies()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);var before=Snapshot(f.Source);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();
        repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Artist="The Who",AlbumArtist="The Who",Album="Quadrophenia",Title="5-15",Year=0,Track=1}},"optional year fixture");
        True(ManualMetadataResolver.NeedsInput(repo.All().Single().Effective));
        new CandidateReviewService(repo,ws,new GuidedReviewInput("")).Run();
        var saved=repo.All().Single();Equal(ProcessingStatus.Ready,saved.Status);Equal("5-15",saved.Effective.Title);Equal(0u,saved.Effective.Year);True(!ManualMetadataResolver.NeedsInput(saved.Effective));
        Equal(0,new CandidateReviewService(repo,ws,new GuidedReviewInput("/q")).Run());
        var run=new RunPipelineService(repo,ws);Equal(1,run.ApplyReady(run.Targets(f.Target),f.Reports));Equal(ProcessingStatus.Processed,repo.All().Single().Status);Equal(before,Snapshot(f.Source));
        True(ManualMetadataResolver.NeedsInput(saved.Effective with{Title="Track 01"},"Track 01"));
    }
}