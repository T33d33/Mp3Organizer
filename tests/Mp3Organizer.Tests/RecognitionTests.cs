using System.Net;
using System.Net.Http;
using System.Text;
using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    sealed class FixedResolver(AudioMetadata result):IMetadataResolver
    {public Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)=>Task.FromResult(result with{FullPath=file.FullPath,FileName=file.FileName,Sha256=file.Sha256});}
    sealed class FakeYear:IAlbumYearClient
    {
        public int Calls;public AlbumYearLookup Result=new([new(ReleaseId,"Test Performer","Test Record",1991,["Album"])]);
        public Task<AlbumYearLookup> LookupAsync(string artist,string album,bool online,CancellationToken ct=default){Calls++;return Task.FromResult(Result);}
    }
    static AudioMetadata CompleteYearRow()=>Row(artist:"Test Performer") with{Album="Test Record",Track=1,Year=0,Identification=new(){Status="TagsAccepted"}};
    static void RecognitionStrongAndOriginalYear()
    {
        var mb=new FakeBrainz{Value=new(new(RecordingId,"Test Performer","Song",100),[new(ReleaseId,"Test Record","Test Performer",2021,1,1,GroupId:OtherRecordingId,OriginalYear:1991)])};
        var row=Poor() with{Year=0};var actual=Resolve(Resolver(mb:mb),row);
        True(actual.Identification!.AutoRecognized);Equal("Resolved",actual.Identification.Status);Equal((uint)1991,actual.Year);True(actual.Identification.RecognitionConfidence>=.78);
    }
    static void YearReliablePreservedAndAmbiguous()
    {
        var row=CompleteYearRow();var years=new FakeYear();var resolver=new YearEnrichmentResolver(new FixedResolver(row),years,true);
        var actual=resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal((uint)1991,actual.Year);True(actual.Identification!.YearEnriched);Equal("MusicBrainz",actual.Identification.YearSource);
        var existing=row with{Year=2021};actual=new YearEnrichmentResolver(new FixedResolver(existing),years,true).ResolveAsync(existing,[existing]).GetAwaiter().GetResult();Equal((uint)2021,actual.Year);Equal(1,years.Calls);
        years.Result=new([new(ReleaseId,"Test Performer","Test Record",1991,["Album"]),new(OtherRecordingId,"Test Performer","Test Record",2000,["Album"])]);
        actual=resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal((uint)0,actual.Year);Equal("Review",actual.Identification!.Status);True(actual.Identification.ReviewReasons.Contains("UnknownYear"));Equal(2,actual.Identification.Candidates.Count);
        var fallback=row with{Year=2021,Identification=row.Identification! with{FieldSources=new(){["Year"]="FolderFallback"}}};
        actual=new YearEnrichmentResolver(new FixedResolver(fallback),new FakeYear(),true).ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal((uint)1991,actual.Year);
    }
    static void AlbumYearPersistentReuse()
    {
        var f=Fixture();var clock=new FakeClock();var cache=Cache(f,clock);
        var handler=new MockHttp(_=>Task.FromResult(Response("{\"count\":1,\"release-groups\":[{\"id\":\""+ReleaseId+"\",\"title\":\"Test Record\",\"first-release-date\":\"1991-08-12\",\"primary-type\":\"Album\",\"artist-credit\":[{\"name\":\"Test Performer\"}]}]}")));
        using var http=new HttpClient(handler);var client=new AlbumYearClient(Transport(http,cache,clock),cache,"Mp3OrganizerTests/1.0");
        Equal((uint)1991,client.LookupAsync("Test Performer","Test Record",true).GetAwaiter().GetResult().Candidates.Single().OriginalYear);
        True(client.LookupAsync("test performer","test record",true).GetAwaiter().GetResult().CacheHit);
        var recreated=new AlbumYearClient(Transport(http,cache,clock),cache,"Mp3OrganizerTests/1.0");True(recreated.LookupAsync("Test Performer","Test Record",false).GetAwaiter().GetResult().CacheHit);Equal(1,handler.Calls);
    }
    sealed class FakeCodex:ICodexClient
    {
        public string Model=>"test-model";public int Calls;public CodexServiceException? Failure;public CodexContext? Context;
        public CodexDecision Decision=new("candidate-1",ReleaseId,1991,.99,"Fingerprint, duration and album agree.",false,[]);
        public Task<CodexDecision> DecideAsync(CodexContext context,CancellationToken ct=default){Calls++;Context=context;if(Failure!=null)throw Failure;return Task.FromResult(Decision);}
    }
    static AudioMetadata AmbiguousRow()=>CompleteYearRow() with{Identification=new(){Status="Review",ReviewReason="Ambiguous releases",ReviewReasons=["AmbiguousAlbum"],Candidates=[
        new(new("Test Performer","Test Performer","Test Record","Song",1,1,1991),RecordingId,ReleaseId,.94,new(){["Fingerprint"]=.55,["Duration"]=.15,["Title"]=.08,["Album"]=.06}),
        new(new("Other Performer","Other Performer","Other Record","Song",2,1,2000),OtherRecordingId,"other",.75,new(){["Fingerprint"]=.55,["Duration"]=.15})]}};
    static void CodexRuntimeAndHardValidation()
    {
        var row=AmbiguousRow();var client=new FakeCodex();var resolver=new CodexMetadataResolver(new FixedResolver(row),client,new());
        var result=resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal("Codex",result.Identification!.RecognitionMethod);True(result.Identification.AutoRecognized);Equal((uint)1991,result.Year);Equal(1,client.Calls);True(client.Context!.Candidates.Count==2);
        client.Decision=client.Decision with{Year=1888};result=resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal("Review",result.Identification!.Status);Equal((uint)0,result.Year);
        client.Decision=client.Decision with{Year=1991,SelectedCandidateId="invented"};result=resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult();True(result.Identification!.CodexValidation.Contains("Unknown candidate"));
        var tied=row with{Identification=row.Identification! with{Candidates=row.Identification.Candidates.Select(x=>x with{Confidence=.94}).ToList()}};
        client.Decision=client.Decision with{SelectedCandidateId="candidate-1"};result=new CodexMetadataResolver(new FixedResolver(tied),client,new()).ResolveAsync(tied,[tied]).GetAwaiter().GetResult();Equal("Review",result.Identification!.Status);
        var truncated=row with{Identification=row.Identification! with{CandidatesComplete=false}};result=new CodexMetadataResolver(new FixedResolver(truncated),client,new()).ResolveAsync(truncated,[truncated]).GetAwaiter().GetResult();Equal("Review",result.Identification!.Status);True(result.Identification.CodexValidation.Contains("incomplete"));
    }
    static void CodexSkipsDeterministicAndPreservesYear()
    {
        var good=CompleteYearRow() with{Year=2000};var client=new FakeCodex();new CodexMetadataResolver(new FixedResolver(good),client,new()).ResolveAsync(good,[good]).GetAwaiter().GetResult();Equal(0,client.Calls);
        var row=AmbiguousRow() with{Year=2000};var result=new CodexMetadataResolver(new FixedResolver(row),client,new()).ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal((uint)2000,result.Year);Equal("Review",result.Identification!.Status);
    }
    static void CodexQuotaResumeAndContinue()
    {
        var f=ProgressFixture(3);var workspace=ProgressWorkspace(f);using(var repo=new ProgressRepository(workspace))
        {
            new IncrementalProgressService(repo).Scan(f.Source);new IncrementalProgressService(repo).AnalyzeAsync(1,_=>new ProgressResolver()).GetAwaiter().GetResult();
            var client=new FakeCodex{Failure=new(CodexFailureKind.Quota,"Quota reached")};var state=new CodexRunState();var prompted=0;
            var analyzer=new IncrementalProgressService(repo,codexState:state,continueWithoutCodex:_=>{prompted++;Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Analyzed));return false;});
            Equal(0,analyzer.AnalyzeAsync(10,_=>new CodexMetadataResolver(new FixedResolver(AmbiguousRow()),client,state)).GetAwaiter().GetResult());True(analyzer.StoppedForCodex);Equal(1,client.Calls);Equal(1,prompted);Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Ready));Equal(0,repo.All().Count(x=>x.Status==ProcessingStatus.Error));
        }
        using(var repo=new ProgressRepository(workspace))
        {
            var client=new FakeCodex{Failure=new(CodexFailureKind.Quota,"Quota reached")};var state=new CodexRunState();var prompted=0;
            var analyzer=new IncrementalProgressService(repo,codexState:state,continueWithoutCodex:_=>{prompted++;return true;});
            Equal(2,analyzer.AnalyzeAsync(10,_=>new CodexMetadataResolver(new FixedResolver(AmbiguousRow()),client,state)).GetAwaiter().GetResult());Equal(1,client.Calls);Equal(1,prompted);Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.NeedsReview));
            True(repo.All().Where(x=>x.Status==ProcessingStatus.NeedsReview).All(x=>x.Effective.Identification!.RecognitionMethod=="DeterministicWithoutCodexFallback"&&x.Effective.Identification.CodexUnavailable));
        }
    }
    static void CodexUnavailableContinuesAllFailureKinds()
    {
        foreach(var kind in Enum.GetValues<CodexFailureKind>())
        {
            var f=ProgressFixture(3);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
            var rows=repo.All();repo.Save(MakeReady(rows[0]),"already ready");
            var client=new FakeCodex{Failure=new(kind,"unavailable")};var state=new CodexRunState();var notices=0;
            var analyzer=new IncrementalProgressService(repo,codexState:state,continueWithoutCodex:_=>{notices++;return true;});
            Equal(2,analyzer.AnalyzeAsync(10,_=>new CodexMetadataResolver(new FixedResolver(AmbiguousRow()),client,state)).GetAwaiter().GetResult());
            True(!analyzer.StoppedForCodex);Equal(1,client.Calls);Equal(1,notices);Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.NeedsReview));
            var pipeline=new RunPipelineService(repo,workspace);Equal(1,pipeline.ApplyReady(pipeline.Targets(f.Target),f.Reports));
            Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Processed));
        }
    }
    static void CodexFailureClassificationAndRetries()
    {
        Equal(CodexFailureKind.Quota,CodexClient.Classify(HttpStatusCode.TooManyRequests,"{\"error\":{\"code\":\"insufficient_quota\"}}").Kind);
        Equal(CodexFailureKind.RateLimit,CodexClient.Classify(HttpStatusCode.TooManyRequests,"{}").Kind);Equal(CodexFailureKind.AuthenticationConfiguration,CodexClient.Classify(HttpStatusCode.Unauthorized,"{}").Kind);Equal(CodexFailureKind.InvalidRequest,CodexClient.Classify(HttpStatusCode.BadRequest,"{}").Kind);
        var handler=new MockHttp(_=>Task.FromResult(Response("{}",HttpStatusCode.ServiceUnavailable)));using var http=new HttpClient(handler);var delays=0;
        var client=new CodexClient(http,"test-key","test-model",(_,_)=>{delays++;return Task.CompletedTask;});
        var context=new CodexContext("test.mp3","test",TagMetadata.From(Row()),TagMetadata.From(Row()),100,[],new{},[],"test");
        Throws<CodexServiceException>(()=>client.DecideAsync(context).GetAwaiter().GetResult());Equal(3,handler.Calls);Equal(2,delays);
    }
    static void CodexHttpStructuredAndPrivacy()
    {
        var handler=new MockHttp(async request=>{
            Equal("https://api.openai.com/v1/responses",request.RequestUri!.ToString());var body=await request.Content!.ReadAsStringAsync();True(body.Contains("json_schema"));True(body.Contains("\"store\":false"));True(!body.Contains("input_audio"));True(!body.Contains("test-key"));
            var decision="{\"selectedCandidateId\":null,\"releaseId\":null,\"year\":null,\"confidence\":0.3,\"reason\":\"No factual candidates\",\"humanReviewRequired\":true,\"conflicts\":[]}";
            return Response("{\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":"+System.Text.Json.JsonSerializer.Serialize(decision)+"}]}]}");});
        using var http=new HttpClient(handler);var result=new CodexClient(http,"test-key","test-model").DecideAsync(new("test.mp3","test",TagMetadata.From(Row()),TagMetadata.From(Row()),100,[],new{},[],"none")).GetAwaiter().GetResult();True(result.HumanReviewRequired);Equal(null,result.SelectedCandidateId);
    }
    sealed class ReviewInput(params string[] values):IReviewConsole
    {readonly Queue<string> answers=new(values);public void Write(string text){}public string? Read()=>answers.Count==0?null:answers.Dequeue();}
    static void ReviewPersistedAndNotRepeated()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);var before=Snapshot(f.Source);
        using(var repo=new ProgressRepository(workspace))
        {
            new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();repo.Save(item with{Status=ProcessingStatus.NeedsReview,Effective=item.Basic with{Identification=AmbiguousRow().Identification}},"test");
            Equal(1,new ReviewService(repo,workspace,new ReviewInput("1","YES")).Run());Equal(ProcessingStatus.Ready,repo.All().Single().Status);Equal("Manual",repo.All().Single().Effective.Identification!.RecognitionMethod);
        }
        using(var repo=new ProgressRepository(workspace)){Equal(0,new ReviewService(repo,workspace,new ReviewInput()).Run());Equal(1,new ManualResolutionStore(workspace,f.Source).Load().Files.Count);}Equal(before,Snapshot(f.Source));
    }
    static string SyntheticMp3(FixturePaths f,uint year=0)
    {
        var path=Path.Combine(f.Source,"synthetic.mp3");using(var output=File.Create(path))for(var i=0;i<120;i++){var frame=new byte[417];frame[0]=0xff;frame[1]=0xfb;frame[2]=0x90;output.Write(frame);}
        using(var tags=TagLib.File.Create(path)){tags.Tag.Performers=["Test Performer"];tags.Tag.Album="Test Record";tags.Tag.Title="Song";tags.Tag.Track=1;tags.Tag.Year=year;tags.Save();}return path;
    }
    static void TagsAudioUnchangedBackupAndYear()
    {
        var f=Fixture();var path=SyntheticMp3(f);var audio=Mp3AudioPayload.Hash(path);var bytes=File.ReadAllBytes(path);using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        var item=repo.All().Single(x=>x.CurrentPath==path);var ready=item with{Status=ProcessingStatus.Ready,Effective=item.Basic with{Year=1991,Identification=new(){Status="Resolved",YearEnriched=true,YearConfidence=.95}}};
        var writer=new Mp3TagWriteService(ProgressWorkspace(f),repo);var updated=writer.WriteAndSave(ready);Equal(audio,Mp3AudioPayload.Hash(path));Equal((uint)1991,new TagLibMetadataReader(new ReadOnlySource()).Read(path).Year);
        True(bytes.SequenceEqual(File.ReadAllBytes(Directory.GetFiles(Path.Combine(ProgressWorkspace(f),"tag-writes"),"*.original.mp3").Single())));True(!Directory.GetFiles(f.Source,".mp3organizer-tags-*").Any());
        writer.WriteAndSave(updated with{Effective=updated.Effective with{Year=2021}});Equal((uint)1991,new TagLibMetadataReader(new ReadOnlySource()).Read(path).Year);writer.Recover();
        Throws<IOException>(()=>writer.WriteAndSave(updated with{Status=ProcessingStatus.NeedsReview}));Equal(audio,Mp3AudioPayload.Hash(path));
        var other=SyntheticMp3(Fixture());var original=new TagLibMetadataReader(new ReadOnlySource()).Read(other);
        var unverified=new IndexedMusic(Guid.NewGuid().ToString("N"),Path.GetDirectoryName(other)!,other,other,original,original with{Year=2021,Identification=new(){Status="Resolved",AutoRecognized=true}},ProcessingStatus.Ready,null,"");
        repo.Save(unverified,"test independent indexed MP3");writer.WriteAndSave(unverified);Equal((uint)0,new TagLibMetadataReader(new ReadOnlySource()).Read(other).Year);
    }
    static void YearFailureDoesNotAbortBatch()
    {
        var f=ProgressFixture(2);using var repo=new ProgressRepository(ProgressWorkspace(f));var service=new IncrementalProgressService(repo,p=>new TagLibMetadataReader(new ReadOnlySource()).Read(p) with{Year=0});service.Scan(f.Source);
        var years=new FakeYear{Result=new([],false,"Synthetic unavailable")};Equal(2,service.AnalyzeAsync(10,_=>new YearEnrichmentResolver(new FixedResolver(CompleteYearRow()),years,true)).GetAwaiter().GetResult());True(repo.All().All(x=>x.Status==ProcessingStatus.NeedsReview));
    }
    static void CodexResumeSucceedsWithoutReset()
    {
        var f=ProgressFixture(2);using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo,p=>new TagLibMetadataReader(new ReadOnlySource()).Read(p) with{Artist="Test Performer",AlbumArtist="Test Performer",Title="Song",Album="Test Record",Year=0}).Scan(f.Source);var client=new FakeCodex{Failure=new(CodexFailureKind.AuthenticationConfiguration,"Configure key")};var state=new CodexRunState();
        var analyzer=new IncrementalProgressService(repo,codexState:state,continueWithoutCodex:_=>false);
        analyzer.AnalyzeAsync(10,_=>new CodexMetadataResolver(new FixedResolver(AmbiguousRow()),client,state)).GetAwaiter().GetResult();True(analyzer.StoppedForCodex);Equal(1,client.Calls);
        client.Failure=null;state=new();analyzer=new(repo,codexState:state);Equal(2,analyzer.AnalyzeAsync(10,_=>new CodexMetadataResolver(new FixedResolver(AmbiguousRow()),client,state)).GetAwaiter().GetResult());True(repo.All().All(x=>x.Status==ProcessingStatus.Ready));Equal(3,client.Calls);
        Equal(0,analyzer.AnalyzeAsync(10,_=>throw new Exception("Completed files must not repeat")).GetAwaiter().GetResult());
        File.Copy(f.File,Path.Combine(f.Source,"new.wav"));Equal(1,analyzer.Scan(f.Source).Added);Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.Ready));
    }
    static void CodexPlanUsesPersistedDecision()
    {
        var f=Fixture();using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();
        var accepted=item.Effective with{Title="Accepted title",Identification=new(){RecognitionMethod="Codex",Status="Resolved",CodexModel="test-model",CodexDecision=new("candidate-1",ReleaseId,2000,.99,"Supported",false,[])}};
        repo.Save(item with{Effective=accepted,Status=ProcessingStatus.Ready},"Accepted test");var fallback=new FixedResolver(item.Basic);var resolver=new ProgressMetadataResolver(fallback,repo.All());
        Equal("Accepted title",resolver.ResolveAsync(item.Basic,[]).GetAwaiter().GetResult().Title);
        Equal(item.Basic.Title,resolver.ResolveAsync(item.Basic with{Sha256=new('F',64)},[]).GetAwaiter().GetResult().Title);
        Equal(item.Basic.Title,resolver.ResolveAsync(item.Basic,[],true).GetAwaiter().GetResult().Title);
        var reports=Path.Combine(f.Reports,"new-evidence");new IdentificationReportWriter().Write(reports,f.Source,[accepted]);var csv=File.ReadAllText(Path.Combine(reports,"identification.csv"));True(csv.Contains("CodexModel"));True(csv.Contains("test-model"));
    }
    static void TagJournalRecoveryAndManualIdentity()
    {
        var f=Fixture();var path=SyntheticMp3(f);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single(x=>x.CurrentPath==path);
        var store=new ManualResolutionStore(workspace,f.Source);var manual=store.Load();manual.Files["sha256:"+item.Basic.Sha256.ToLowerInvariant()]=new(){Title="Manual title",Year=1991,LastKnownPath=path};store.Save(manual);
        var writer=new Mp3TagWriteService(workspace,repo);var updated=writer.WriteAndSave(item with{Status=ProcessingStatus.Ready,Effective=item.Basic with{Year=1991,Title="Manual title",Identification=new(){Status="ManualResolved"}}},true);
        var journalPath=Directory.GetFiles(Path.Combine(workspace,"tag-writes"),"*.json").Single();var journal=JsonFormat.Read<TagWriteJournal>(File.ReadAllText(journalPath));File.WriteAllText(journalPath,JsonFormat.Serialize(journal with{State="Prepared"}));
        repo.Save(item,"Simulated interrupted DB commit");writer.Recover();Equal(updated.Basic.Sha256,repo.All().Single(x=>x.CurrentPath==path).Basic.Sha256);True(store.Load().Files.ContainsKey("sha256:"+updated.Basic.Sha256.ToLowerInvariant()));Equal("Committed",JsonFormat.Read<TagWriteJournal>(File.ReadAllText(journalPath)).State);
    }
}
