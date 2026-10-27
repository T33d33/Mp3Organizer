using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void KeepFoldersUsesNormalPipelineAndPlaylists()
    {
        var f=Fixture();var a=Path.Combine(f.Source,"Christmas","Mazowsze");var b=Path.Combine(f.Source,"Christmas","Other");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
        var first=Path.Combine(a,"01 - Song.wav");File.Move(f.File,first);File.Copy(first,Path.Combine(b,"Same audio.wav"));
        var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);OutputLayout.Configure(["output-layout","folders","--workspace",ws]);
        using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);foreach(var row in repo.All())repo.Save(MakeReady(row),"ready");
        var service=new RunPipelineService(repo,ws);var targets=service.Targets(f.Target);Equal(2,service.ApplyReady(targets,f.Reports));
        True(File.Exists(Path.Combine(f.Target,"Christmas","Mazowsze","01 - Song.wav")));True(File.Exists(Path.Combine(f.Target,"Christmas","Other","Same audio.wav")));Equal(2,repo.Mappings(f.Target).Select(x=>x.RelativePath).Distinct().Count());
        True(Directory.GetFiles(Path.Combine(f.Target,"_Playlists","Folders"),"*.m3u8").Length>=2);
        var item=repo.All().First();repo.Save(item with{Effective=item.Effective with{Album="Corrected album",AlbumArtist="Another artist",Year=2003}},"album edit");
        Equal(0,service.ApplyReady(targets,f.Reports));True(File.Exists(Path.Combine(f.Target,"Christmas","Mazowsze","01 - Song.wav")));Equal(before,Snapshot(f.Source));
        var blocked=false;try{OutputLayout.Configure(["output-layout","albums","--workspace",ws]);}catch(IOException){blocked=true;}True(blocked);
    }
    static void KeepFoldersAlbumSetupIsDirectAndControlsRemain()
    {
        var f=Fixture();var child=Path.Combine(f.Source,"Child");Directory.CreateDirectory(child);File.Copy(f.File,Path.Combine(child,"Other.wav"));var ws=ProgressWorkspace(f);OutputLayout.Configure(["output-layout","folders","--workspace",ws]);
        using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{AlbumArtist="",Album="",Year=0}},"needs album");
        var parent=repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==f.Source);
        var input=new GuidedReviewInput("Singer","Parent album","2000","");True(new AlbumSetupService(repo,ws,input).Ensure(parent));
        Equal("",repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==child).Effective.Album);
        True(input.Output.Any(x=>x.Contains("EXCLUDING child folders")));
        var controls=new GuidedReviewInput("/q");new CandidateReviewService(repo,ws,controls,albumFirst:false).Run();True(controls.Output.Any(x=>x.Contains("[M]")));True(controls.Output.Any(x=>x.Contains("[F]")));True(controls.Output.Any(x=>x.Contains("[B]")));
    }
}
