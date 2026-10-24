using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void FinishFolderMatchesEnterAndStopsAtBoundary()
    {
        var f=Fixture();var cd4=Path.Combine(f.Source,"MerryChristmasEverybody","CD4");var cd5=Path.Combine(f.Source,"MerryChristmasEverybody","CD5");Directory.CreateDirectory(cd4);Directory.CreateDirectory(cd5);
        File.Copy(f.File,Path.Combine(cd4,"01.wav"));File.Copy(f.File,Path.Combine(cd4,"02.wav"));File.Copy(f.File,Path.Combine(cd5,"01.wav"));
        var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())
        {
            var effective=MakeReady(row).Effective with{Album="Christmas",AlbumArtist="Various Artists",Year=2000};
            if(Path.GetFileName(row.CurrentPath)=="01.wav")effective=effective with{Identification=effective.Identification! with{Candidates=[new(TagMetadata.From(effective) with{Title="Lower score"},"","",.3,new()),new(TagMetadata.From(effective) with{Title="Best fit",Album="Other Album",Year=2009},"","",.9,new())]}};
            repo.Save(row with{Effective=effective,Status=row.CurrentPath==f.File?ProcessingStatus.Skipped:ProcessingStatus.NeedsReview},"review");
        }
        var store=new ManualResolutionStore(ws,f.Source);var manual=store.Load();manual.Folders["folder:scope"]=new(){LastKnownPath=f.Source,AlbumSetupConfirmed=true,ReviewFileIds=repo.All().Select(x=>x.Id).ToArray(),Album="Christmas",AlbumArtist="Various Artists",Year=2000};store.Save(manual);
        var input=new GuidedReviewInput("F","/q");Equal(2,new CandidateReviewService(repo,ws,input,albumFirst:true).Run());
        var finished=repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==cd4).OrderBy(x=>x.CurrentPath).ToArray();True(finished.All(x=>x.Status==ProcessingStatus.Ready));Equal("Best fit",finished[0].Effective.Title);Equal("Test Song",finished[1].Effective.Title);True(finished.All(x=>x.Effective.Album=="Christmas"&&x.Effective.Year==2000));
        Equal(ProcessingStatus.NeedsReview,repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==cd5).Status);
        True(input.Output.Any(x=>x.Contains("[F] Finish (MerryChristmasEverybody")));Equal(before,Snapshot(f.Source));
    }
}
