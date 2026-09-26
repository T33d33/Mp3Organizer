using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void PartialOnlinePreservesFields()
    {
        var row=Kingpin() with{Album="Kingpin OST",AlbumArtist="Cypress Hill",Year=1999,Disc=2,Track=3};
        var mb=new FakeBrainz{Value=new(new(RecordingId,"Cypress Hill","Checkmate",100),[])};
        var result=Resolve(Resolver(mb:mb),row);Equal("Kingpin OST",result.Album);Equal((uint)1999,result.Year);Equal((uint)3,result.Track);Equal((uint)2,result.Disc);Equal("Cypress Hill",result.AlbumArtist);Equal("Checkmate",result.Title);
        Equal("Tags",result.Identification!.FieldSources["Album"]);Equal("AcoustIdMusicBrainz",result.Identification.FieldSources["Title"]);
        result=Resolve(Resolver(mb:mb),Kingpin());Equal("Kingpin OST",result.Album);Equal("FolderFallback",result.Identification!.FieldSources["Album"]);
    }
    static void PartialFieldPriorityAndPlaceholders()
    {
        var tags=Kingpin() with{Artist="Cypress Hill",Year=1999};var fallback=tags with{Album="Kingpin OST",Title="Fallback title",Disc=1};
        var merged=MetadataFieldMerger.Merge(tags,fallback,new(RecordingId,"Different artist","Checkmate",100),new(ReleaseId,"Online Album","Online Owner",2001,4,2));
        Equal("Cypress Hill",merged.Artist);Equal("Online Album",merged.Album);Equal("Checkmate",merged.Title);Equal((uint)1999,merged.Year);Equal((uint)2,merged.Disc);
        var manual=ManualMetadataResolver.Apply(ManualMetadataResolver.Apply(merged,new(){Artist="Manual Artist",Year=2005}),new(){Title="Manual Title"});
        Equal("Manual Artist",manual.Artist);Equal("Online Album",manual.Album);Equal("Manual Title",manual.Title);Equal((uint)2005,manual.Year);
        var empty=MetadataFieldMerger.Merge(Kingpin(),Kingpin());Equal("",empty.Artist);Equal("",empty.Album);Equal("",empty.Title);
    }
    static void FieldSourcesReport()
    {
        var f=Fixture();var row=MetadataFieldMerger.Merge(Kingpin(),new FolderMetadataFallback().Resolve(Kingpin()),new(RecordingId,"Cypress Hill","Checkmate",100));
        new IdentificationReportWriter().Write(f.Reports,f.Source,[row]);var text=File.ReadAllText(Path.Combine(f.Reports,"identification.csv"));True(text.Contains("AlbumSource"));True(text.Contains("FolderFallback"));
        var sources=MetadataFieldMerger.ManualSources(row.Identification!,new(){Year=2000},new(){Title="Chosen"});Equal("FolderManualOverride",sources["Year"]);Equal("FileManualOverride",sources["Title"]);Equal("FolderFallback",sources["Album"]);
    }
    static void IdentificationStageProgress()
    {
        var messages=new List<string>();var resolver=new MetadataResolver(new FakeFingerprint(),new FakeAcoust(),new FakeBrainz(),true,true,messages.Add);
        Resolve(resolver,Kingpin());foreach(var stage in new[]{"Fingerprinting:","AcoustID:","MusicBrainz:","Resolution:"})True(messages.Any(x=>x.StartsWith(stage)));
    }
}
