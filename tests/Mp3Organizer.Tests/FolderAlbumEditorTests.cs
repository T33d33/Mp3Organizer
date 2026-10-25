using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void FolderAlbumActionScopedPersistentAndCancelable()
    {
        var f=Fixture();var folder=Path.Combine(f.Source,"Album");Directory.CreateDirectory(Path.Combine(folder,"Child"));File.Copy(f.File,Path.Combine(folder,"a.wav"));File.Copy(f.File,Path.Combine(folder,"b.wav"));File.Copy(f.File,Path.Combine(folder,"Child","c.wav"));
        var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var before=Snapshot(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=Path.GetFileName(row.CurrentPath)=="a.wav"?ProcessingStatus.NeedsReview:ProcessingStatus.Processed,Effective=MakeReady(row).Effective with{Album="Christmas",AlbumArtist="Various Artists",Year=2000}},"old review");
        var store=new ManualResolutionStore(ws,f.Source);var data=store.Load();data.Folders["folder:parent"]=new(){LastKnownPath=f.Source,AlbumSetupConfirmed=true,ReviewFileIds=repo.All().Select(x=>x.Id).ToArray(),Album="Christmas",AlbumArtist="Various Artists",Year=2000};store.Save(data);
        var current=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="a.wav");var original=File.ReadAllText(store.FilePath);
        var cancel=new FolderAlbumEditor(repo,ws,new GuidedReviewInput("Changed","/cancel")).Edit(current);True(!cancel.Saved);Equal(original,File.ReadAllText(store.FilePath));
        var input=new GuidedReviewInput("B","Ryszard Rynkowski","Koledy","1997","/back","1998","","/q");new CandidateReviewService(repo,ws,input,albumFirst:true).Run();
        var direct=repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==folder).ToList();True(direct.All(x=>x.Effective.Album=="Koledy"&&x.Effective.AlbumArtist=="Ryszard Rynkowski"&&x.Effective.Year==1998));True(direct.All(x=>x.Effective.Artist=="Test Artist"&&x.Effective.Title=="Test Song"&&x.Effective.Track==1));Equal(ProcessingStatus.NeedsReview,direct.Single(x=>x.Id==current.Id).Status);True(repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)!=folder).All(x=>x.Effective.Album=="Christmas"));
        var resolver=new ManualMetadataResolver(new StubMetadataResolver(),new ManualResolutionStore(ws,f.Source));Equal("Koledy",resolver.ResolveAsync(current.Basic,[]).GetAwaiter().GetResult().Album);Equal("Christmas",resolver.ResolveAsync(repo.All().Single(x=>x.CurrentPath==f.File).Basic,[]).GetAwaiter().GetResult().Album);True(input.Output.Any(x=>x.Contains("[B] Edit album")));Equal(before,Snapshot(f.Source));
    }
    static void FolderAlbumCorrectsProcessedTarget()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);repo.Save(MakeReady(repo.All().Single()),"ready");var before=Snapshot(f.Source);
        var run=new RunPipelineService(repo,ws);var targets=run.Targets(f.Target);run.ApplyReady(targets,f.Reports);var current=repo.All().Single();
        True(new FolderAlbumEditor(repo,ws,new GuidedReviewInput("Ryszard Rynkowski","Koledy","1998","")).Edit(current).Saved);
        run.ApplyReady(targets,f.Reports);var mapping=repo.Mappings(f.Target).Single();True(mapping.RelativePath.Contains(Path.Combine("Ryszard Rynkowski","1998 - Koledy")));True(File.Exists(Path.Combine(f.Target,mapping.RelativePath)));Equal(ProcessingStatus.Processed,repo.All().Single().Status);Equal(before,Snapshot(f.Source));
    }
}
