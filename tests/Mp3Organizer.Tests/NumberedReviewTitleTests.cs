using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void NumberedFilenameTitlesSaveWithEnterAndFinish()
    {
        foreach(var afterAlbumEdit in new[]{false,true})
        foreach(var action in new[]{"","F"})
        {
            var f=Fixture();var path=Path.Combine(f.Source,"11. Największy teatr świata.wav");File.Move(f.File,path);var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);
            using(var repo=new ProgressRepository(ws))
            {
                new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();var data=MakeReady(item).Effective with{Title="11. Największy teatr świata",Track=11};
                data=data with{Identification=data.Identification! with{FieldSources=new(){["Title"]="FileManualOverride"}}};
                repo.Save(item with{Effective=data,Status=ProcessingStatus.NeedsReview},"previous acceptance of filename title");
                new CandidateReviewService(repo,ws,new GuidedReviewInput("/q")).Run();Equal(data.Title,repo.All().Single().Effective.Title);
                new CandidateReviewService(repo,ws,new GuidedReviewInput(afterAlbumEdit?new[]{"B","Test Artist","Corrected album","2006","",action}:new[]{action})).Run();var saved=repo.All().Single();Equal("Największy teatr świata",saved.Effective.Title);Equal(11u,saved.Effective.Track);Equal(ProcessingStatus.Ready,saved.Status);
                var service=new RunPipelineService(repo,ws);Equal(1,service.ApplyReady(service.Targets(f.Target),f.Reports));
                Equal("99 Luftballons",ReviewFilenameSuggestions.Apply(data with{Title="99 Luftballons",FileName="99 Luftballons.wav",Track=1}).Title);
                Equal("My custom title",ReviewFilenameSuggestions.Apply(data with{Title="My custom title"}).Title);
                Equal("5-15",ReviewFilenameSuggestions.Apply(data with{Title="5-15",FileName="5-15.wav",Track=5}).Title);
            }
            using(var repo=new ProgressRepository(ws))Equal(0,new CandidateReviewService(repo,ws,new GuidedReviewInput("/q")).Run());
            Equal(before,Snapshot(f.Source));
        }
    }
}