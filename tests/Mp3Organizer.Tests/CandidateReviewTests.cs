using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void ReviewEnterSavesLooseTrackAndPreservesDraft()
    {
        var f=Fixture();var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);var row=GuidedReviewFixture(repo,f);
        repo.Save(row with{Effective=row.Effective with{Album="",Year=0,Track=0}},"loose track");
        var input=new GuidedReviewInput("M","","Joy to the World","/cancel","M","","","","","","Angelo Kelly & Family","/cancel","","");
        Equal(1,new CandidateReviewService(repo,ws,input).Run());var saved=repo.All().Single();Equal("Joy to the World",saved.Effective.Title);Equal("Angelo Kelly & Family",saved.Effective.AlbumArtist);Equal(ProcessingStatus.Ready,saved.Status);True(saved.Effective.ConfirmedLooseTrack);
        var restored=new ManualMetadataResolver(new StubMetadataResolver(),new ManualResolutionStore(ws,f.Source)).ResolveAsync(saved.Basic,[]).GetAwaiter().GetResult();True(!ManualMetadataResolver.NeedsInput(restored));
        Equal(0,new CandidateReviewService(repo,ws,new GuidedReviewInput()).Run());var run=new RunPipelineService(repo,ws);Equal(1,run.ApplyReady(run.Targets(f.Target),f.Reports));Equal(ProcessingStatus.Processed,repo.All().Single().Status);Equal(before,Snapshot(f.Source));
        True(input.Output.Any(x=>x.Contains("Title (track) [Joy to the World]")));
    }
    static void ReviewSingleEnterSavesAndAdvances()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Album="",Track=0}},"loose tracks");
        var input=new GuidedReviewInput("","");Equal(2,new CandidateReviewService(repo,ws,input).Run());True(repo.All().All(x=>x.Status==ProcessingStatus.Ready));
        True(!input.Output.Any(x=>x.Contains("Confirm:")));Equal(2,input.Output.Count(x=>x.Contains("Action [Enter = save and next; M = edit fields]")));
    }
    static void ReviewActionBackAndVisibleControls()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview},"review");
        var played=0;var input=new GuidedReviewInput("/back","","/back","P","M","","Corrected title","","","","","","/q");
        Equal(1,new CandidateReviewService(repo,ws,input,_=>played++).Run());Equal(1,played);
        var rows=repo.All().OrderBy(x=>x.CurrentPath).ToArray();Equal("Corrected title",rows[0].Effective.Title);Equal(ProcessingStatus.NeedsReview,rows[1].Status);
        True(input.Output.Any(x=>x.Contains("[P] Play | [D] Defer | [/back] Previous file | [/q] Stop:")));
        True(input.Output.Any(x=>x.Contains("first file in this review session")));
    }
    static void CandidateModeNumbersAndConfirmation()
    {
        foreach(var choice in new[]{"1","5",""})
        {
            var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);var row=GuidedReviewFixture(repo,f);
            var candidates=Enumerable.Range(1,5).Select(i=>new RecognitionCandidate(TagMetadata.From(row.Effective) with{Title="Candidate "+i},"","",i*.1,new())).ToList();
            repo.Save(row with{Effective=row.Effective with{Identification=row.Effective.Identification! with{Candidates=candidates}}},"candidates");
            var service=new CandidateReviewService(repo,ws,new GuidedReviewInput(choice,"YES"));Equal(1,service.Run());Equal("Candidate "+(choice==""?"5":choice),repo.All().Single().Effective.Title);
        }
    }
    static void CandidateManualCancelBackClearAndScope()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);GuidedReviewFixture(repo,f);
        var cancelled=new GuidedReviewInput("M","Changed artist","/cancel","/q");Equal(0,new CandidateReviewService(repo,ws,cancelled).Run());True(!File.Exists(Path.Combine(ws,"manual-resolutions.json")));
        var unconfirmed=new GuidedReviewInput("M","","Draft title","","","","","NO","/q");Equal(0,new CandidateReviewService(repo,ws,unconfirmed).Run());True(!File.Exists(Path.Combine(ws,"manual-resolutions.json")));
        var input=new GuidedReviewInput("M","","Draft title","/back","Correct title","","/clear","","","YES");Equal(1,new CandidateReviewService(repo,ws,input).Run());
        Equal("Correct title",repo.All().Single().Effective.Title);Equal(0u,repo.All().Single().Effective.Year);True(input.Output.Any(x=>x.Contains("Title (track) [Draft title]")));
        var manual=new ManualResolutionStore(ws,f.Source).Load().Files.Values.Single();True(manual.YearTrackOnly);Equal(0u,manual.Year!.Value);
    }
    static void CandidateReviewDoesNotPropagateTrackValues()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var item in repo.All())repo.Save(MakeReady(item) with{Status=ProcessingStatus.NeedsReview},"review");
        var input=new GuidedReviewInput("M","Unique Artist","Unique Title","Album decision","1999","7","Album Artist","YES","D");Equal(1,new CandidateReviewService(repo,ws,input).Run());
        new AlbumYearCorrectionService(repo,ws).Propagate();var untouched=repo.All().Single(x=>x.Status==ProcessingStatus.NeedsReview);Equal("Test Artist",untouched.Effective.Artist);Equal("Test Song",untouched.Effective.Title);
    }
}
