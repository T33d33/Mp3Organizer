using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void NestedIdentifiedAlbumRepairsOnlyDirectMembers()
    {
        var f=Fixture();var albumFolder=Path.Combine(f.Source,"Christmas","Rokiczanka");Directory.CreateDirectory(albumFolder);Directory.CreateDirectory(Path.Combine(albumFolder,"Child"));
        for(var i=1;i<=5;i++)File.Copy(f.File,Path.Combine(albumFolder,i+".wav"));File.Copy(f.File,Path.Combine(albumFolder,"Child","extra.wav"));
        var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var before=Snapshot(f.Source);
        foreach(var row in repo.All())
        {
            var metadata=MakeReady(row).Effective with{Album="Christmas",AlbumArtist="Various Artists",Artist="Rokiczanka",Year=2000};
            var tags=TagMetadata.From(metadata) with{Album="Hej, Kolęda!",AlbumArtist="Rokiczanka",Year=2018};
            var evidence=metadata.Identification! with{Candidates=Path.GetFileName(row.CurrentPath)=="5.wav"?[]:[new(tags,"","",.9,new())]};
            repo.Save(row with{Status=Path.GetFileName(row.CurrentPath)=="1.wav"?ProcessingStatus.Ready:ProcessingStatus.NeedsReview,Effective=metadata with{Identification=evidence}},"review");
        }
        var store=new ManualResolutionStore(ws,f.Source);var data=store.Load();data.Folders["folder:parent"]=new(){LastKnownPath=f.Source,AlbumSetupConfirmed=true,ReviewFileIds=repo.All().Select(x=>x.Id).ToArray(),Album="Christmas",AlbumArtist="Various Artists",Year=2000};store.Save(data);
        var current=repo.All().Single(x=>Path.GetFileName(x.CurrentPath)=="2.wav");
        True(!new AlbumSetupService(repo,ws,new GuidedReviewInput("/q")).Ensure(current));Equal(1,store.Load().Folders.Count);
        var input=new GuidedReviewInput("");True(new AlbumSetupService(repo,ws,input).Ensure(current));True(input.Output.Any(x=>x.Contains("5 files only")));
        var direct=repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==albumFolder).ToList();True(direct.All(x=>x.Effective.Album=="Hej, Kolęda!"&&x.Effective.Year==2018));Equal(ProcessingStatus.Ready,direct.Single(x=>Path.GetFileName(x.CurrentPath)=="1.wav").Status);True(repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)!=albumFolder).All(x=>x.Effective.Album=="Christmas"));
        var restarted=new GuidedReviewInput();True(new AlbumSetupService(repo,ws,restarted).Ensure(current));Equal(0,restarted.Output.Count);Equal(before,Snapshot(f.Source));
        var conflicting=direct.Select((x,i)=>i==0?x with{Effective=x.Effective with{Identification=x.Effective.Identification! with{Candidates=[new(TagMetadata.From(x.Effective) with{Album="Another album"},"","",.9,new())]}}}:x).ToList();True(AlbumSetupService.SupportedAlbum(conflicting)==null);
    }
}
