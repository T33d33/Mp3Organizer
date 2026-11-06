using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void ArtistTrackTitleAlbumFilenamePreservesFields()
    {
        var row=new AudioMetadata{FileName="Alexandrov Chorus & Dance Ensemble - 05 - Kalinka - Legend.mp3"};
        var fallback=new FilenameMetadataFallback();var parsed=fallback.Resolve(row);
        Equal("Alexandrov Chorus & Dance Ensemble",parsed.Artist);Equal("Kalinka - Legend",parsed.Title);Equal(5u,parsed.Track);
        parsed=fallback.Resolve(row with{Album="Legend"});Equal("Kalinka",parsed.Title);
        parsed=fallback.Resolve(row with{Artist="Confirmed Artist",Title="Confirmed Title",Track=7,Album="Confirmed Album"});Equal("Confirmed Artist",parsed.Artist);Equal("Confirmed Title",parsed.Title);Equal(7u,parsed.Track);Equal("Confirmed Album",parsed.Album);
    }
}
