using System.Net;
using System.Net.Http;
using System.Text;
using Mp3Organizer;

namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    static string Workspace(FixturePaths f)=>Path.Combine(f.Reports,"workspace");
    static ManualResolutionStore ManualStore(FixturePaths f)=>new(Workspace(f),f.Source,f.Target);
    static AudioMetadata Tagged(FixturePaths f)=>new TagLibMetadataReader(new ReadOnlySource()).Read(f.File);
    static string FileKey(AudioMetadata row)=>"sha256:"+row.Sha256.ToLowerInvariant();
    static void SqlitePersistenceAndSchema()
    {
        var f=Fixture();var clock=new FakeClock();var cache=Cache(f,clock);var row=Tagged(f);cache.Observe(row);cache.Put("chromaprint-v1-length120:"+row.Sha256,JsonFormat.Serialize(Fingerprint),TimeSpan.MaxValue);
        var loaded=Cache(f,clock);True(loaded.Get("chromaprint-v1-length120:"+row.Sha256)!=null);
        Equal("SQLite format 3\0",Encoding.ASCII.GetString(File.ReadAllBytes(cache.DatabasePath).Take(16).ToArray()));
        using var db=new SqliteDatabase(cache.DatabasePath);Equal("4",db.Query("SELECT count(*) FROM sqlite_master WHERE type='table'")[0][0]);Equal(row.Sha256,db.Query("SELECT sha256 FROM source_files")[0][0]);Equal("Chromaprint/2",db.Query("SELECT algorithm FROM fingerprints")[0][0]);
    }
    static void SqliteNegativeExpiry()
    {
        foreach(var body in new[]{"{\"status\":\"ok\",\"results\":[]}","{\"status\":\"ok\",\"results\":[{\"id\":\"a\",\"score\":0.99,\"recordings\":[{\"id\":\""+RecordingId+"\"},{\"id\":\""+OtherRecordingId+"\"}]}]}"})
        {
            var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response(body)));using var http=new HttpClient(handler);var cache=new IdentificationCache(Path.Combine(f.Reports,"cache"),f.Source,f.Target,clock){NegativeRetryAge=TimeSpan.FromDays(2)};
            var client=new AcoustIdClient(Transport(http,cache,clock),"test");client.LookupAsync(Fingerprint,true).GetAwaiter().GetResult();client.LookupAsync(Fingerprint,true).GetAwaiter().GetResult();Equal(1,handler.Calls);
            clock.Delay(TimeSpan.FromDays(3),default).GetAwaiter().GetResult();client.LookupAsync(Fingerprint,true).GetAwaiter().GetResult();Equal(2,handler.Calls);
        }
    }
    static void SqliteFailureRetry()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response("",HttpStatusCode.ServiceUnavailable)));using var http=new HttpClient(handler);var cache=Cache(f,clock);var transport=Transport(http,cache,clock);
        transport.GetAsync("failure",Request,true,default).GetAwaiter().GetResult();Equal("TransientFailure",cache.Status("http-v1:failure"));transport.GetAsync("failure",Request,true,default).GetAwaiter().GetResult();Equal(4,handler.Calls);
        clock.Delay(TimeSpan.FromMinutes(6),default).GetAwaiter().GetResult();transport.GetAsync("failure",Request,true,default).GetAwaiter().GetResult();Equal(8,handler.Calls);
    }
    static void SqliteSuccessfulIndefinite()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response("{}")));using var http=new HttpClient(handler);var transport=Transport(http,Cache(f,clock),clock);
        transport.GetAsync("success",Request,true,default).GetAwaiter().GetResult();clock.Delay(TimeSpan.FromDays(1000),default).GetAwaiter().GetResult();True(transport.GetAsync("success",Request,true,default).GetAwaiter().GetResult().CacheHit);Equal(1,handler.Calls);
    }
    static void SqliteForcedRefresh()
    {
        var f=Fixture();var clock=new FakeClock();var handler=new MockHttp(_=>Task.FromResult(Response("{}")));using var http=new HttpClient(handler);Transport(http,Cache(f,clock),clock).GetAsync("refresh",Request,true,default).GetAwaiter().GetResult();
        var force=new IdentificationCache(Path.Combine(f.Reports,"cache"),f.Source,f.Target,clock){RefreshIdentification=true};var service=Transport(http,force,clock);True(!service.GetAsync("refresh",Request,true,default).GetAwaiter().GetResult().CacheHit);service.GetAsync("refresh",Request,true,default).GetAwaiter().GetResult();Equal(2,handler.Calls);
    }
    sealed class FakeGenerator:IFingerprintGenerator
    {
        public int Calls;
        public Task<AudioFingerprint> GenerateAsync(AudioMetadata file,CancellationToken ct){Calls++;return Task.FromResult(Fingerprint with{GeneratorVersion="mock/1"});}
    }
    static void FingerprintForcedRebuild()
    {
        var f=Fixture();var row=Tagged(f);var generator=new FakeGenerator();var first=new ChromaprintFingerprintService("unused",Cache(f),generator);
        True(!first.FingerprintAsync(row,true).GetAwaiter().GetResult()!.CacheHit);True(first.FingerprintAsync(row,true).GetAwaiter().GetResult()!.CacheHit);Equal(1,generator.Calls);
        var forced=new ChromaprintFingerprintService("unused",new IdentificationCache(Path.Combine(f.Reports,"cache"),f.Source,f.Target){RebuildFingerprints=true},generator);
        True(!forced.FingerprintAsync(row,true).GetAwaiter().GetResult()!.CacheHit);Equal(2,generator.Calls);True(forced.FingerprintAsync(row,true).GetAwaiter().GetResult()!.CacheHit);
    }
    static void FingerprintChangedFileNewIdentity()
    {
        var f=Fixture();var cache=Cache(f);var generator=new FakeGenerator();var service=new ChromaprintFingerprintService("unused",cache,generator);var old=Tagged(f);service.FingerprintAsync(old,true).GetAwaiter().GetResult();
        using(var stream=new FileStream(f.File,FileMode.Open,FileAccess.Write)){stream.Position=stream.Length-1;stream.WriteByte(12);}File.SetLastWriteTimeUtc(f.File,old.LastWriteTimeUtc);
        var changed=Tagged(f);Equal(old.Size,changed.Size);True(old.Sha256!=changed.Sha256);service.FingerprintAsync(changed,true).GetAwaiter().GetResult();Equal(2,generator.Calls);
    }
    sealed class IdentityResolver:IMetadataResolver
    {
        public Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)=>Task.FromResult(file with{Identification=file.Identification??new(){Original=TagMetadata.From(file)}});
    }
    static AudioMetadata ManualResolve(ManualResolutionStore store,AudioMetadata row,IReadOnlyList<AudioMetadata>? neighbors=null)=>new ManualMetadataResolver(new IdentityResolver(),store).ResolveAsync(row,neighbors??[row]).GetAwaiter().GetResult();
    static void ManualFolderPersistence()
    {
        var f=Fixture();var store=ManualStore(f);var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Artist="Chosen",Album="Chosen Album"};store.Save(data);
        Equal("Chosen",ManualStore(f).Load().Folders["folder:one"].Artist);Equal("Chosen",ManualResolve(ManualStore(f),Tagged(f)).Artist);
    }
    static void ManualFilePersistence()
    {
        var f=Fixture();var row=Tagged(f);var store=ManualStore(f);var data=store.Load();data.Files[FileKey(row)]=new(){LastKnownPath=f.File,Title="Checkmate",TrackNumber=1};store.Save(data);
        Equal("Checkmate",ManualResolve(ManualStore(f),row).Title);Equal((uint)1,ManualResolve(store,row).Track);
    }
    static void ManualPriority()
    {
        var f=Fixture();var store=ManualStore(f);var row=Tagged(f) with{Track=1};var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Artist="Manual",Album="Manual Album"};store.Save(data);
        var result=new ManualMetadataResolver(new CorrectingResolver(),store).ResolveAsync(row,[row],true).GetAwaiter().GetResult();Equal("Manual",result.Artist);Equal("Manual Album",result.Album);Equal(MetadataSource.FolderManualOverride,result.Identification!.Source);True(result.Identification.ManualOverrideHit);
    }
    static void ManualFileOverFolder()
    {
        var f=Fixture();var row=Tagged(f);var store=ManualStore(f);var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Artist="Folder",Album="Album"};data.Files[FileKey(row)]=new(){LastKnownPath=f.File,Artist="File"};store.Save(data);
        var result=ManualResolve(store,row);Equal("File",result.Artist);Equal("Album",result.Album);Equal(MetadataSource.FileManualOverride,result.Identification!.Source);
    }
    static void ManualFolderPropagationAndChanges()
    {
        var f=Fixture();var row=Tagged(f);var store=ManualStore(f);var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Album="Fixed"};data.Files[FileKey(row)]=new(){LastKnownPath=f.File,Title="Original content only"};store.Save(data);
        var changed=row with{Sha256=new('B',64),Title="New content"};Equal("Fixed",ManualResolve(store,changed).Album);Equal("New content",ManualResolve(store,changed).Title);
        Equal("Fixed",ManualResolve(store,changed with{FullPath=Path.Combine(f.Source,"other.wav")}).Album);
    }
    static void ManualFolderRelocation()
    {
        var f=Fixture();var row=Tagged(f);var store=ManualStore(f);var data=store.Load();data.Folders["folder:stable"]=new(){LastKnownPath=Path.Combine(root,"old-folder"),AnchorSha256=[row.Sha256],Album="Same album"};store.Save(data);
        Equal("Same album",ManualResolve(store,row).Album);
    }
    sealed class ScriptConsole(params string[] answers):IResolutionConsole
    {
        private readonly Queue<string> inputs=new(answers);public List<string> Prompts=new();public List<string> Output=new();
        public void Write(string text)=>Output.Add(text);
        public ResolutionInput Read(string prompt,string suggestion){Prompts.Add(prompt);if(inputs.Count==0)throw new Exception("Unexpected prompt: "+prompt);var value=inputs.Dequeue();return value=="<ESC>"?new(true,""):new(false,value==""?suggestion:value);}
    }
    static AudioMetadata Unresolved(FixturePaths f)=>Tagged(f) with{Artist="Cypress Hill",Album="Kingpin OST",Title="track01",Track=0,Year=0,FileName="01 - track01.mp3"};
    static void ManualSkipFolder()
    {
        var f=Fixture();var store=ManualStore(f);new InteractiveResolution(store,new ScriptConsole("<ESC>")).Run([Unresolved(f)],true,false);True(!File.Exists(store.FilePath));
    }
    static void ManualAbortTracksKeepsProgress()
    {
        var f=Fixture();var first=Unresolved(f);var second=first with{Sha256=new('C',64),FullPath=Path.Combine(f.Source,"second.wav"),FileName="02 - track02.mp3",Title="track02"};var store=ManualStore(f);
        new InteractiveResolution(store,new ScriptConsole("","","","","Checkmate","1","<ESC>")).Run([first,second],true,false);
        var saved=ManualStore(f).Load();Equal(1,saved.Files.Count);Equal("Checkmate",saved.Files[FileKey(first)].Title);Equal((uint?)1,saved.Files[FileKey(first)].TrackNumber);Equal(1,saved.Folders.Count);
    }
    static void ManualAcceptDefaultsNoRepeat()
    {
        var f=Fixture();var row=Unresolved(f) with{Title="Song",Track=1,Identification=new(){Status="Review",ReviewReason="Ambiguous release"}};var store=ManualStore(f);var input=new ScriptConsole("","","","");new InteractiveResolution(store,input).Run([row],true,false);
        var data=store.Load();Equal("Cypress Hill",data.Folders.Values.Single().Artist);Equal("Kingpin OST",data.Folders.Values.Single().Album);Equal(0,data.Files.Count);
        var again=new ScriptConsole();new InteractiveResolution(store,again).Run([row],true,false);Equal(0,again.Prompts.Count);
    }
    static void ManualEditExisting()
    {
        var f=Fixture();var row=Tagged(f);var store=ManualStore(f);var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Artist="Old",Album="Old"};data.Files[FileKey(row)]=new(){LastKnownPath=f.File,Title="Old",TrackNumber=1};store.Save(data);
        new InteractiveResolution(store,new ScriptConsole("","New Artist","New Album","2021","New Title","2")).Run([row],false,true);
        data=store.Load();Equal("New Artist",data.Folders["folder:one"].Artist);Equal("New Title",data.Files[FileKey(row)].Title);Equal((uint?)2,data.Files[FileKey(row)].TrackNumber);
    }
    static void ManualDirectEditReload()
    {
        var f=Fixture();var row=Tagged(f);var store=ManualStore(f);var data=store.Load();data.Files[FileKey(row)]=new(){LastKnownPath=f.File,Title="Before"};store.Save(data);var resolver=new ManualMetadataResolver(new IdentityResolver(),store);
        Equal("Before",resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult().Title);File.WriteAllText(store.FilePath,File.ReadAllText(store.FilePath).Replace("Before","After direct edit"),new UTF8Encoding(false));
        Equal("After direct edit",resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult().Title);Equal("After direct edit",ManualResolve(ManualStore(f),row).Title);
    }
    static void ManualInvalidJson()
    {
        var f=Fixture();var store=ManualStore(f);Directory.CreateDirectory(Workspace(f));File.WriteAllText(store.FilePath,"{broken");Throws<IOException>(()=>store.Load());Throws<IOException>(()=>store.Save(new()));Equal("{broken",File.ReadAllText(store.FilePath));
        Throws<IOException>(()=>new ManualMetadataResolver(new IdentityResolver(),store));
    }
    static void ManualSchemaValidation()
    {
        var f=Fixture();var store=ManualStore(f);Directory.CreateDirectory(Workspace(f));
        foreach(var text in new[]{"{}","{\"schemaVersion\":2,\"folders\":{},\"files\":{}}","{\"schemaVersion\":1,\"folders\":{},\"files\":{},\"unknown\":1}","{\"schemaVersion\":1,\"schemaVersion\":1,\"folders\":{},\"files\":{}}"}){File.WriteAllText(store.FilePath,text);Throws<IOException>(()=>store.Load());}
        var data=new ManualResolutions();data.Files["sha256:bad"]=new(){LastKnownPath=f.File};Throws<IOException>(()=>ManualResolutionStore.Validate(data));
    }
    static void ManualAtomicBackup()
    {
        var f=Fixture();var store=ManualStore(f);var data=store.Load();store.Save(data);var before=File.ReadAllText(store.FilePath);data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Album="Björk"};store.Save(data);
        var backups=Directory.GetFiles(Workspace(f),"manual-resolutions.json.bak-*");Equal(1,backups.Length);Equal(before,File.ReadAllText(backups[0]));Equal(0,Directory.GetFiles(Workspace(f),"*.tmp-*").Length);True(!File.ReadAllBytes(store.FilePath).Take(3).SequenceEqual(new byte[]{239,187,191}));True(File.Exists(Path.ChangeExtension(store.FilePath,".csv")));
    }
    static void ManualConcurrentEditRefused()
    {
        var f=Fixture();var store=ManualStore(f);var data=store.Load();store.Save(data);File.AppendAllText(store.FilePath," ");var edited=File.ReadAllText(store.FilePath);Throws<IOException>(()=>store.Save(data));Equal(edited,File.ReadAllText(store.FilePath));
    }
    static void ManualValidationReferences()
    {
        var f=Fixture();var store=ManualStore(f);var data=store.Load();data.Files["sha256:"+new string('D',64)]=new(){LastKnownPath=f.File};var result=store.ValidateReferences(data);Equal(1,result.FileCount);Equal(1,result.UnresolvedReferences.Count);
    }
    static void ManualSourceImmutable()
    {
        var f=Fixture();File.SetAttributes(f.File,File.GetAttributes(f.File)|FileAttributes.ReadOnly);var before=Snapshot(f.Source);new InteractiveResolution(ManualStore(f),new ScriptConsole("","","","","Song","1")).Run([Unresolved(f)],true,false);Equal(before,Snapshot(f.Source));
        Throws<IOException>(()=>new ManualResolutionStore(f.Source,f.Source));Throws<IOException>(()=>new IdentificationCache(f.Source,f.Source));
    }
    static void ManualStalePlanRejected()
    {
        var f=Fixture();var store=ManualStore(f);var plan=new CopyPlanBuilder().Build(f.Source,f.Target);plan.ManualFilePath=store.FilePath;plan.ManualFileRevision=store.Revision();store.Save(store.Load());Throws<IOException>(()=>new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false));True(!Directory.Exists(f.Target));
    }
    static void ManualTargetPlaylistRegeneration()
    {
        var f=Fixture();var plan=new CopyPlanBuilder().Build(f.Source,f.Target);new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false);var store=ManualStore(f);var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Artist="Manual Target Artist",Album="Manual Target Album"};store.Save(data);
        Equal(0,Mp3Organizer.Program.Run(["playlists",f.Target,"--workspace",Workspace(f)]));True(Directory.GetFiles(Path.Combine(f.Target,"_Playlists","Artists")).Any(x=>x.Contains("Manual Target Artist")));
    }
    static void ManualReportsFlags()
    {
        var f=Fixture();var row=Tagged(f);new IdentificationReportWriter().Write(f.Reports,f.Source,[row]);var header=File.ReadLines(Path.Combine(f.Reports,"identification.csv")).First();foreach(var name in new[]{"FingerprintCacheHit","IdentificationCacheHit","ManualOverrideHit","ResolutionStatus"})True(header.Contains(name));
    }
    static void AutomaticCacheRenameReuse()
    {
        var f=Fixture();var cache=Cache(f);var row=Row() with{Album="Album",Sha256=new('A',64)};var fp=new FakeFingerprint();var resolver=new CachedMetadataResolver(Resolver(fp),cache,true);resolver.ResolveAsync(row,[row],true).GetAwaiter().GetResult();
        var renamed=row with{FullPath=Path.Combine(f.Source,"renamed.wav"),FileName="renamed.wav"};var result=resolver.ResolveAsync(renamed,[renamed]).GetAwaiter().GetResult();True(result.Identification!.IdentificationCacheHit);Equal(1,fp.Calls);
    }
    static void ManualAbortMidTrackNoRepeatedTitle()
    {
        var f=Fixture();var store=ManualStore(f);var row=Unresolved(f);
        new InteractiveResolution(store,new ScriptConsole("","","","","Saved title","<ESC>")).Run([row],true,false);
        var again=new ScriptConsole("","1");new InteractiveResolution(store,again).Run([row],true,false);Equal(2,again.Prompts.Count);True(!again.Prompts.Any(x=>x.StartsWith("Title")));Equal("Saved title",store.Load().Files[FileKey(row)].Title);
    }
    static void ManualRefreshCannotOverride()
    {
        var f=Fixture();var row=Tagged(f) with{Track=1};var store=ManualStore(f);var data=store.Load();data.Folders["folder:manual"]=new(){LastKnownPath=f.Source,Artist="User Artist",Album="User Album"};store.Save(data);
        var result=new ManualMetadataResolver(new CorrectingResolver(),store,true).ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal("User Artist",result.Artist);Equal("User Album",result.Album);Equal(MetadataSource.FolderManualOverride,result.Identification!.Source);
    }
    static void ManualRemovalRestoresAutomatic()
    {
        var f=Fixture();var row=Tagged(f) with{Track=1};var store=ManualStore(f);var data=store.Load();data.Folders["folder:one"]=new(){LastKnownPath=f.Source,Artist="Manual"};store.Save(data);
        var overridden=ManualResolve(store,row);Equal("Manual",overridden.Artist);data=store.Load();data.Folders.Clear();store.Save(data);
        var restored=ManualResolve(store,overridden);Equal("Test Artist",restored.Artist);True(restored.Identification?.ManualOverrideHit!=true);
    }
    static void ManualValidateCommand()
    {
        var f=Fixture();var store=ManualStore(f);store.Save(store.Load());Equal(0,Mp3Organizer.Program.Run(["manual","validate","--workspace",Workspace(f)]));True(File.Exists(Path.Combine(Workspace(f),"manual-resolutions.csv")));
    }
    static void ManualAcceptsUtf8Bom()
    {
        var f=Fixture();var store=ManualStore(f);Directory.CreateDirectory(Workspace(f));File.WriteAllText(store.FilePath,"{\"schemaVersion\":1,\"folders\":{},\"files\":{}}",new UTF8Encoding(true));Equal(0,store.Load().Folders.Count);
    }
    static void AutomaticNegativeResultPersistence()
    {
        var f=Fixture();var clock=new FakeClock();var cache=Cache(f,clock);var acoustic=new FakeAcoust{Value=new([])};var row=Poor() with{Sha256=new('E',64)};
        var resolver=new CachedMetadataResolver(Resolver(ac:acoustic),cache,true);var first=resolver.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal("NoMatch",first.Identification!.LookupStatus);
        var later=new CachedMetadataResolver(Resolver(ac:acoustic),Cache(f,clock),true);var second=later.ResolveAsync(row,[row]).GetAwaiter().GetResult();True(second.Identification!.IdentificationCacheHit);Equal(1,acoustic.Calls);
        clock.Delay(TimeSpan.FromDays(31),default).GetAwaiter().GetResult();later.ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal(2,acoustic.Calls);
    }
    static void SqlitePersistenceAcrossProcesses()
    {
        var f=Fixture();var cache=Cache(f);cache.Put("chromaprint-v1-length120:"+new string('A',64),JsonFormat.Serialize(Fingerprint),TimeSpan.MaxValue);
        var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(typeof(TestRunner).Assembly.Location);start.ArgumentList.Add("--cache-probe");start.ArgumentList.Add(cache.DatabasePath);start.ArgumentList.Add(f.Source);
        using var child=System.Diagnostics.Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();if(!child.WaitForExit(30000)){child.Kill(true);throw new Exception("Cache probe timed out");}Equal(0,child.ExitCode);True(stdout.GetAwaiter().GetResult().Contains("cache-persisted"));stderr.GetAwaiter().GetResult();
    }
    static void LegacyFingerprintImport()
    {
        var f=Fixture();var cache=Cache(f);var key="chromaprint-v1-length120:"+new string('A',64);var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));Directory.CreateDirectory(Path.GetDirectoryName(cache.DatabasePath)!);
        var legacy=Path.Combine(Path.GetDirectoryName(cache.DatabasePath)!,digest+".json");File.WriteAllText(legacy,JsonFormat.Serialize(new{Expires=DateTimeOffset.UtcNow.AddDays(1),Value=JsonFormat.Serialize(Fingerprint)}));True(cache.Get(key)!=null);True(File.Exists(cache.DatabasePath));True(File.Exists(legacy));
    }
}
