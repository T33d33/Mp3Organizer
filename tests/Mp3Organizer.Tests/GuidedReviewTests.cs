using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    sealed class GuidedReviewInput(params string[] answers):IReviewConsole
    {
        readonly Queue<string> inputs=new(answers);public List<string> Output=new();
        public void Write(string text)=>Output.Add(text);
        public string? Read()=>inputs.Count>0?inputs.Dequeue():null;
    }
    static IndexedMusic GuidedReviewFixture(ProgressRepository repo,FixturePaths f)
    {
        new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();
        var review=row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1,Identification=new(){Status="Review",ReviewReasons=["WeakFingerprint"]}}};repo.Save(review,"test review");return review;
    }
    static void GuidedReviewWithoutCandidatesUsesDefaults()
    {
        var f=Fixture();var before=Snapshot(f.Source);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);GuidedReviewFixture(repo,f);
        var input=new GuidedReviewInput("");Equal(1,new ReviewService(repo,workspace,input).Run());
        Equal(ProcessingStatus.Ready,repo.All().Single().Status);Equal("Test Album",repo.All().Single().Effective.Album);
        True(input.Output.Any(x=>x.Contains("No online options")));True(!input.Output.Any(x=>x.Contains("[1-9]")||x.Contains("Accept candidate 1")));
        var questions=input.Output.Where(x=>x.StartsWith("Question ")).ToList();Equal(0,questions.Count);
        Equal(before,Snapshot(f.Source));
        True(!input.Output.Any(x=>x.Contains("Save [YES]")));

    }
    static void ReviewAlbumYearOnceAndAcrossRestart()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1,Year=0}},"unknown year");
        var first=new GuidedReviewInput("","2017","Q");Equal(1,new ReviewService(repo,workspace,first).Run());
        Equal(1,first.Output.Count(x=>x.Contains("Album year []:")));
        var second=new GuidedReviewInput("");Equal(1,new ReviewService(repo,workspace,second).Run());
        True(!second.Output.Any(x=>x.Contains("Album year []:")));True(repo.All().All(x=>x.Effective.Year==2017));
    }
    static void ReviewKnownAlbumYearClearsOnlyYearBlocker()
    {
        foreach(var extraReason in new[]{false,true})
        {
            var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
            var rows=repo.All().OrderBy(x=>x.CurrentPath).ToArray();
            repo.Save(rows[0] with{Status=ProcessingStatus.Ready,Effective=rows[0].Basic with{Track=1,Year=2017,Identification=new(){FieldSources=new(){["Year"]="FileManualOverride"}}}},"manual album year");
            repo.Save(rows[1] with{Status=ProcessingStatus.NeedsReview,Effective=rows[1].Basic with{Track=1,Year=0,Identification=new(){Status="Review",ReviewReasons=extraReason?["UnknownYear","WeakFingerprint"]:["UnknownYear"],ReviewReason="UnknownYear; Original year not established uniquely; no year guessed."}}},"year review");
            var before=Snapshot(f.Source);var input=new GuidedReviewInput("Q");Equal(extraReason?0:1,new ReviewService(repo,workspace,input).Run());
            var result=repo.All().Single(x=>x.Id==rows[1].Id);Equal(extraReason?ProcessingStatus.NeedsReview:ProcessingStatus.Ready,result.Status);
            if(!extraReason)
            {
                Equal(2017u,result.Effective.Year);Equal(0,result.Effective.Identification!.ReviewReasons.Length);Equal("",result.Effective.Identification.ReviewReason);
                True(!input.Output.Any(x=>x.Contains("Your choice")));Equal(0,new ReviewService(repo,workspace,new GuidedReviewInput()).Run());
                Equal(2017u,new ManualResolutionStore(workspace,f.Source).Load().Files.Values.Single().Year!.Value);
            }
            Equal(before,Snapshot(f.Source));
        }
    }
    static void ReviewGuidedAlbumNeverSkipsSongTitles()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1,Year=0,Identification=new(){Status="Review",ReviewReasons=["UnknownYear"]}}},"year missing");
        var input=new GuidedReviewInput("M","","","1999","First title","","Second title","");Equal(2,new ReviewService(repo,workspace,input).Run());
        Equal(2,input.Output.Count(x=>x.StartsWith("Question — Title")));Equal(1,input.Output.Count(x=>x.StartsWith("Question — Album year")));
        True(repo.All().Any(x=>x.Effective.Title=="Second title"));
    }
    static void ReviewReopensReadyPlaceholderTitles()
    {
        foreach(var title in new[]{"Track","02.Track","AudioTrack 02"})
        {
            var f=Fixture();var before=Snapshot(f.Source);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);var row=GuidedReviewFixture(repo,f);
            repo.Save(row with{Status=ProcessingStatus.Ready,Effective=row.Effective with{Title=title}},"old ready");
            var input=new GuidedReviewInput("M","","","","Real song","");Equal(1,new ReviewService(repo,workspace,input).Run());
            True(input.Output.Any(x=>x.Contains("placeholder title")));Equal("Real song",repo.All().Single().Effective.Title);Equal(before,Snapshot(f.Source));
        }
    }
    static void ReviewFinishAlbumStopsAtInvalidAndFolderBoundary()
    {
        foreach(var invalidTitle in new[]{false,true})
        {
            var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"z-second.wav"));Directory.CreateDirectory(Path.Combine(f.Source,"next"));File.Copy(f.File,Path.Combine(f.Source,"next","song.wav"));
            var before=Snapshot(f.Source);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
            foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1,Title=invalidTitle&&Path.GetFileName(row.CurrentPath)=="z-second.wav"?"Track":row.Basic.Title}},"review");
            // The child folder sorts first; skip it for this invocation, then finish the parent album.
            var input=invalidTitle?new GuidedReviewInput("S","M","","","F","Real title",""):new GuidedReviewInput("S","M","","","F");
            Equal(3,new ReviewService(repo,workspace,input).Run());True(repo.All().Where(x=>x.Status!=ProcessingStatus.Skipped).All(x=>x.Status==ProcessingStatus.Ready));
            Equal(invalidTitle,input.Output.Any(x=>x.Contains("Auto-finish paused")));Equal(before,Snapshot(f.Source));
        }
        var other=Fixture();Directory.CreateDirectory(Path.Combine(other.Source,"zz-next"));File.Copy(other.File,Path.Combine(other.Source,"zz-next","song.wav"));var ws=ProgressWorkspace(other);using var db=new ProgressRepository(ws);new IncrementalProgressService(db).Scan(other.Source);
        foreach(var row in db.All())db.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1}},"review");
        Equal(1,new ReviewService(db,ws,new GuidedReviewInput("F","Q")).Run());Equal(1,db.All().Count(x=>x.Status==ProcessingStatus.NeedsReview));
    }
    static void ReviewBackCrossesSongsAndPreservesDrafts()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"z-second.wav"));var before=Snapshot(f.Source);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1}},"review");
        var input=new GuidedReviewInput("M","","","First title","13","Second edited title","/back","/back","/back","First corrected title","14","/","","2");
        Equal(2,new ReviewService(repo,workspace,input).Run());
        var result=repo.All().OrderBy(x=>x.CurrentPath).ToArray();Equal("First corrected title",result[0].Effective.Title);Equal(14u,result[0].Effective.Track);
        Equal("Second edited title",result[1].Effective.Title);Equal(2u,result[1].Effective.Track);
        True(input.Output.Any(x=>x.Contains("Track number [13]")));True(input.Output.Any(x=>x.Contains("Title [Second edited title]")));
        True(input.Output.Any(x=>x.Contains("Commands: /back")));True(!input.Output.Any(x=>x.Contains("placeholder title")));Equal(before,Snapshot(f.Source));
    }
    static void ReviewLegitimateNoTitlePersistsAndProcesses()
    {
        foreach(var title in new[]{"No","no","NO"})True(!MetadataQualityEvaluator.Invalid(title));
        foreach(var junk in new[]{"no title","No-Artist","no album","AudioTrack 02","Track"})True(MetadataQualityEvaluator.Invalid(junk));
        var f=Fixture();var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);var row=GuidedReviewFixture(repo,f);
        repo.Save(row with{Effective=row.Effective with{Title="No"}},"legitimate short title");
        Equal(1,new ReviewService(repo,ws,new GuidedReviewInput("")).Run());Equal(ProcessingStatus.Ready,repo.All().Single().Status);
        Equal(0,new ReviewService(repo,ws,new GuidedReviewInput()).Run());
        var current=repo.All().Single();var resolved=new ManualMetadataResolver(new StubMetadataResolver(),new ManualResolutionStore(ws,f.Source)).ResolveAsync(current.Basic,[]).GetAwaiter().GetResult();
        Equal("No",resolved.Title);True(!ManualMetadataResolver.NeedsInput(resolved));
        var pipeline=new RunPipelineService(repo,ws);Equal(1,pipeline.ApplyReady(pipeline.Targets(f.Target),f.Reports));Equal(ProcessingStatus.Processed,repo.All().Single().Status);Equal(before,Snapshot(f.Source));
    }
    static void ReviewAcceptInvalidStaysInOverview()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);var row=GuidedReviewFixture(repo,f);
        repo.Save(row with{Effective=row.Effective with{Album="unknown",Track=0}},"incomplete");
        var input=new GuidedReviewInput("A","Q");Equal(0,new ReviewService(repo,workspace,input).Run());
        Equal(ProcessingStatus.NeedsReview,repo.All().Single().Status);
        True(input.Output.Any(x=>x.Contains("Missing or suspicious: Album, Track number")));
        True(!input.Output.Any(x=>x.StartsWith("Question —")));
        True(!File.Exists(Path.Combine(workspace,"manual-resolutions.json")));
    }
    static void GuidedReviewBackPreservesEditedValues()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);GuidedReviewFixture(repo,f);
        var input=new GuidedReviewInput("M","Correct Artist","Correct Album","/back","Edited Album","Correct Title","");
        Equal(1,new ReviewService(repo,workspace,input).Run());var row=repo.All().Single();Equal("Correct Artist",row.Effective.Artist);Equal("Edited Album",row.Effective.Album);Equal("Correct Title",row.Effective.Title);
        True(input.Output.Any(x=>x.EndsWith("Album title [Correct Album]:")));var manual=new ManualResolutionStore(workspace,f.Source).Load().Files.Values.Single();Equal("Edited Album",manual.Album);
    }
    static void GuidedReviewAlbumArtistReusedWithinFolder()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var before=Snapshot(f.Source);
        var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Basic with{Track=1,Year=0}},"review");
        var input=new GuidedReviewInput("M","Chosen Artist","","2024","","","","");Equal(2,new ReviewService(repo,workspace,input).Run());
        True(repo.All().All(x=>x.Effective.Artist=="Chosen Artist"&&x.Effective.AlbumArtist=="Chosen Artist"&&x.Status==ProcessingStatus.Ready));
        Equal(1,input.Output.Count(x=>x.StartsWith("Question — Album artist")));Equal(1,input.Output.Count(x=>x.StartsWith("Question — Album year")));True(repo.All().All(x=>x.Effective.Year==2024));True(!input.Output.Any(x=>x.Contains("Disc number")));Equal(before,Snapshot(f.Source));
    }
    static void GuidedReviewCompilationAndNumericTitle()
    {
        foreach(var alias in new[]{"Various","V.A.","various artists","Diverse Interpreten"})True(ReviewService.CompilationAlbumArtist(alias));
        True(!ReviewService.CompilationAlbumArtist("Various Cruelties"));
        var f=Fixture();var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);var row=GuidedReviewFixture(repo,f);
        repo.Save(row with{Effective=row.Effective with{Title="99,5"}},"numeric title");
        var input=new GuidedReviewInput("M","Various","","","","Actual Artist");
        Equal(1,new ReviewService(repo,workspace,input).Run());var result=repo.All().Single();
        Equal("Various Artists",result.Effective.AlbumArtist);Equal("Actual Artist",result.Effective.Artist);True(!ManualMetadataResolver.NeedsInput(result.Effective));
        var resolved=new ManualMetadataResolver(new StubMetadataResolver(),new ManualResolutionStore(workspace,f.Source)).ResolveAsync(result.Basic,[]).GetAwaiter().GetResult();
        True(!ManualMetadataResolver.NeedsInput(resolved));
    }
    sealed class StubMetadataResolver:IMetadataResolver
    {
        public Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)=>Task.FromResult(file);
    }
    static void GuidedReviewChoicesPlaybackAndQuit()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);var row=GuidedReviewFixture(repo,f);var played=0;
        repo.Save(row with{Effective=row.Effective with{Identification=row.Effective.Identification! with{Candidates=[new(TagMetadata.From(row.Effective),"","",.8,new())]}}},"candidate");
        var input=new GuidedReviewInput("P","Q");Equal(0,new ReviewService(repo,workspace,input,_=>played++).Run());Equal(1,played);Equal(ProcessingStatus.NeedsReview,repo.All().Single().Status);
        True(input.Output.Any(x=>x.Contains("Option 1 — online metadata suggestion")));True(input.Output.Any(x=>x.Contains("Numbers 1–1")));True(!input.Output.Any(x=>x.Contains("Accept candidate 1")));
        Equal(1,new ReviewService(repo,workspace,new GuidedReviewInput("S")).Run());Equal(ProcessingStatus.Skipped,repo.All().Single().Status);
    }
}
