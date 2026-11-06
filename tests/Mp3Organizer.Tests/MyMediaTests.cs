using Mp3Organizer;
using System.Text;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void AsciiNamingPolishGermanCyrillicAndPunctuation()
    {
        var n=new FileSystemNaming("ascii");
        Equal("Debiec Sciezka Mala krolewna Pieklo Lodz zolc PNVI",n.Name("Dębiec Ścieżka Mała królewna Piekło Łódź żółć PŃVI"));
        Equal("ae oe ue Ae Oe Ue ss SS",n.Name("ä ö ü Ä Ö Ü ß ẞ"));
        Equal("Moskva Shchuka Yozh Kyiv",n.Name("Москва Щука Ёж Київ"));
        Equal("Its a song",n.Name("It’s – a — song!"));
        Equal(n.Name("東京😀"),n.Name("東京😀"));True(n.Name("東京😀").All(c=>c<128));
        Equal(n.Name("żółć"),n.Name("żółć".Normalize(NormalizationForm.FormD)));
        Equal("Żółć – Title.mp3",new FileSystemNaming().RelativePath("Żółć – Title.mp3"));
        Throws<ArgumentException>(()=>new FileSystemNaming("other"));
    }
    static void PolishPlaceholderQualityAndFilenameFallback()
    {
        foreach(var value in new[]{"Unknown Artist","UNKNOWN-album 12","No Title","Nieznany wykonawca","nieznany ARTYSTA 2","Nieznany album (2008-02-14 11:13:39)","Nieznany tytuł","Ścieżka 22","ścieżka 22","Track 22","AudioTrack 22","Œcie¿ka 22","Dêbiec"})True(MetadataQualityEvaluator.Invalid(value));
        foreach(var value in new[]{"5 2 Dębiec","Pow pow","Ścieżka do domu","Unknown Pleasures"})True(!MetadataQualityEvaluator.Invalid(value));
        foreach(var filename in new[]{"01-title.mp3","01 - title.mp3","01_title.mp3","01.title.mp3","01 title.mp3","22 - Pow pow.mp3"})
        {
            var parsed=FilenameTrackParser.Parse(filename)!;Equal(filename.StartsWith("22")?22u:1u,parsed.Number);Equal(filename.StartsWith("22")?"Pow pow":"title",parsed.Title);
        }
        var bad=new AudioMetadata{FileName="22 - Pow pow.mp3",Title="Œcie¿ka 22",Artist="Nieznany wykonawca",Album="Nieznany album (2008-02-14 11:13:39)"};
        var good=MetadataFieldMerger.Merge(bad,new FilenameMetadataFallback().Resolve(bad));Equal("Pow pow",good.Title);Equal(22u,good.Track);Equal("",good.Artist);Equal("",good.Album);Equal("FilenameFallback",good.Identification!.FieldSources["Title"]);
        var reliable=bad with{Title="Real title"};Equal("Real title",MetadataFieldMerger.Merge(reliable,new FilenameMetadataFallback().Resolve(reliable)).Title);
    }
    sealed class MyMediaResolver : IMetadataResolver
    {
        public Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
        {
            var sources=MetadataFieldMerger.Fields.ToDictionary(x=>x,x=>x=="Title"||x=="TrackNumber"?"FilenameFallback":"FolderManualOverride");
            return Task.FromResult(file with{Artist="5 2 Dębiec",TrackArtists=["5 2 Dębiec"],AlbumArtist="5 2 Dębiec",Album="PŃVI",Title=Path.GetFileNameWithoutExtension(file.FileName).Contains("second")?"Pieklo":"Piekło",Track=22,Disc=2,Year=2003,Identification=new(){Status="Resolved",MetadataPolicyVersion=2,Original=TagMetadata.From(file),FieldSources=sources}});
        }
    }
    static string MyMediaMp3(FixturePaths f,string name="22 - Pow pow.mp3",byte sample=0)
    {
        var path=Path.Combine(f.Source,name);
        using(var output=File.Create(path))for(int i=0;i<120;i++){var frame=new byte[417];frame[0]=255;frame[1]=251;frame[2]=144;frame[100]=sample;output.Write(frame);}
        using(var tags=TagLib.File.Create(path)){tags.Tag.Title="Œcie¿ka 22";tags.Tag.Performers=["Nieznany wykonawca"];tags.Tag.Album="Nieznany album (2008-02-14 11:13:39)";tags.Tag.Track=22;tags.Save();}
        return path;
    }
    static void MyMediaTargetTagsPathsSourceAndProvenance()
    {
        var f=Fixture();File.Delete(f.File);var source=MyMediaMp3(f);var before=Snapshot(f.Source);var audio=Mp3AudioPayload.Hash(source);
        var plan=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver(),filesystemNaming:"ascii");
        var op=plan.Operations.Single();True(op.RelativePath.All(c=>c<128));True(op.RelativePath.Contains("5 2 Debiec"));True(op.RelativePath.Contains("PNVI"));True(op.TagChanges.Any(x=>x.Field=="Title"&&x.Before=="Œcie¿ka 22"&&x.After=="Piekło"));
        var output=new List<string>();TargetTagWriter.Describe(plan,output.Add);True(output.Any(x=>x.StartsWith("PATH TRANSFORMATION:")));True(output.Any(x=>x.Contains("Œcie¿ka 22")&&x.Contains("FilenameFallback")));
        new SafeCopyExecutor().Execute(plan,f.Source,f.Target,true);Equal(before,Snapshot(f.Source));True(!Directory.Exists(f.Target));
        new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false);
        var path=Path.Combine(f.Target,op.RelativePath);var tags=new TagLibMetadataReader(new ReadOnlySource()).Read(path);
        Equal("Piekło",tags.Title);Equal("5 2 Dębiec",tags.Artist);Equal("PŃVI",tags.Album);Equal("5 2 Dębiec",tags.AlbumArtist);Equal(22u,tags.Track);Equal(2u,tags.Disc);Equal(2003u,tags.Year);
        Equal(before,Snapshot(f.Source));Equal(audio,Mp3AudioPayload.Hash(path));True(tags.Sha256!=op.Metadata.Sha256);
        var saved=plan.TargetMetadata[op.RelativePath];Equal(op.Metadata.Sha256,saved.SourceSha256);Equal(tags.Sha256,saved.Sha256);Equal("FilenameFallback",saved.Identification!.FieldSources["Title"]);
        True(new PlaylistValidator().Validate(f.Target).All(x=>x.IsValid));
        var map=File.ReadAllText(Path.Combine(f.Target,"playlist-map.json"));
        var next=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver());Equal("ascii",next.FileSystemNaming);Equal("AlreadyPresent",next.Operations.Single().Action);
        new SafeCopyExecutor().Execute(next,f.Source,f.Target,false);Equal(map,File.ReadAllText(Path.Combine(f.Target,"playlist-map.json")));
        Throws<IOException>(()=>new CopyPlanBuilder().Build(f.Source,f.Target,filesystemNaming:"unicode"));
        new CsvReportWriter().Write(f.Reports,f.Source,plan.Library,plan);True(File.ReadAllText(Path.Combine(f.Reports,"target-tag-changes.csv")).Contains("FilenameFallback"));
    }
    static void MyMediaTransliterationCollisionAndStableIds()
    {
        var f=Fixture();File.Delete(f.File);MyMediaMp3(f);MyMediaMp3(f,"second.mp3",1);
        var ascii=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver(),filesystemNaming:"ascii");
        var unicode=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver());
        Equal(JsonFormat.Serialize(unicode.PlaylistMap),JsonFormat.Serialize(ascii.PlaylistMap));
        Equal(2,ascii.Operations.Select(x=>x.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());Equal(1,ascii.ResolvedConflicts.Count);
        var repeat=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver(),filesystemNaming:"ascii");Equal(string.Join("|",ascii.Operations.Select(x=>x.RelativePath)),string.Join("|",repeat.Operations.Select(x=>x.RelativePath)));
        new SafeCopyExecutor().Execute(ascii,f.Source,f.Target,false);True(new PlaylistValidator().Validate(f.Target).All(x=>x.IsValid));
        foreach(var op in ascii.Operations)True(File.Exists(Path.Combine(f.Target,op.RelativePath)));
    }
    static void PlaylistValidatorUnicodeAndFailureDetails()
    {
        var f=Fixture();File.Delete(f.File);MyMediaMp3(f);
        var plan=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver());True(plan.Operations.Single().RelativePath.Contains("Dębiec"));
        new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false);True(new PlaylistValidator().Validate(f.Target).All(x=>x.IsValid));
        var playlist=Path.Combine(f.Target,plan.Playlists.First().RelativePath);var entry=File.ReadAllLines(playlist).First(x=>x.Length>0&&!x.StartsWith('#'));
        File.AppendAllText(playlist,"\n"+entry+"\nmissing.mp3\nhttps://invalid/test.mp3\n../../../../escape.mp3\n",new UTF8Encoding(false));
        var bad=new PlaylistValidator().Validate(f.Target).Single(x=>x.Path==playlist);Equal(1,bad.Duplicates);Equal(1,bad.Missing);Equal(1,bad.Malformed);Equal(1,bad.Escaping);True(bad.Failures.Single(x=>x.Kind=="Missing").ResolvedPath.EndsWith("missing.mp3"));
        Equal(3,Program.Run(["validate-playlists",f.Target]));
        File.WriteAllBytes(playlist,[255,254,255]);True(new PlaylistValidator().Validate(f.Target).Single(x=>x.Path==playlist).Malformed>0);
    }
    static void TargetTagsPreserveUnresolvedAndRejectOverwrite()
    {
        var f=Fixture();File.Delete(f.File);var source=MyMediaMp3(f);var original=new TagLibMetadataReader(new ReadOnlySource()).Read(source);
        var resolved=original with{Title="Good title",Identification=new(){FieldSources=new(){["Title"]="Unresolved"}}};Equal(0,TargetTagWriter.Plan(original,resolved).Count);
        var plan=new CopyPlanBuilder().Build(f.Source,f.Target,new MyMediaResolver());Directory.CreateDirectory(f.Target);
        // Any external change after planning fails before publishing.
        File.WriteAllText(Path.Combine(f.Target,"keep.txt"),"keep");Throws<IOException>(()=>new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false));Equal("keep",File.ReadAllText(Path.Combine(f.Target,"keep.txt")));
    }
}
