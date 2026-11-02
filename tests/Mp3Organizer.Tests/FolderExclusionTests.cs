using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void PermanentFolderExclusionSurvivesScanAndRestart()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);string id;
        using(var repo=new ProgressRepository(ws))
        {
            new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();id=item.Id;
            repo.Save(MakeReady(item) with{Status=ProcessingStatus.NeedsReview},"fixture");
            new CandidateReviewService(repo,ws,new GuidedReviewInput("X")).Run();
            Equal(ProcessingStatus.Skipped,repo.Find(id)!.Status);
            True(!repo.IsExcluded(Path.Combine(f.Source,"child","new.wav")));
            True(!repo.IsExcluded(f.Source+"-sibling"+Path.DirectorySeparatorChar+"song.mp3"));
            repo.Save(item with{Status=ProcessingStatus.Ready},"attempt reset");Equal(ProcessingStatus.Skipped,repo.Find(id)!.Status);
        }
        using(var repo=new ProgressRepository(ws))
        {
            True(repo.IsExcluded(Path.Combine(f.Source,"new.wav")));
            var reads=0;new IncrementalProgressService(repo,path=>{reads++;throw new Exception("excluded audio read");}).Scan(f.Source,true);Equal(0,reads);
            Equal(ProcessingStatus.Skipped,repo.Find(id)!.Status);
            repo.IncludeFolder(f.Source);True(!repo.IsExcluded(Path.Combine(f.Source,"new.wav")));Equal(ProcessingStatus.Discovered,repo.Find(id)!.Status);
        }
    }
    static void FolderActionsKeepChildrenActiveAndFStrict()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();
        repo.Save(MakeReady(item) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(item).Effective with{Title="Track 01"}},"generic title");
        new CandidateReviewService(repo,ws,new GuidedReviewInput("F")).Run();Equal(ProcessingStatus.NeedsReview,repo.Find(item.Id)!.Status);
        new CandidateReviewService(repo,ws,new GuidedReviewInput("G")).Run();Equal(ProcessingStatus.Ready,repo.Find(item.Id)!.Status);
        var child=Path.Combine(f.Source,"child");Directory.CreateDirectory(child);var childFile=Path.Combine(child,"song.wav");File.Copy(f.File,childFile);
        repo.ExcludeFolder(f.Source);new IncrementalProgressService(repo).Scan(f.Source,true);
        Equal(ProcessingStatus.Skipped,repo.Find(item.Id)!.Status);Equal(ProcessingStatus.Discovered,repo.All().Single(x=>x.CurrentPath==childFile).Status);
        True(!repo.IsExcluded(childFile));
    }
}
