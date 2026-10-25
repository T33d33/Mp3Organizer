using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void CandidateAlbumOverridesContextAndSurvivesRun()
    {
        foreach(var choice in new[]{"","1"})
        {
            var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"other.wav"));var ws=ProgressWorkspace(f);var before=Snapshot(f.Source);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
            foreach(var row in repo.All())
            {
                var metadata=MakeReady(row).Effective with{Album="Christmas",AlbumArtist="Various Artists",Year=2000};
                var candidate=TagMetadata.From(metadata) with{Album="Hej, Kolęda!",AlbumArtist="Rokiczanka",Artist="Rokiczanka",Title="Nowina Miła",Year=2018,Track=3};
                repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=metadata with{Identification=metadata.Identification! with{Candidates=[new(candidate,"","",.9,new())]}}},"review");
            }
            var store=new ManualResolutionStore(ws,f.Source);var data=store.Load();data.Folders["folder:scope"]=new(){LastKnownPath=f.Source,AlbumSetupConfirmed=true,ReviewFileIds=repo.All().Select(x=>x.Id).ToArray(),Album="Christmas",AlbumArtist="Various Artists",Year=2000};store.Save(data);
            new CandidateReviewService(repo,ws,new GuidedReviewInput(choice,"/q"),albumFirst:true).Run();
            var accepted=repo.All().Single(x=>x.Status==ProcessingStatus.Ready);var other=repo.All().Single(x=>x.Status==ProcessingStatus.NeedsReview);
            Equal("Hej, Kolęda!",accepted.Effective.Album);Equal(2018u,accepted.Effective.Year);Equal(3u,accepted.Effective.Track);Equal("Rokiczanka",accepted.Effective.AlbumArtist);Equal("Christmas",other.Effective.Album);
            True(new AlbumSetupService(repo,ws,new GuidedReviewInput()).Ensure(accepted));
            var resolver=new ManualMetadataResolver(new StubMetadataResolver(),new ManualResolutionStore(ws,f.Source));var resolved=resolver.ResolveAsync(accepted.Basic,[]).GetAwaiter().GetResult();Equal("Hej, Kolęda!",resolved.Album);Equal(2018u,resolved.Year);
            var peer=resolver.ResolveAsync(other.Basic,[]).GetAwaiter().GetResult();Equal("Christmas",peer.Album);Equal(2000u,peer.Year);
            var run=new RunPipelineService(repo,ws);run.ApplyReady(run.Targets(f.Target),f.Reports);True(repo.Mappings(f.Target).Single().RelativePath.Contains("2018 - Hej, Kolęda!"));Equal(before,Snapshot(f.Source));
        }
    }
}
