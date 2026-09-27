using System.Net;
using System.Net.Http;
using System.Text;
using Mp3Organizer;

namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    const string RecordingId = "11111111-1111-4111-8111-111111111111";
    const string OtherRecordingId = "22222222-2222-4222-8222-222222222222";
    const string ReleaseId = "33333333-3333-4333-8333-333333333333";
    static readonly AudioFingerprint Fingerprint = new(100,"FAKE-COMPRESSED-FINGERPRINT");
    sealed class FakeFingerprint : IAudioFingerprintService
    {
        public int Calls; public AudioFingerprint? Value = Fingerprint;
        public Task<AudioFingerprint?> FingerprintAsync(AudioMetadata file,bool allowCompute,CancellationToken ct=default) {Calls++;return Task.FromResult(Value);}
    }
    sealed class FakeAcoust : IAcoustIdClient
    {
        public int Calls; public AcousticLookup Value = new([new("acoust-id",.99,RecordingId)]);
        public Task<AcousticLookup> LookupAsync(AudioFingerprint f,bool online,CancellationToken ct=default) {Calls++;return Task.FromResult(Value);}
    }
    sealed class FakeBrainz : IMusicBrainzClient
    {
        public int Calls; public RecordingLookup Value = new(new(RecordingId,"Test Performer","Song",100),[new(ReleaseId,"Test Record","Test Performer",2000,1,1)]);
        public Task<RecordingLookup> LookupAsync(string id,bool online,CancellationToken ct=default) {Calls++;return Task.FromResult(Value);}
    }
    static MetadataResolver Resolver(FakeFingerprint? fp=null,FakeAcoust? ac=null,FakeBrainz? mb=null) => new(fp??new(),ac??new(),mb??new(),true,true);
    static AudioMetadata Resolve(MetadataResolver resolver,AudioMetadata row,bool force=false,IReadOnlyList<AudioMetadata>? neighbors=null) => resolver.ResolveAsync(row,neighbors??[row],force).GetAwaiter().GetResult();
    static AudioMetadata Poor() => Row(artist:"") with {Album="Test Record",Year=2000,Track=1};
    static void ResolutionTagsPriority()
    {
        var fp=new FakeFingerprint();var ac=new FakeAcoust();var mb=new FakeBrainz();var row=Row(artist:"Test Performer") with{Album="Test Record"};
        var result=Resolve(Resolver(fp,ac,mb),row);Equal(0,fp.Calls);Equal(0,ac.Calls);Equal(TagMetadata.From(row),TagMetadata.From(result));Equal(MetadataSource.Tags,result.Identification!.Source);
    }
    static void ResolutionForcedPreservesTags()
    {
        var fp=new FakeFingerprint();var row=Row(artist:"Test Performer") with{Album="Original Good Album",Year=1998};var result=Resolve(Resolver(fp),row,true);
        Equal(1,fp.Calls);Equal(TagMetadata.From(row),TagMetadata.From(result));True(result.Identification!.ConfidentRecording);
    }
    static void ResolutionLowScore()
    {
        var ac=new FakeAcoust {Value=new([new("id",.7,RecordingId)])};var mb=new FakeBrainz();var r=Resolve(Resolver(ac:ac,mb:mb),Poor());
        Equal(0,mb.Calls);Equal("Review",r.Identification!.Status);Equal("",r.Identification.RecordingId);
    }
    static void ResolutionAmbiguous()
    {
        var ac=new FakeAcoust {Value=new([new("a",.99,RecordingId),new("b",.97,OtherRecordingId)])};var mb=new FakeBrainz();var r=Resolve(Resolver(ac:ac,mb:mb),Poor());
        Equal(2,mb.Calls);Equal(2,r.Identification!.Candidates.Count);True(r.Identification.ReviewReason.Contains("Ambiguous"));Equal("",r.Identification.RecordingId);
    }
    static void ResolutionReleaseSelection()
    {
        var mb=new FakeBrainz {Value=new(new(RecordingId,"Test Performer","Song",100),[new(ReleaseId,"Test Record","Test Performer",2000,1,1),new("reissue","Test Record","Test Performer",2020,1,1),new("compilation","Greatest Hits","Various Artists",2000,1,1,"Official",true)])};
        var r=Resolve(Resolver(mb:mb),Poor());Equal(ReleaseId,r.Identification!.ReleaseId);Equal("Test Performer",r.Artist);Equal((uint)2000,r.Year);Equal(MetadataSource.AcoustIdMusicBrainz,r.Identification.Source);Equal("",r.Identification.Original.Artist);
    }
    static void ResolutionReleaseAmbiguous()
    {
        var mb=new FakeBrainz {Value=new(new(RecordingId,"Test Performer","Song",100),[new("one","Test Record","Test Performer",2000,1,1),new("two","Test Record","Test Performer",2000,1,1)])};
        var r=Resolve(Resolver(mb:mb),Poor());Equal("Test Performer",r.Artist);Equal("Test Record",r.Album);Equal("Review",r.Identification!.Status);Equal("",r.Identification.ReleaseId);
    }
    static void ResolutionNoArbitraryCompilation()
    {
        var mb=new FakeBrainz {Value=new(new(RecordingId,"Test Performer","Song",100),[new(ReleaseId,"Greatest Hits","Various Artists",2020,1,1,"Official",true)])};
        var r=Resolve(Resolver(mb:mb),Poor() with{Album=""});Equal("",r.Album);Equal("Test Performer",r.Artist);True(r.Identification!.ConfidentRecording);
    }
    static void ResolutionNeighborAlbum()
    {
        var row=Poor() with{Album=""};var neighbors=new[]{row,Row("b.wav") with{Album="Test Record"},Row("c.wav") with{Album="Test Record"}};
        Equal("",Resolve(Resolver(),row,neighbors:neighbors).Album);
    }
    static void ResolutionGoodTagConflict()
    {
        var row=Poor() with{Artist="Different Artist",Album=""};var r=Resolve(Resolver(),row);Equal("Different Artist",r.Artist);Equal("Review",r.Identification!.Status);True(!r.Identification.ConfidentRecording);
    }
    static void ResolutionDurationConflict()
    {
        var mb=new FakeBrainz {Value=new(new(RecordingId,"Test Performer","Song",160),[])};var r=Resolve(Resolver(mb:mb),Poor());True(!r.Identification!.ConfidentRecording);True(r.Identification.ReviewReason.Contains("duration"));
    }
    static void FallbackNoiseAndSoundtrack()
    {
        var path=Path.Combine(root,"Cypress Hill - Kingpin OST-uppedByTeedee","01 - Song.mp3");var row=Row(path,artist:"",title:"");
        var r=new FilenameMetadataFallback().Resolve(row);Equal("",r.Artist);Equal("",r.Album);Equal("Song",r.Title);Equal((uint)1,r.Track);
        r=new FilenameMetadataFallback().Resolve(Row(Path.Combine(root,"Cuphead OST","Song.mp3"),artist:""));Equal("",r.Album);Equal("",r.Artist);
        True(!FilenameMetadataFallback.Clean("Album [320kbps]-uppedByUser").Contains("320"));
    }
    static void QualityTriggers()
    {
        var evaluator=new MetadataQualityEvaluator();var good=Row(artist:"Test Performer") with{Album="Test Record"};Equal(0,evaluator.Evaluate(good,[good]).Count);
        foreach(var row in new[]{good with{Artist="Unknown Artist"},good with{Title="song.mp3"},good with{Album=""},good with{Title="uppedByBob"}})True(evaluator.Evaluate(row,[row]).Count>0);
        var conflict=good with{Album="Other"};Equal(0,evaluator.Evaluate(conflict,[conflict,good with{FullPath="b"},good with{FullPath="c"},good with{FullPath="d"}]).Count);
    }
    sealed class FakeClock : ILookupClock
    {
        public DateTimeOffset Now {get;private set;}=DateTimeOffset.Parse("2026-01-01T00:00:00Z");public List<TimeSpan> Delays=new();
        public Task Delay(TimeSpan duration,CancellationToken ct) {ct.ThrowIfCancellationRequested();Delays.Add(duration);Now+=duration;return Task.CompletedTask;}
    }
    sealed class MockHttp(Func<HttpRequestMessage,Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {Calls++;return reply(request);}
    }
    static HttpResponseMessage Response(string body,HttpStatusCode code=HttpStatusCode.OK) => new(code){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    static IdentificationCache Cache(FixturePaths f,FakeClock? clock=null) => new(Path.Combine(f.Reports,"cache"),f.Source,f.Target,clock);
    static IdentificationHttp Transport(HttpClient http,IdentificationCache cache,FakeClock clock,int ms=350) => new(http,cache,new RequestRateLimiter(TimeSpan.FromMilliseconds(ms),clock),clock);
    static Func<HttpRequestMessage> Request => ()=>new(HttpMethod.Get,"https://example.invalid/mock");
    static void OfflineNeverCallsHttp()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>throw new Exception("Network forbidden"));using var http=new HttpClient(handler);
        var result=Transport(http,Cache(f,clock),clock).GetAsync("offline",Request,false,default).GetAwaiter().GetResult();True(result.Error.Contains("Offline"));Equal(0,handler.Calls);
        var fp=new FakeFingerprint{Value=null};var ac=new FakeAcoust();var row=Resolve(new MetadataResolver(fp,ac,new FakeBrainz(),false,false),Poor());Equal(0,ac.Calls);Equal("Review",row.Identification!.Status);
    }
    static void FingerprintCacheReuse()
    {
        var f=Fixture();var cache=Cache(f);var row=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File);cache.Put("chromaprint-v1-length120:"+row.Sha256,JsonFormat.Serialize(Fingerprint),TimeSpan.FromDays(1));
        var service=new ChromaprintFingerprintService("DOES-NOT-EXIST",cache);Equal(Fingerprint with{CacheHit=true},service.FingerprintAsync(row,true).GetAwaiter().GetResult());
    }
    static void HttpRateLimits()
    {
        foreach(var interval in new[]{350,1100})
        {
            var f=Fixture();var clock=new FakeClock();var starts=new List<DateTimeOffset>();var handler=new MockHttp(_=>{starts.Add(clock.Now);return Task.FromResult(Response("{}"));});using var http=new HttpClient(handler);var service=Transport(http,Cache(f,clock),clock,interval);
            for(var i=0;i<5;i++) service.GetAsync("limit"+i,Request,true,default).GetAwaiter().GetResult();
            for(var i=1;i<starts.Count;i++)True((starts[i]-starts[i-1]).TotalMilliseconds>=interval);
        }
    }
    static void HttpRetryBackoff()
    {
        var f=Fixture();var clock=new FakeClock();var count=0;var handler=new MockHttp(_=>Task.FromResult(++count<4?Response("",HttpStatusCode.ServiceUnavailable):Response("{}")));using var http=new HttpClient(handler);
        var result=Transport(http,Cache(f,clock),clock,1100).GetAsync("retry",Request,true,default).GetAwaiter().GetResult();Equal("",result.Error);Equal(4,handler.Calls);True(clock.Delays.Contains(TimeSpan.FromSeconds(2)));True(clock.Delays.Contains(TimeSpan.FromSeconds(4)));
    }
    static void HttpNegativeCache()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response("{\"status\":\"ok\",\"results\":[]}")));using var http=new HttpClient(handler);var cache=Cache(f,clock);
        var client=new AcoustIdClient(Transport(http,cache,clock),"test-key");Equal(0,client.LookupAsync(Fingerprint,true).GetAwaiter().GetResult().Candidates.Count);
        client=new AcoustIdClient(Transport(http,cache,clock),"");Equal(0,client.LookupAsync(Fingerprint,false).GetAwaiter().GetResult().Candidates.Count);Equal(1,handler.Calls);
    }
    static void HttpTransientFailureCache()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response("",HttpStatusCode.ServiceUnavailable)));using var http=new HttpClient(handler);var service=Transport(http,Cache(f,clock),clock);
        True(service.GetAsync("fail",Request,true,default).GetAwaiter().GetResult().Error!="");service.GetAsync("fail",Request,true,default).GetAwaiter().GetResult();Equal(4,handler.Calls);
    }
    static void HttpPayloadPrivacy()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(async request=>
        {
            Equal(HttpMethod.Post,request.Method);Equal("api.acoustid.org",request.RequestUri!.Host);
            var body=await request.Content!.ReadAsStringAsync();var keys=body.Split('&').Select(x=>x.Split('=')[0]).Order().ToArray();Equal("client,duration,fingerprint,format,meta",string.Join(",",keys));True(!body.Contains(f.File));True(!body.Contains("RIFF"));
            return Response("{\"status\":\"ok\",\"results\":[{\"id\":\"a\",\"score\":0.99,\"recordings\":[{\"id\":\""+RecordingId+"\"}]}]}");
        });using var http=new HttpClient(handler);var cache=Cache(f,clock);var client=new AcoustIdClient(Transport(http,cache,clock),"private-test-key");
        Equal(RecordingId,client.LookupAsync(Fingerprint,true).GetAwaiter().GetResult().Candidates.Single().RecordingId);
        foreach(var path in Directory.GetFiles(Path.Combine(f.Reports,"cache")))True(!File.ReadAllText(path).Contains("private-test-key"));
    }
    static string RecordingJson => "{\"id\":\""+RecordingId+"\",\"title\":\"Song\",\"length\":100000,\"artist-credit\":[{\"name\":\"Artist\"}]}";
    static string ReleasesJson => "{\"release-count\":1,\"releases\":[{\"id\":\""+ReleaseId+"\",\"title\":\"Album\",\"date\":\"2000-02-03\",\"status\":\"Official\",\"artist-credit\":[{\"name\":\"Artist\"}],\"media\":[{\"position\":1,\"tracks\":[{\"position\":2,\"recording\":{\"id\":\""+RecordingId+"\"}}]}]}]}";
    static void HttpMusicBrainzParse()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(request=>{True(request.Headers.UserAgent.ToString().Contains("Mp3Organizer/1.1"));return Task.FromResult(Response(request.RequestUri!.AbsolutePath.Contains("/recording/")?RecordingJson:ReleasesJson));});using var http=new HttpClient(handler);var cache=Cache(f,clock);
        var client=new MusicBrainzClient(Transport(http,cache,clock,1100),"Mp3Organizer/1.1 (test@example.invalid)");var r=client.LookupAsync(RecordingId,true).GetAwaiter().GetResult();Equal("Artist",r.Recording!.Artist);Equal((uint)2,r.Releases.Single().Track);Equal((uint)2000,r.Releases.Single().Year);
        client.LookupAsync(RecordingId,false).GetAwaiter().GetResult();Equal(2,handler.Calls);True(clock.Delays.Any(x=>x.TotalMilliseconds>=1100));
    }
    static void HttpMalformedResponse()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response("garbage")));using var http=new HttpClient(handler);
        var result=new AcoustIdClient(Transport(http,Cache(f,clock),clock),"test").LookupAsync(Fingerprint,true).GetAwaiter().GetResult();True(result.Error!="");Equal(4,handler.Calls);
    }
    static void HttpMusicBrainzFailure()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(request=>Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/recording/")?Response(RecordingJson):Response("",HttpStatusCode.ServiceUnavailable)));using var http=new HttpClient(handler);
        var r=new MusicBrainzClient(Transport(http,Cache(f,clock),clock,1100),"Mp3Organizer/1.1 (test@example.invalid)").LookupAsync(RecordingId,true).GetAwaiter().GetResult();True(r.Recording!=null);Equal(0,r.Releases.Count);True(r.Error!="");
    }
    static void RecordingIdentityDuplicates()
    {
        var evidence=new IdentificationEvidence {RecordingId=RecordingId,ConfidentRecording=true};
        var rows=new[]{Row(hash:new('A',64)) with{Identification=evidence,Lossless=true},Row("b.wav",title:"Different title",hash:new('B',64)) with{Identification=evidence,Lossless=false},Row("live.wav",hash:new('C',64)) with{Identification=evidence with{RecordingId=OtherRecordingId}}};
        var result=Detector().Select(rows);Equal(3,result.Selected.Count);Equal(2,result.Decisions.Count(x=>x.GroupId=="Recording-"+RecordingId));True(result.Decisions.Any(x=>x.Reason.Contains("Quality preference")));True(!result.Decisions.Any(x=>x.Source=="live.wav" && x.GroupId.StartsWith("Recording-")));
    }
    static void ResolutionSourceImmutable()
    {
        var f=Fixture();File.SetAttributes(f.File,File.GetAttributes(f.File)|FileAttributes.ReadOnly);var before=Snapshot(f.Source);var row=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File) with{Artist="",Title="",Album="Test Record",Year=2000,Track=1};
        var r=Resolve(Resolver(),row);Equal("Test Performer",r.Artist);Equal(before,Snapshot(f.Source));
    }
    sealed class CorrectingResolver : IMetadataResolver
    {
        public Task<AudioMetadata> ResolveAsync(AudioMetadata row,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default) => Task.FromResult(row with{Artist="Resolved Artist",TrackArtists=["Resolved Artist"],AlbumArtist="Resolved Artist",Album="Resolved Album",Identification=new(){Original=TagMetadata.From(row),Source=MetadataSource.AcoustIdMusicBrainz,RecordingId=RecordingId,ConfidentRecording=true}});
    }
    static void TargetEffectiveMetadataPersists()
    {
        var f=Fixture();var before=Snapshot(f.Source);var p=new CopyPlanBuilder().Build(f.Source,f.Target,new CorrectingResolver());new SafeCopyExecutor().Execute(p,f.Source,f.Target,false);
        var playlist=p.Playlists.Single(x=>x.RelativePath.Contains("Artists"+Path.DirectorySeparatorChar));True(playlist.RelativePath.Contains("Resolved Artist"));
        Equal(0,Mp3Organizer.Program.Run(["playlists",f.Target]));Equal(playlist.Content,File.ReadAllText(Path.Combine(f.Target,playlist.RelativePath)));Equal(before,Snapshot(f.Source));
        var copied=new AudioFileScanner().Scan(f.Target).Single();Equal("Test Artist",new TagLibMetadataReader(new ReadOnlySource()).Read(copied).Artist);
    }
    static void IdentificationReportFields()
    {
        var f=Fixture();var r=Resolve(Resolver(),Poor());new IdentificationReportWriter().Write(f.Reports,f.Source,[r]);var text=File.ReadAllText(Path.Combine(f.Reports,"identification.csv"));True(text.Contains("OriginalArtist"));True(text.Contains("ResolvedDisc"));True(text.Contains(RecordingId));True(text.Contains("AcoustIdMusicBrainz"));
    }
    static void IdentificationCacheSafety() {var f=Fixture();Throws<IOException>(()=>new IdentificationCache(f.Source,f.Source));Throws<IOException>(()=>new IdentificationCache(f.Target,f.Source,f.Target));}
    static void IdentifyOfflineCli()
    {
        var f=Fixture();var before=Snapshot(f.Source);Equal(0,Mp3Organizer.Program.Run(["identify",f.Source,"--offline","--reports",f.Reports]));Equal(before,Snapshot(f.Source));True(Directory.GetFiles(f.Reports,"identification.csv",SearchOption.AllDirectories).Length==1);True(!Directory.Exists(f.Target));
    }
    static void ResolutionCacheReusedOffline()
    {
        var f=Fixture();var cache=Cache(f);var fp=new FakeFingerprint();var row=Row(artist:"Test Performer") with{Album="Test Record",Sha256=new('A',64)};
        var first=new CachedMetadataResolver(Resolver(fp),cache,true).ResolveAsync(row,[row],true).GetAwaiter().GetResult();True(first.Identification!.ConfidentRecording);
        var offline=new CachedMetadataResolver(new MetadataResolver(new FakeFingerprint{Value=null},new FakeAcoust(),new FakeBrainz(),false,false),cache,false);
        var second=offline.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal(JsonFormat.Serialize(first.Identification! with{IdentificationCacheHit=true}),JsonFormat.Serialize(second.Identification));Equal(1,fp.Calls);
        var changed=row with{Album="Changed Album"};var third=offline.ResolveAsync(changed,[changed]).GetAwaiter().GetResult();True(third.Identification?.ConfidentRecording!=true);
    }
    static void HttpMusicBrainzPagination()
    {
        var f=Fixture();var clock=new FakeClock();var queries=new List<string>();var handler=new MockHttp(request=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/recording/"))return Task.FromResult(Response(RecordingJson));
            queries.Add(request.RequestUri.Query);
            var json=ReleasesJson.Replace("\"release-count\":1","\"release-count\":2");
            if(queries.Count==2)json=json.Replace(ReleaseId,"44444444-4444-4444-8444-444444444444");
            return Task.FromResult(Response(json));
        });using var http=new HttpClient(handler);
        var r=new MusicBrainzClient(Transport(http,Cache(f,clock),clock,1100),"Mp3Organizer/1.1 (test@example.invalid)").LookupAsync(RecordingId,true).GetAwaiter().GetResult();
        Equal(2,r.Releases.Count);True(queries[1].Contains("offset=1"));
    }
    static void NeighborYearSelectsRelease()
    {
        var row=Poor() with{Year=0,Album=""};var neighbors=new[]{row,Row("b") with{Album="Test Record",Year=2000},Row("c") with{Album="Test Record",Year=2000}};
        var release=MetadataResolver.ChooseRelease(row,neighbors,[new("original","Test Record","Test Performer",2000,1,1),new("reissue","Test Record","Test Performer",2020,1,1)]);
        True(release==null);
    }
    static void LosslessPropertyPreference() => Equal<int?>(1,new AudioQualityComparer().Compare(Row(artist:"Test Performer") with{Lossless=true,SampleRate=96000,BitsPerSample=24},Row(artist:"Test Performer") with{Lossless=true,SampleRate=44100,BitsPerSample=16}));
    static void UncertainQualityKeepsBoth()
    {
        var e=new IdentificationEvidence{RecordingId=RecordingId,ConfidentRecording=true};var rows=new[]{Row(hash:new('A',64)) with{Identification=e},Row("b",hash:new('B',64)) with{Identification=e}};
        var result=Detector().Select(rows);Equal(2,result.Selected.Count);True(result.Decisions.Where(x=>x.GroupId.StartsWith("Recording-")).All(x=>x.Decision=="KeepBoth/Review"));
    }
    static void ResolutionPreservesGoodFieldSpelling()
    {
        var result=Resolve(Resolver(),Row(artist:"test performer",title:"song"));Equal("test performer",result.Artist);Equal("song",result.Title);True(result.Identification!.ConfidentRecording);
    }
    static void FingerprintRejectsChangedSource()
    {
        var f=Fixture();var row=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File);var cache=Cache(f);
        using(var file=new FileStream(f.File,FileMode.Append))file.WriteByte(42);
        var before=Snapshot(f.Source);
        Throws<IOException>(()=>new ChromaprintFingerprintService("DOES-NOT-EXIST",cache).FingerprintAsync(row,true).GetAwaiter().GetResult());
        Equal(before,Snapshot(f.Source));True(!Directory.Exists(Path.Combine(f.Reports,"cache")));
    }
}

