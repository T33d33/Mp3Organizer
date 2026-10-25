using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void PolishArtistAlbumRepairUsesPathEvidence()
    {
        var f=Fixture();var basic=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File);
        var row=basic with{FullPath=Path.Combine(f.Source,"Ewa Błaszczyk - Nie żałuję - 1999 - 128 kbps","Song.mp3"),Artist="Ewa B³aszczyk",AlbumArtist="Ewa B³aszczyk",Album="Nie ¿a³ujê"};
        var fixedRow=FilenameEncodingRepair.Apply(row);Equal("Ewa Błaszczyk",fixedRow.Artist);Equal("Ewa Błaszczyk",fixedRow.AlbumArtist);Equal("Nie żałuję",fixedRow.Album);
        var merged=MetadataFieldMerger.Merge(row,row);Equal(fixedRow.Artist,merged.Artist);Equal(fixedRow.Album,merged.Album);
        Equal(row.Artist,FilenameEncodingRepair.Apply(row with{FullPath=f.File}).Artist);
        Equal(row.Artist,FilenameEncodingRepair.Apply(row with{Identification=new(){FieldSources=new(){["Artist"]="FileManualOverride"}}}).Artist);
        var woj=basic with{FullPath=Path.Combine(f.Source,"Elżbieta Wojnowska - Songi Brechta","Song.mp3"),Artist="El¿bieta Wojnowska"};Equal("Elżbieta Wojnowska",FilenameEncodingRepair.Apply(woj).Artist);
        Equal("Jaki kolor wybrać chcesz",FilenameEncodingRepair.Apply(basic with{FileName="Jaki kolor wybrać chcesz.mp3",Title="Jaki kolor wybraæ chcesz"}).Title);
    }
    static void PolishAlbumSetupAndPendingReviewRepair()
    {
        var f=Fixture();var folder=Path.Combine(f.Source,"Ewa Błaszczyk - Nie żałuję - 1999");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"Song.wav");File.Move(f.File,path);
        var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();
        repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Artist="Ewa B³aszczyk",AlbumArtist="",Album="Nie ¿a³ujê",Year=0}},"old tags");
        var cancel=new GuidedReviewInput("/q");new CandidateReviewService(repo,ws,cancel,albumFirst:true).Run();True(cancel.Output.Any(x=>x.Contains("Album Artist [Ewa Błaszczyk]")));Equal("Ewa B³aszczyk",repo.All().Single().Effective.Artist);
        var accept=new GuidedReviewInput("","","1999","","");new CandidateReviewService(repo,ws,accept,albumFirst:true).Run();Equal("Ewa Błaszczyk",repo.All().Single().Effective.Artist);Equal("Nie żałuję",repo.All().Single().Effective.Album);Equal(before,Snapshot(f.Source));
    }
    static void PolishTitleRepairRequiresFilenameAgreement()
    {
        var f=Fixture();var row=new TagLibMetadataReader(new ReadOnlySource()).Read(f.File) with{FileName="13-Jaki kolor wybrać chcesz.mp3",Title="Jaki kolor wybraæ chcesz"};
        Equal("Jaki kolor wybrać chcesz",FilenameEncodingRepair.Title(row));
        var merged=MetadataFieldMerger.Merge(row,new FilenameMetadataFallback().Resolve(row));Equal("Jaki kolor wybrać chcesz",merged.Title);Equal("FilenameEncodingRepair",merged.Identification!.FieldSources["Title"]);
        True(FilenameEncodingRepair.Title(row with{Title="Another song"})==null);
        True(FilenameEncodingRepair.Title(row with{Title="Jaki kolor wybrać chcesz"})==null);
        True(FilenameEncodingRepair.Title(row with{Identification=new(){FieldSources=new(){["Title"]="FileManualOverride"}}})==null);
        var valid=row with{FileName="01-Encyclopædia.mp3",Title="Encyclopædia"};True(FilenameEncodingRepair.Title(valid)==null);
        Equal("Zażółć",FilenameEncodingRepair.Title(row with{FileName="01-Zażółć.mp3",Title="ZaÅ¼Ã³Å‚Ä‡"}));
    }
    static void ReviewRepairsPersistedEncodingSuggestionOnAcceptance()
    {
        var f=Fixture();var path=Path.Combine(f.Source,"13-Jaki kolor wybrać chcesz.wav");File.Move(f.File,path);var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        var row=repo.All().Single();repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Title="Jaki kolor wybraæ chcesz"}},"old damaged suggestion");
        var cancelled=new GuidedReviewInput("/q");new CandidateReviewService(repo,ws,cancelled).Run();Equal("Jaki kolor wybraæ chcesz",repo.All().Single().Effective.Title);True(cancelled.Output.Any(x=>x.Contains("Jaki kolor wybrać chcesz")));
        new CandidateReviewService(repo,ws,new GuidedReviewInput("")).Run();Equal("Jaki kolor wybrać chcesz",repo.All().Single().Effective.Title);Equal(ProcessingStatus.Ready,repo.All().Single().Status);Equal(before,Snapshot(f.Source));
    }
}
