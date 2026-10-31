using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void DirectReviewLookupAndChangedAlbumScope()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();
        Equal(row.CurrentPath,repo.Find(row.Id)!.CurrentPath);True(repo.Find("absent")==null);
        repo.Save(MakeReady(row),"ready");Equal(ProcessingStatus.Ready,repo.Find(row.Id)!.Status);
        var store=new ManualResolutionStore(ws,f.Source);var data=store.Load();var decision=new ManualOverride{LastKnownPath=f.Source,ReviewFileIds=[row.Id],AlbumSetupConfirmed=true,Album="First",AlbumArtist="Singer",Year=2000};data.Folders["folder:performance"]=decision;store.Save(data);
        var setup=new AlbumSetupService(repo,ws,new GuidedReviewInput());True(setup.Ensure(repo.Find(row.Id)!));Equal("First",repo.Find(row.Id)!.Effective.Album);
        True(setup.Ensure(repo.Find(row.Id)!));
        data=store.Load();data.Folders["folder:performance"]=decision with{Album="Second"};store.Save(data);True(setup.Ensure(repo.Find(row.Id)!));Equal("Second",repo.Find(row.Id)!.Effective.Album);
        data=store.Load();data.Folders["folder:performance"]=decision;store.Save(data);True(setup.Ensure(repo.Find(row.Id)!));Equal("First",repo.Find(row.Id)!.Effective.Album);
        for(var i=0;i<200;i++)repo.Save(row with{Id="perf-"+i,CurrentPath=Path.Combine(f.Source,"benchmark-"+i+".wav")},"benchmark");
        var timer=System.Diagnostics.Stopwatch.StartNew();for(var i=0;i<20;i++)_ =repo.All().Single(x=>x.Id==row.Id);var all=timer.Elapsed.TotalMilliseconds;
        timer.Restart();for(var i=0;i<20;i++)_ =repo.Find(row.Id);Console.WriteLine($"LOOKUP BENCHMARK (201 tracks, 20 reads): full inventory {all:F1} ms; direct {timer.Elapsed.TotalMilliseconds:F1} ms.");
    }
}