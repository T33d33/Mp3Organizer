using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void AlbumSequenceOrderingOverridesAndCollisions()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var seed=repo.All().Single();
        IndexedMusic Item(string name,uint track,ProcessingStatus status)=>seed with{Id=name,CurrentPath=Path.Combine(f.Source,name+".wav"),Status=status,Effective=seed.Effective with{Track=track}};
        var one=Item("1",0,ProcessingStatus.NeedsReview);var two=Item("2",0,ProcessingStatus.NeedsReview);var ten=Item("10",0,ProcessingStatus.NeedsReview);
        Equal(1u,AlbumTrackSequence.Suggest(one,[one,two,ten]));
        one=one with{Status=ProcessingStatus.Ready,Effective=one.Effective with{Track=5}};
        Equal(6u,AlbumTrackSequence.Suggest(two,[one,two,ten]));
        ten=ten with{Status=ProcessingStatus.Processed,Effective=ten.Effective with{Track=6}};
        Equal(7u,AlbumTrackSequence.Suggest(two,[one,two,ten]));
        one=one with{Effective=one.Effective with{Track=3}};Equal(4u,AlbumTrackSequence.Suggest(two,[one,two,ten]));
        Equal(6u,AlbumTrackSequence.Suggest(ten,[one,two,ten]));
    }
    static void AlbumSequenceBackRecalculatesSuggestion()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"z-next.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var item in repo.All())repo.Save(item with{Status=ProcessingStatus.NeedsReview,Effective=item.Basic with{Track=0}},"missing number");
        var input=new GuidedReviewInput("","/back","M","","","5","","");Equal(2,new CandidateReviewService(repo,ws,input,albumFirst:true).Run());
        var rows=repo.All().OrderBy(x=>x.CurrentPath,NaturalPathComparer.Instance).ToArray();Equal(5u,rows[0].Effective.Track);Equal(6u,rows[1].Effective.Track);
        True(input.Output.Any(x=>x.Contains("suggestion: 1")));True(input.Output.Any(x=>x.Contains("suggestion: 2")));True(input.Output.Any(x=>x.Contains("suggestion: 6")));
    }
    static void AlbumSequenceReviewPersistsAcrossRestart()
    {
        var f=Fixture();File.Move(f.File,Path.Combine(f.Source,"1.wav"));File.Copy(Path.Combine(f.Source,"1.wav"),Path.Combine(f.Source,"2.wav"));File.Copy(Path.Combine(f.Source,"1.wav"),Path.Combine(f.Source,"10.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var item in repo.All())repo.Save(item with{Status=ProcessingStatus.NeedsReview,Effective=item.Basic with{Track=0}},"missing number");
        Equal(1,new CandidateReviewService(repo,ws,new GuidedReviewInput("M","","","5","","/q"),albumFirst:true).Run());
        var input=new GuidedReviewInput("","");Equal(2,new CandidateReviewService(repo,ws,input,albumFirst:true).Run());
        var rows=repo.All().OrderBy(x=>x.CurrentPath,NaturalPathComparer.Instance).ToArray();Equal(5u,rows[0].Effective.Track);Equal(6u,rows[1].Effective.Track);Equal(7u,rows[2].Effective.Track);
        Equal(3,rows.Select(x=>x.Effective.Track).Distinct().Count());True(input.Output.Any(x=>x.Contains("suggestion: 6")));True(input.Output.Any(x=>x.Contains("suggestion: 7")));
    }
}
