using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void GeneralFolderRepairPreservesTracksAndScope()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=MakeReady(repo.All().Single());repo.Save(row with{Status=ProcessingStatus.Processed},"processed");
        var child=row with{Id=Guid.NewGuid().ToString("N"),CurrentPath=Path.Combine(f.Source,"child","song.wav"),Status=ProcessingStatus.Ready};repo.Save(child,"child");
        True(new FolderRepairService(repo,ws,new GuidedReviewInput("Composer","Correct Album","","" )).Run(f.Source));
        var saved=repo.Find(row.Id)!;Equal("Correct Album",saved.Effective.Album);Equal(ProcessingStatus.Processed,saved.Status);Equal(row.Effective.Title,saved.Effective.Title);Equal(row.Effective.Artist,saved.Effective.Artist);Equal(row.Effective.Track,saved.Effective.Track);Equal(child.Effective.Album,repo.Find(child.Id)!.Effective.Album);
        True(new FolderRepairService(repo,ws,new GuidedReviewInput("","Second correction","","" )).Run(f.Source));Equal("Second correction",repo.Find(row.Id)!.Effective.Album);
        True(!new FolderRepairService(repo,ws,new GuidedReviewInput("/cancel")).Run(f.Source));Equal("Second correction",repo.Find(row.Id)!.Effective.Album);
        True(!new FolderRepairService(repo,ws,new GuidedReviewInput()).Run(Path.Combine(f.Source,"absent")));
    }
}
