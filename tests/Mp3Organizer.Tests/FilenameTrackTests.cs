using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void CupheadTrackNumbers()
    {
        foreach(var sample in new (string Name,uint Number,string Title)[]{("Cuphead - OST - 07 7. Botanic Panic.mp3",7u,"Botanic Panic"),("Cuphead - OST - 13 13. Floral Fury.mp3",13u,"Floral Fury"),("Cuphead - OST - 22 22. Fiery Frolic.mp3",22u,"Fiery Frolic"),("01 - Checkmate.mp3",1u,"Checkmate")})
        {
            var hint=FilenameTrackParser.Parse(sample.Name)!;Equal(sample.Number,hint.Number);Equal(sample.Title,hint.Title);
            var row=Row(Path.Combine(root,sample.Name),"Cuphead Composer","") with{Album="Cuphead OST"};
            var result=new FilenameMetadataFallback().Resolve(row);Equal(sample.Number,result.Track);Equal(sample.Title,result.Title);
            result=new FilenameMetadataFallback().Resolve(row with{Track=99,Title="Trusted title"});Equal(99u,result.Track);Equal("Trusted title",result.Title);
        }
        foreach(var name in new[]{"1984 - Song.mp3","Summer of 69.mp3","Cuphead - OST - 07 8. Conflicting.mp3","00 - Song.mp3","track title.mp3"})True(FilenameTrackParser.Parse(name)==null);
    }
    static void TrackHintWithReliableTitle()
    {
        var row=Row(Path.Combine(root,"Cuphead - OST - 07 7. Botanic Panic.mp3"),"Kristofer Maddigan","Botanic Panic") with{Album="Cuphead OST"};
        var fp=new FakeFingerprint();var result=Resolve(Resolver(fp),row);Equal(7u,result.Track);Equal(0,fp.Calls);Equal("FilenameFallback",result.Identification!.FieldSources["TrackNumber"]);
    }
    static void InteractiveTrackHintAccepted()
    {
        var f=Fixture();var row=Unresolved(f) with{FileName="Cuphead - OST - 07 7. Botanic Panic.mp3",Title="Botanic Panic"};var store=ManualStore(f);
        new InteractiveResolution(store,new ScriptConsole("","","","","")).Run([row],true,false);
        Equal((uint?)7,store.Load().Files.Values.Single().TrackNumber);
    }
    static void GenericFilenameTrackHints()
    {
        foreach(var name in new[]{"Track02.mp3","Track 02.mp3","TRACK_02.flac","AudioTrack02.mp3","Audio-Track-02.wav"})
        {
            var hint=FilenameTrackParser.Parse(name)!;Equal(2u,hint.Number);Equal("",hint.Title);
            var row=Row(Path.Combine(root,name),"Cypress Hill","Trouble") with{Album="Skull and Bones"};
            var result=new FilenameMetadataFallback().Resolve(row);Equal(2u,result.Track);Equal("Trouble",result.Title);
            result=new FilenameMetadataFallback().Resolve(row with{Title="Track02"});Equal(2u,result.Track);True(MetadataQualityEvaluator.Invalid(result.Title));
            Equal(9u,new FilenameMetadataFallback().Resolve(row with{Track=9}).Track);
        }
        foreach(var name in new[]{"Track00.mp3","Track1984.mp3","Soundtrack02.mp3","Track02Live.mp3"})
        {
            True(FilenameTrackParser.Parse(name)==null);
        }
    }
    static void GenericFilenameInteractiveDefault()
    {
        var f=Fixture();var row=Unresolved(f) with{FileName="Track02.mp3",Title="Trouble"};var store=ManualStore(f);
        new InteractiveResolution(store,new ScriptConsole("","","","","")).Run([row],true,false);
        Equal((uint?)2,store.Load().Files.Values.Single().TrackNumber);
    }
    static void ArtistAlbumTrackFilename()
    {
        foreach(var separator in new[]{" - "," – "," — "})
        {
            var name=string.Join(separator,"Cypress Hill","Till Death Do Us Part","01","Another Body Drops")+".mp3";
            var hint=FilenameTrackParser.Parse(name)!;Equal(1u,hint.Number);Equal("Another Body Drops",hint.Title);
            var row=Row(Path.Combine(root,name),"Cypress Hill",Path.GetFileNameWithoutExtension(name)) with{Album="Till Death Do Us Part"};
            True(MetadataQualityEvaluator.FilenameTitle(row));var result=new FilenameMetadataFallback().Resolve(row);
            Equal(1u,result.Track);Equal("Another Body Drops",result.Title);
        }
        var numbered=Row(Path.Combine(root,"08 - 3 lil'putos.mp3"),title:"");
        var fallback=new FilenameMetadataFallback().Resolve(numbered);Equal(8u,fallback.Track);Equal("3 lil'putos",fallback.Title);
        var f=Fixture();var input=Unresolved(f) with{FileName="Cypress Hill - Till Death Do Us Part - 03 - Latin Thugs.mp3",Title="Latin Thugs"};
        var store=ManualStore(f);new InteractiveResolution(store,new ScriptConsole("","","","","")).Run([input],true,false);
        Equal((uint?)3,store.Load().Files.Values.Single().TrackNumber);
    }
}

