using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void AlbumSetupBeforeTracksPersistentAndScoped()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Album="",AlbumArtist="",Year=0}},"missing album");
        var input=new GuidedReviewInput("","Christmas Album","2012","","","D");
        Equal(1,new CandidateReviewService(repo,ws,input,albumFirst:true).Run());
        True(input.Output.First().StartsWith("ALBUM METADATA"));True(input.Output.Any(x=>x.Contains("Album Artist [Test Artist]")));
        Equal(1,input.Output.Count(x=>x.Contains("Confirm album:")));True(repo.All().All(x=>x.Effective.Album=="Christmas Album"&&x.Effective.Year==2012&&x.Effective.AlbumArtist=="Test Artist"));
        True(repo.All().All(x=>x.Effective.Title=="Test Song"&&x.Effective.Track==1));
        var again=new GuidedReviewInput("/q");new CandidateReviewService(repo,ws,again,albumFirst:true).Run();True(!again.Output.Any(x=>x.StartsWith("ALBUM METADATA")));
    }
    static void AlbumSetupRepairsProcessedMissingAlbum()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);var before=Snapshot(f.Source);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var original=repo.All().Single();
        repo.Save(MakeReady(original) with{Effective=MakeReady(original).Effective with{Album="",AlbumArtist="",Year=0,ConfirmedLooseTrack=true}},"loose track processed previously");
        var run=new RunPipelineService(repo,ws);var targets=run.Targets(f.Target);run.ApplyReady(targets,f.Reports);var old=new AudioFileScanner().Scan(f.Target).Single();
        True(new AlbumSetupService(repo,ws,new GuidedReviewInput("","Confirmed Album","2001","")).Ensure(repo.All().Single()));
        Equal(ProcessingStatus.Processed,repo.All().Single().Status);run.ApplyReady(targets,f.Reports);
        var final=new AudioFileScanner().Scan(f.Target).Single();True(final.Contains("2001 - Confirmed Album"));True(!File.Exists(old));Equal(before,Snapshot(f.Source));
    }
    static void AlbumSetupVariousAndExplicitOwner()
    {
        foreach(var explicitOwner in new[]{false,true})
        {
            var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);var item=GuidedReviewFixture(repo,f);repo.Save(item with{Effective=item.Effective with{Album="",AlbumArtist="",Year=0}},"setup");
            var input=new GuidedReviewInput(explicitOwner?"Fixed Artist":"","Album","2000","","YES");var setup=new AlbumSetupService(repo,ws,input);True(setup.Ensure(repo.All().Single()));
            var changed=repo.All().Single();changed=changed with{Effective=changed.Effective with{Artist="Another Artist"}};repo.Save(changed,"confirmed different track artist");setup.ReconcileArtist(changed);
            var actual=repo.All().Single().Effective;Equal(explicitOwner?"Fixed Artist":"Various Artists",actual.AlbumArtist);Equal("Another Artist",actual.Artist);Equal("Album",actual.Album);Equal(2000u,actual.Year);
        }
    }
}
