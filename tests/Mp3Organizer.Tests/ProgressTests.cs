using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static string ProgressWorkspace(FixturePaths f)=>Path.Combine(f.Reports,"persistent-workspace");
    sealed class ProgressResolver:IMetadataResolver
    {
        public int Calls;public bool FailFirst;
        public Task<AudioMetadata> ResolveAsync(AudioMetadata row,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
        {
            Calls++;if(FailFirst&&Calls==1)throw new IOException("Synthetic analysis failure");
            return Task.FromResult(row with{Track=1,Identification=new(){Original=TagMetadata.From(row),Status="Resolved"}});
        }
    }
    static FixturePaths ProgressFixture(int count=3)
    {
        var f=Fixture();for(var i=1;i<count;i++)File.Copy(f.File,Path.Combine(f.Source,$"track-{i}.wav"));return f;
    }
    static void ProgressLimitAndRestart()
    {
        var f=ProgressFixture();var resolver=new ProgressResolver();string id;
        using(var repo=new ProgressRepository(ProgressWorkspace(f)))
        {
            var service=new IncrementalProgressService(repo);Equal(3,service.Scan(f.Source).Added);
            Equal(2,service.AnalyzeAsync(2,_=>resolver).GetAwaiter().GetResult());Equal(2,resolver.Calls);
            Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.Ready));id=repo.All()[0].Id;
        }
        using(var repo=new ProgressRepository(ProgressWorkspace(f)))
        {
            var service=new IncrementalProgressService(repo);Equal(id,repo.All()[0].Id);
            Equal(1,service.AnalyzeAsync(2,_=>resolver).GetAwaiter().GetResult());Equal(3,resolver.Calls);
            Equal(0,service.AnalyzeAsync(200,_=>resolver).GetAwaiter().GetResult());Equal(3,resolver.Calls);
        }
    }
    static void ProgressProcessedNeverRepeated()
    {
        var f=Fixture();using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        var row=repo.All().Single();repo.Save(row with{Status=ProcessingStatus.Processed},"test processed");
        Equal(0,new IncrementalProgressService(repo).AnalyzeAsync(100,_=>throw new Exception("Must not resolve processed file")).GetAwaiter().GetResult());
    }
    static void ProgressScanPreservesAndSkipsReads()
    {
        var f=Fixture();var reads=0;using var repo=new ProgressRepository(ProgressWorkspace(f));
        AudioMetadata Reader(string path){reads++;return new TagLibMetadataReader(new ReadOnlySource()).Read(path);}
        var service=new IncrementalProgressService(repo,Reader);service.Scan(f.Source);service.AnalyzeAsync(1,_=>new ProgressResolver()).GetAwaiter().GetResult();
        var before=repo.All().Single();Equal(1,service.Scan(f.Source).Unchanged);Equal(1,reads);Equal(before.Status,repo.All().Single().Status);
        service.Scan(f.Source,true);Equal(2,reads);Equal(before.Status,repo.All().Single().Status);Equal(before.LastProcessedUtc,repo.All().Single().LastProcessedUtc);
        File.SetLastWriteTimeUtc(f.File,File.GetLastWriteTimeUtc(f.File).AddMinutes(1));service.Scan(f.Source);Equal(3,reads);Equal(before.Status,repo.All().Single().Status);
        using(var stream=new FileStream(f.File,FileMode.Open,FileAccess.Write)){stream.Position=stream.Length-1;stream.WriteByte(123);}
        service.Scan(f.Source,true);Equal(ProcessingStatus.Discovered,repo.All().Single().Status);Equal(before.Id,repo.All().Single().Id);Equal(before.LastProcessedUtc,repo.All().Single().LastProcessedUtc);
    }
    static void ProgressResetFileAndFolder()
    {
        var f=Fixture();var sub=Path.Combine(f.Source,"album");var adjacent=Path.Combine(f.Source,"album-extra");Directory.CreateDirectory(sub);Directory.CreateDirectory(adjacent);
        File.Copy(f.File,Path.Combine(sub,"one.wav"));File.Copy(f.File,Path.Combine(adjacent,"two.wav"));
        using var repo=new ProgressRepository(ProgressWorkspace(f));var service=new IncrementalProgressService(repo);service.Scan(f.Source);service.AnalyzeAsync(10,_=>new ProgressResolver()).GetAwaiter().GetResult();
        var reset=new ProgressResetService(repo);Equal(1,reset.Reset("reset-file",f.File));Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.Ready));
        Equal(1,reset.Reset("reset-folder",sub));Equal(ProcessingStatus.Ready,repo.All().Single(x=>x.CurrentPath.StartsWith(adjacent)).Status);
        var original=repo.All().Select(x=>(x.Id,x.CurrentPath,JsonFormat.Serialize(x.Basic))).ToList();Equal(3,reset.Reset("reset-progress"));
        True(repo.All().All(x=>x.Status==ProcessingStatus.Discovered&&x.LastProcessedUtc==null));True(original.SequenceEqual(repo.All().Select(x=>(x.Id,x.CurrentPath,JsonFormat.Serialize(x.Basic)))));
    }
    static void ProgressErrorsAndReviewReset()
    {
        var f=ProgressFixture();using var repo=new ProgressRepository(ProgressWorkspace(f));var service=new IncrementalProgressService(repo);service.Scan(f.Source);
        var resolver=new ProgressResolver{FailFirst=true};Equal(3,service.AnalyzeAsync(10,_=>resolver).GetAwaiter().GetResult());Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Error));True(repo.All().Any(x=>x.ErrorMessage.Contains("Synthetic")));
        var good=repo.All().Last();repo.Save(good with{Status=ProcessingStatus.NeedsReview},"review");var reset=new ProgressResetService(repo);
        Equal(1,reset.Reset("reset-errors"));Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.NeedsReview));Equal(1,reset.Reset("reset-review"));Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Ready));
    }
    static void ProgressBackupBeforeReset()
    {
        var f=Fixture();var before=Snapshot(f.Source);using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        var reset=new ProgressResetService(repo);var folder=Path.Combine(ProgressWorkspace(f),"backups");
        True(!reset.ResetAll(()=>{True(Directory.GetFiles(folder,"*.db").Length==1);return "reset";},_=>{}));Equal(1,repo.All().Count);
        True(reset.ResetAll(()=>{Equal(2,Directory.GetFiles(folder,"*.db").Length);return "RESET";},_=>{}));Equal(0,repo.All().Count);
        foreach(var file in Directory.GetFiles(folder,"*.db")){using var backup=new SqliteDatabase(file);Equal("1",backup.Query("SELECT count(*) FROM music_files")[0][0]);}
        Equal(before,Snapshot(f.Source));
    }
    static void ProgressRenameIdentityAndDuplicates()
    {
        var f=Fixture();using var repo=new ProgressRepository(ProgressWorkspace(f));var service=new IncrementalProgressService(repo);service.Scan(f.Source);service.AnalyzeAsync(1,_=>new ProgressResolver()).GetAwaiter().GetResult();var old=repo.All().Single();
        var renamed=Path.Combine(f.Source,"renamed.wav");File.Move(f.File,renamed);service.Scan(f.Source);var updated=repo.All().Single();Equal(old.Id,updated.Id);Equal(f.File,updated.OriginalPath);Equal(renamed,updated.CurrentPath);Equal(old.Status,updated.Status);
        File.Copy(renamed,f.File);service.Scan(f.Source);Equal(2,repo.All().Count);True(repo.All().Select(x=>x.Id).Distinct().Count()==2);
    }
    static void ProgressEachFileCommitted()
    {
        var f=ProgressFixture();using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        var notifications=0;
        Throws<IOException>(()=>new IncrementalProgressService(repo).AnalyzeAsync(3,_=>new ProgressResolver(),message=>{if(message.StartsWith("Ready:")&&++notifications==1)throw new IOException("Simulated interruption after commit");}).GetAwaiter().GetResult());
        using var reopened=new ProgressRepository(ProgressWorkspace(f));Equal(1,reopened.All().Count(x=>x.Status==ProcessingStatus.Ready));
        Equal(2,new IncrementalProgressService(reopened).AnalyzeAsync(3,_=>new ProgressResolver()).GetAwaiter().GetResult());
    }
    static void ProgressCliAndSourceSafety()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);var before=Snapshot(f.Source);
        Equal(0,Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",workspace]));
        Equal(0,Mp3Organizer.Program.Run(["analyze","--limit","1","--workspace",workspace]));
        Equal(0,Mp3Organizer.Program.Run(["status","--workspace",workspace]));
        Equal(0,Mp3Organizer.Program.Run(["reset-progress","--workspace",workspace]));
        Throws<IOException>(()=>Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",f.Source]));Equal(before,Snapshot(f.Source));
        Throws<ArgumentException>(()=>Mp3Organizer.Program.Run(["analyze","--limit","0","--workspace",workspace]));
    }
    static void ProgressSeparateProcess()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);
        using(var repo=new ProgressRepository(workspace)){var service=new IncrementalProgressService(repo);service.Scan(f.Source);service.AnalyzeAsync(1,_=>new ProgressResolver()).GetAwaiter().GetResult();}
        var info=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(typeof(TestRunner).Assembly.Location);info.ArgumentList.Add("--progress-probe");info.ArgumentList.Add(workspace);
        using var process=System.Diagnostics.Process.Start(info)!;True(process.WaitForExit(15000));Equal(0,process.ExitCode);
    }
    static void ProgressRecorderIntegration()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);AudioMetadata row;
        using(var repo=new ProgressRepository(workspace)){new IncrementalProgressService(repo).Scan(f.Source);row=repo.All().Single().Basic with{Track=1,Identification=new(){Status="ManualResolved"}};}
        ProgressResultRecorder.Record(workspace,f.Source,[row]);
        using(var repo=new ProgressRepository(workspace))Equal(ProcessingStatus.Ready,repo.All().Single().Status);
        ProgressResultRecorder.Record(workspace,f.Source,[row],true);
        using(var repo=new ProgressRepository(workspace))Equal(ProcessingStatus.Processed,repo.All().Single().Status);
        ProgressResultRecorder.Record(workspace,f.Source,[row]);
        using(var repo=new ProgressRepository(workspace))Equal(ProcessingStatus.Processed,repo.All().Single().Status);
    }
    static void ProgressBackupFailureSafe()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
        File.WriteAllText(Path.Combine(workspace,"backups"),"pre-existing file");var prompted=false;
        Throws<IOException>(()=>new ProgressResetService(repo).ResetAll(()=>{prompted=true;return "RESET";},_=>{}));
        True(!prompted);Equal(1,repo.All().Count);
    }
}