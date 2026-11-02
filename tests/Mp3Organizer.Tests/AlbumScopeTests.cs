using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void AlbumScopeIncludesNestedOnlyAfterConfirmation()
    {
        var f=Fixture();var parent=Path.Combine(f.Source,"Soundtracks");var child=Path.Combine(parent,"Child");Directory.CreateDirectory(child);File.Copy(f.File,Path.Combine(parent,"a.wav"));File.Copy(f.File,Path.Combine(child,"b.wav"));
        var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Album="",AlbumArtist="",Year=0}},"fixture");
        var a=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="a.wav");var b=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="b.wav");
        var setup=new AlbumSetupService(repo,ws,new GuidedReviewInput("Various Artists","Soundtracks","",""));True(setup.Ensure(a));Equal(1,setup.Context(a)!.ReviewFileIds!.Length);True(setup.Context(b)==null);Equal("",repo.Find(b.Id)!.Effective.Album);
        // Reproduce a saved broad decision from the faulty version.
        var store=new ManualResolutionStore(ws,f.Source);var data=store.Load();var key=data.Folders.Keys.Single();data.Folders[key]=data.Folders[key] with{ReviewFileIds=[a.Id,b.Id]};store.Save(data);
        var stopped=new AlbumSetupService(repo,ws,new GuidedReviewInput("/q"));True(!stopped.Ensure(b));
        var repair=new AlbumSetupService(repo,ws,new GuidedReviewInput("Composer","Child OST","",""));True(repair.Ensure(b));
        var fixedRow=repo.Find(b.Id)!;Equal("Child OST",fixedRow.Effective.Album);Equal(b.Effective.Title,fixedRow.Effective.Title);Equal(b.Effective.Artist,fixedRow.Effective.Artist);Equal(b.Status,fixedRow.Status);
        Equal("Soundtracks",repo.Find(a.Id)!.Effective.Album);Equal(child,repair.Context(b)!.LastKnownPath);
        var restart=new GuidedReviewInput();True(new AlbumSetupService(repo,ws,restart).Ensure(fixedRow));Equal(0,restart.Output.Count);
    }
}
