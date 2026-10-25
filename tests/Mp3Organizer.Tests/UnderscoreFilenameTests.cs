using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void UnderscoreArtistTitleSuggestions()
    {
        var f=Fixture();var basic=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File);
        foreach(var sample in new[]{("_Brooklyn_Bounce_-_Bass_Beats_&_Melody.mp3","Brooklyn Bounce","Bass Beats & Melody"),("_DJ_Aligator_Project_-_Turn_Up_The_Music.mp3","DJ Aligator Project","Turn Up The Music"),("AC-DC_-_Back_In_Black.mp3","AC-DC","Back In Black"),("_King_Africa_-_La_Bomba.mp3","King Africa","La Bomba"),("_Fifty_Fifty_-_I_Want_You.mp3","Fifty Fifty","I Want You"),("Slade_Merry Xmas.mp3","Slade","Merry Xmas"),("Slade-Merry Xmas.mp3","Slade","Merry Xmas"),("  Slade__  -__  Song__Name.mp3","Slade","Song Name"),("07_Slade_-_Song_Title.mp3","Slade","Song Title")})
        {
            var row=basic with{Artist="",FileName=sample.Item1,Title=Path.GetFileNameWithoutExtension(sample.Item1).Trim('_')};
            var suggestion=ReviewFilenameSuggestions.Apply(row);Equal(sample.Item2,suggestion.Artist);Equal(sample.Item3,suggestion.Title);
            var merged=MetadataFieldMerger.Merge(row,new FilenameMetadataFallback().Resolve(row));Equal(sample.Item2,merged.Artist);Equal(sample.Item3,merged.Title);
            Equal(row.Title,ReviewFilenameSuggestions.Apply(row with{Identification=new(){FieldSources=new(){["Title"]="FileManualOverride"}}}).Title);
        }
        var ambiguous=basic with{Artist="",FileName="AC-DC-Title.mp3",Title="AC-DC-Title"};Equal("",ReviewFilenameSuggestions.Apply(ambiguous).Artist);
    }
    static void UnderscoreReviewAcceptedAndCancelled()
    {
        var f=Fixture();var path=Path.Combine(f.Source,"_Brooklyn_Bounce_-_Bass_Beats_&_Melody.wav");File.Move(f.File,path);var ws=ProgressWorkspace(f);var before=Snapshot(f.Source);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Artist="",Title="Brooklyn_Bounce_-_Bass_Beats_&_Melody"}},"old pending");
        new CandidateReviewService(repo,ws,new GuidedReviewInput("/q")).Run();Equal("",repo.All().Single().Effective.Artist);
        new CandidateReviewService(repo,ws,new GuidedReviewInput("")).Run();Equal("Brooklyn Bounce",repo.All().Single().Effective.Artist);Equal("Bass Beats & Melody",repo.All().Single().Effective.Title);Equal(before,Snapshot(f.Source));
    }
}
