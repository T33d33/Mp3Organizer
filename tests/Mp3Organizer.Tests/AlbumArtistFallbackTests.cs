using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void ReviewMissingArtistUsesAlbumArtistOnAcceptance()
    {
        foreach(var accept in new[]{"","F"})
        foreach(var sample in new[]{("","Duffy","Duffy"),("Guest Singer","Duffy","Guest Singer"),("","Various Artists","Various Artists"),("","VA","Various Artists"),("Nirvana","Various Artists","Nirvana"),("","Unknown Artist","")})
        {
            var f=Fixture();var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);
            using(var repo=new ProgressRepository(ws))
            {
                new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();
                repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Artist=sample.Item1,AlbumArtist=sample.Item2,Album="The best of",Title="Stop",Track=3,Year=2010}},"missing artist fixture");
                new CandidateReviewService(repo,ws,new GuidedReviewInput("/q")).Run();Equal(sample.Item1,repo.All().Single().Effective.Artist);
                new CandidateReviewService(repo,ws,new GuidedReviewInput(accept)).Run();var saved=repo.All().Single();Equal(sample.Item3,saved.Effective.Artist);Equal("Stop",saved.Effective.Title);
                Equal(sample.Item3==""?ProcessingStatus.NeedsReview:ProcessingStatus.Ready,saved.Status);
                if(sample.Item3!="")Equal(sample.Item3,saved.Effective.TrackArtists.Single());
            }
            using(var reopened=new ProgressRepository(ws))
            {
                Equal(sample.Item3,reopened.All().Single().Effective.Artist);
                if(sample.Item3!="")Equal(0,new CandidateReviewService(reopened,ws,new GuidedReviewInput("")).Run());
            }
            Equal(before,Snapshot(f.Source));
        }
    }
}
