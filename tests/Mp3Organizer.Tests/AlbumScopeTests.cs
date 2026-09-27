using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void AlbumScopeIncludesNestedOnlyAfterConfirmation()
    {
        var f=Fixture();Directory.CreateDirectory(Path.Combine(f.Source,"Christmas","Child"));Directory.CreateDirectory(Path.Combine(f.Source,"Outside"));
        File.Copy(f.File,Path.Combine(f.Source,"Christmas","a.wav"));File.Copy(f.File,Path.Combine(f.Source,"Christmas","Child","b.wav"));File.Copy(f.File,Path.Combine(f.Source,"Outside","c.wav"));
        var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Album="",AlbumArtist="",Year=0}},"missing album");
        var parent=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="a.wav");var child=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="b.wav");
        var input=new GuidedReviewInput("Various Artists","Christmas","2000","");var setup=new AlbumSetupService(repo,ws,input);True(setup.Ensure(parent));
        Equal(2,setup.Context(parent)!.ReviewFileIds!.Length);True(input.Output.Any(x=>x.Contains("exactly 2 indexed tracks")));
        var restartInput=new GuidedReviewInput();var restart=new AlbumSetupService(repo,ws,restartInput);True(restart.Ensure(child));Equal(0,restartInput.Output.Count);
        var saved=repo.All().Single(x=>x.Id==child.Id);Equal("Various Artists",saved.Effective.AlbumArtist);Equal("Christmas",saved.Effective.Album);Equal(2000u,saved.Effective.Year);Equal("Test Artist",saved.Effective.Artist);Equal("Test Song",saved.Effective.Title);
        var outside=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="c.wav");Equal("",outside.Effective.Album);True(restart.Context(outside)==null);
        var newcomer=outside with{Id=Guid.NewGuid().ToString("N"),CurrentPath=Path.Combine(f.Source,"Christmas","new.wav")};repo.Save(newcomer,"new file outside snapshot");True(restart.Context(newcomer)==null);
        var resolved=new ManualMetadataResolver(new StubMetadataResolver(),new ManualResolutionStore(ws,f.Source)).ResolveAsync(saved.Basic,[]).GetAwaiter().GetResult();Equal("Christmas",resolved.Album);Equal("Various Artists",resolved.AlbumArtist);
        var changed=saved with{Effective=saved.Effective with{Artist="Suidakra"}};repo.Save(changed,"confirmed track artist");restart.ReconcileArtist(changed);Equal("Various Artists",repo.All().Single(x=>x.Id==child.Id).Effective.AlbumArtist);
    }
}
