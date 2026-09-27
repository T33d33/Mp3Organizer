using Mp3Organizer;

namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    static AudioMetadata Kingpin() => Row(Path.Combine(root,"Cypress Hill - Kingpin OST-uppedByTeedee","AudioTrack 02.mp3"),"no artist","AudioTrack 02",hash:new('D',64)) with {Album="no title",Track=2};
    static void PlaceholderVariants()
    {
        foreach(var value in new[]{"no artist","unknown artist","unknown","artist","no title","unknown album","album"," NO-ARTIST (01) ","Unknown_Album.12","ＮＯ ＡＲＴＩＳＴ"})
            True(MetadataQualityEvaluator.Invalid(value));
        foreach(var title in new[]{"AudioTrack 01","AudioTrack01","Track01","Track 01","track1","audio.track_02","02 - AudioTrack 02","002.mp3","---"})
            True(MetadataQualityEvaluator.FilenameTitle(Row(title:title)));
        foreach(var title in new[]{"Track of My Tears","Unknown Pleasures","No Artist Left Behind","01 - Checkmate","1984"+" Blues"})
            True(!MetadataQualityEvaluator.Invalid(title)&&!MetadataQualityEvaluator.FilenameTitle(Row(title:title)));
    }
    static void KingpinOfflineAndReport()
    {
        var row=Kingpin();var ac=new FakeAcoust();var result=Resolve(new(new FakeFingerprint{Value=null},ac,new FakeBrainz(),false,false),row);
        Equal(0,ac.Calls);Equal("",result.Artist);Equal("",result.Album);Equal("Review",result.Identification!.Status);
        Equal("no artist",result.Identification.Original.Artist);Equal("no title",result.Identification.Original.Album);Equal("AudioTrack 02",result.Identification.Original.Title);
        foreach(var reason in new[]{"PlaceholderArtist","PlaceholderAlbum","GenericTrackTitle"})True(result.Identification.ReviewReason.Contains(reason));
        var f=Fixture();new IdentificationReportWriter().Write(f.Reports,f.Source,[result]);var csv=File.ReadAllText(Path.Combine(f.Reports,"identification.csv"));
        True(csv.Contains("PlaceholderArtist; GenericTrackTitle; PlaceholderAlbum"));True(!csv.Contains("TagsAccepted"));
    }
    static void KingpinOnlineAndFailure()
    {
        var fp=new FakeFingerprint();var ac=new FakeAcoust();var mb=new FakeBrainz{Value=new(new(RecordingId,"Cypress Hill","Checkmate",100),[])};
        var result=Resolve(Resolver(fp,ac,mb),Kingpin());Equal(1,fp.Calls);Equal(1,ac.Calls);Equal(1,mb.Calls);Equal("Checkmate",result.Title);Equal("Cypress Hill",result.Artist);True(result.Identification!.Status!="TagsAccepted");True(result.Identification.ReviewReason.Contains("GenericTrackTitle"));
        ac=new(){Value=new([],"HTTP 503")};result=Resolve(Resolver(ac:ac),Kingpin());Equal("",result.Artist);Equal("",result.Album);Equal("Review",result.Identification!.Status);
    }
    static void KingpinInteractiveAndManualPriority()
    {
        var f=Fixture();var store=ManualStore(f);var row=Resolve(new(new FakeFingerprint{Value=null},new FakeAcoust(),new FakeBrainz(),false,false),Kingpin());
        var input=new ScriptConsole("","Cypress Hill","Kingpin OST","","Checkmate");new InteractiveResolution(store,input).Run([row],true,false);
        True(input.Prompts.Any(x=>x.StartsWith("Title")));Equal("Checkmate",store.Load().Files.Values.Single().Title);
        var fp=new FakeFingerprint();var resolved=new ManualMetadataResolver(Resolver(fp),store).ResolveAsync(Kingpin(),[Kingpin()]).GetAwaiter().GetResult();
        Equal(0,fp.Calls);Equal("Checkmate",resolved.Title);Equal("ManualResolved",resolved.Identification!.Status);
    }
    static void PlaceholderOldCacheInvalidated()
    {
        var f=Fixture();var cache=Cache(f);var row=Kingpin();
        var context=$"{(MetadataNormalizer.Key(row.Album),row.Year)}:1";
        cache.Put("resolution-v2:"+row.Sha256+":"+JsonFormat.Serialize(TagMetadata.From(row))+":"+context,JsonFormat.Serialize(row with{Identification=new(){LookupStatus="Success"}}),TimeSpan.MaxValue);
        var fp=new FakeFingerprint{Value=null};var result=new CachedMetadataResolver(new MetadataResolver(fp,new FakeAcoust(),new FakeBrainz(),false,false),cache,false).ResolveAsync(row,[row]).GetAwaiter().GetResult();
        Equal(1,fp.Calls);Equal("Review",result.Identification!.Status);Equal("",result.Artist);
    }
}
