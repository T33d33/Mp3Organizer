using Mp3Organizer;
using System.Text;
namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    static string[] ImportedLines(FixturePaths f,SourcePlaylist playlist)=>File.ReadAllLines(Path.Combine(f.Target,playlist.TargetPath)).Where(x=>x!=""&&!x.StartsWith('#')).ToArray();
    static void ImportPlaylistFinalPathsAndDuplicates()
    {
        var f=Fixture();var a=AddFolderTrack(f,"Album A","song.wav");var b=AddFolderTrack(f,"Album B","same.wav");
        var lists=Path.Combine(f.Source,"Lists");Directory.CreateDirectory(lists);
        var first=Path.Combine(lists,"Christmas Mix.m3u8");File.WriteAllText(first,"#EXTM3U\n../Album A/song.wav\n"+b+"\n"+new Uri(a).AbsoluteUri+"\n");
        File.WriteAllText(Path.Combine(lists,"Another Mix.m3u"),"..\\Album B\\same.wav\n");var before=Snapshot(f.Source);
        using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        Equal(2,repo.SourcePlaylists().Count);True(!repo.Folders().Any(x=>x.Name=="Lists"));foreach(var row in repo.All())repo.Save(MakeReady(row),"ready");
        Equal(3,ApplyFolderReady(repo,f));Equal(1,new AudioFileScanner().Scan(f.Target).Count);
        var playlist=repo.SourcePlaylists().Single(x=>x.Path==first);var lines=ImportedLines(f,playlist);Equal(3,lines.Length);True(lines.All(x=>x==lines[0]));
        True(lines[0].StartsWith("..\\Test Artist\\"));True(!lines[0].Contains("Album A"));foreach(var line in lines)True(File.Exists(Path.GetFullPath(Path.Combine(f.Target,"_Playlist-Folder",line))));
        True(File.ReadAllText(Path.Combine(f.Target,"playlist-index.csv")).Contains("\""+playlist.Code+"\",\"ImportedPlaylist\""));
        True(!File.ReadAllBytes(Path.Combine(f.Target,playlist.TargetPath)).Take(3).SequenceEqual(new byte[]{239,187,191}));
        True(repo.SourcePlaylistEntries(playlist.Id).All(x=>x.Result=="Playable"));Equal(before,Snapshot(f.Source));
    }
    static void ImportPlaylistPendingAndAutomaticRetry()
    {
        var f=Fixture();var second=AddFolderTrack(f,"Album","second.wav",2);var path=Path.Combine(f.Source,"mix.m3u8");File.WriteAllText(path,"test.wav\nAlbum/second.wav\ntest.wav\nmissing.wav\nhttps://example.invalid/radio\n");
        using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=row.CurrentPath==second?ProcessingStatus.NeedsReview:ProcessingStatus.Ready,Effective=MakeReady(row).Effective with{Title=row.CurrentPath==second?"Second song":"First song"}},"fixture");
        Equal(1,ApplyFolderReady(repo,f));var playlist=repo.SourcePlaylists().Single();Equal(2,ImportedLines(f,playlist).Length);True(File.ReadAllText(Path.Combine(f.Target,playlist.TargetPath)).Contains("#OMITTED:3"));
        var pending=repo.All().Single(x=>x.CurrentPath==second);Equal(ProcessingStatus.NeedsReview,pending.Status);repo.Save(pending with{Status=ProcessingStatus.Ready},"review accepted");
        Equal(1,ApplyFolderReady(repo,f));var lines=ImportedLines(f,playlist);Equal(3,lines.Length);True(lines[1].Contains("Second song"));Equal(lines[0],lines[2]);
        Equal("Playable",repo.SourcePlaylistEntries(playlist.Id)[1].Result);True(repo.SourcePlaylistEntries(playlist.Id)[4].Result.Contains("URI"));
        var report=Directory.GetFiles(Path.Combine(ProgressWorkspace(f),"playlist-import-reports"),"*.csv").Order().Last();var csv=File.ReadAllText(report);True(csv.Contains("missing.wav"));True(csv.Contains("No unique indexed source path"));
    }
    static void ImportPlaylistStableCodesNoCopyAndRestart()
    {
        var f=Fixture();AddFolderTrack(f,"Album","second.wav",2);var list=Path.Combine(f.Source,"mix.m3u8");File.WriteAllText(list,"test.wav\nAlbum/second.wav\n");long id;
        using(var repo=new ProgressRepository(ProgressWorkspace(f)))
        {
            new IncrementalProgressService(repo).Scan(f.Source);foreach(var row in repo.All())repo.Save(MakeReady(row) with{Effective=MakeReady(row).Effective with{Title=Path.GetFileNameWithoutExtension(row.CurrentPath)+" song"}},"ready");
            Equal(2,ApplyFolderReady(repo,f));id=repo.SourcePlaylists().Single().Id;
        }
        File.WriteAllText(list,"Album/second.wav\ntest.wav\nAlbum/second.wav\n");
        using(var repo=new ProgressRepository(ProgressWorkspace(f)))
        {
            var scan=new IncrementalProgressService(repo);scan.Scan(f.Source);Equal(id,repo.SourcePlaylists().Single().Id);var audio=new AudioFileScanner().Scan(f.Target).Select(x=>x+Hash(x)+File.GetLastWriteTimeUtc(x)).ToArray();
            Equal(0,ApplyFolderReady(repo,f));var playlist=repo.SourcePlaylists().Single();var lines=ImportedLines(f,playlist);Equal(3,lines.Length);True(lines[0].Contains("second song"));Equal(lines[0],lines[2]);
            True(audio.SequenceEqual(new AudioFileScanner().Scan(f.Target).Select(x=>x+Hash(x)+File.GetLastWriteTimeUtc(x))));
            File.Delete(Path.Combine(f.Target,playlist.TargetPath));File.Delete(Path.Combine(f.Target,"playlist-index.csv"));Equal(0,ApplyFolderReady(repo,f));Equal(id,repo.SourcePlaylists().Single().Id);True(lines.SequenceEqual(ImportedLines(f,playlist)));
            // External edits/removal are simulated only in these generated fixtures.
            File.Delete(list);File.WriteAllText(Path.Combine(f.Source,"new.m3u8"),"test.wav\n");scan.Scan(f.Source);Equal(0,ApplyFolderReady(repo,f));Equal(0,ImportedLines(f,repo.SourcePlaylists().Single(x=>x.Id==id)).Length);True(repo.SourcePlaylists().Single(x=>x.Present).Id>id);
            var ids=repo.SourcePlaylists().Select(x=>x.Id).ToArray();repo.Clear();scan.Scan(f.Source);True(ids.SequenceEqual(repo.SourcePlaylists().Select(x=>x.Id)));
        }
    }
    static void ImportPlaylistFormatsAndEncodings()
    {
        var f=Fixture();var samples=new Dictionary<string,string>{
            [".m3u8"]="#EXTM3U\ntest.wav\ntest.wav\n",
            [".m3u"]="test.wav\ntest.wav\n",
            [".pls"]="[playlist]\nFile2=test.wav\nFile1=test.wav\nNumberOfEntries=2\n",
            [".wpl"]="<?wpl version=\"1.0\"?><smil><body><seq><media src=\"test.wav\"/><media src=\"test.wav\"/></seq></body></smil>",
            [".xspf"]="<playlist xmlns=\"http://xspf.org/ns/0/\"><trackList><track><location>test.wav</location></track><track><location>test.wav</location></track></trackList></playlist>",
            [".asx"]="<ASX><ENTRY><REF HREF=\"test.wav\"/></ENTRY><ENTRY><REF HREF=\"test.wav\"/></ENTRY></ASX>"};
        foreach(var sample in samples){var path=Path.Combine(f.Source,"list"+sample.Key);File.WriteAllText(path,sample.Value);var entries=SourcePlaylistReader.Read(path);Equal(2,entries.Count);True(entries.All(x=>x=="test.wav"));}
        var unicode=AddFolderTrack(f,"","Märchen.wav");Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var ansi=Path.Combine(f.Source,"legacy.m3u");File.WriteAllBytes(ansi,Encoding.GetEncoding(1252).GetBytes("Märchen.wav\n"));Equal("Märchen.wav",SourcePlaylistReader.Read(ansi).Single());
        File.WriteAllText(ansi,"Märchen.wav\n",Encoding.Unicode);Equal("Märchen.wav",SourcePlaylistReader.Read(ansi).Single());
        var xspf=Path.Combine(f.Source,"escaped.xspf");File.WriteAllText(xspf,"<playlist><trackList><track><location>M%C3%A4rchen.wav</location></track></trackList></playlist>");Equal(unicode,SourcePlaylistReader.ResolvePath(xspf,SourcePlaylistReader.Read(xspf).Single()));
        using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);Equal(8,repo.SourcePlaylists().Count);True(repo.SourcePlaylists().All(x=>x.Error==""));
    }
    static void ImportPlaylistErrorsAndNoBasenameGuessing()
    {
        var f=Fixture();AddFolderTrack(f,"A","same.wav");AddFolderTrack(f,"B","same.wav");File.WriteAllText(Path.Combine(f.Source,"bad.m3u8"),"old/same.wav\n../../outside.wav\nhttps://example.invalid/a.mp3\n");
        File.WriteAllText(Path.Combine(f.Source,"entities.xspf"),"<!DOCTYPE playlist [<!ENTITY x SYSTEM 'file:///C:/unread-secret'>]><playlist>&x;</playlist>");
        File.WriteAllText(Path.Combine(f.Source,"stream.m3u8"),"#EXTM3U\n#EXT-X-VERSION:3\nhttp://example.invalid/segment\n");
        File.WriteAllText(Path.Combine(f.Source,"unsupported.fpl"),"opaque format");var before=Snapshot(f.Source);
        using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);Equal(4,repo.SourcePlaylists().Count);Equal(3,repo.SourcePlaylists().Count(x=>x.Error!=""));
        foreach(var row in repo.All())repo.Save(MakeReady(row),"ready");ApplyFolderReady(repo,f);foreach(var list in repo.SourcePlaylists())Equal(0,ImportedLines(f,list).Length);Equal(before,Snapshot(f.Source));
        True(repo.SourcePlaylistEntries(repo.SourcePlaylists().Single(x=>x.Name=="bad").Id).All(x=>x.FileId==""&&x.Problem!=""));
    }
    static void ImportPlaylistOriginalPathAndCollisionSafety()
    {
        var f=Fixture();var path=Path.Combine(f.Source,"mix.m3u8");File.WriteAllText(path,"test.wav\n");using var repo=new ProgressRepository(ProgressWorkspace(f));var scan=new IncrementalProgressService(repo);scan.Scan(f.Source);
        var relocated=Path.Combine(f.Source,"renamed.wav");File.Move(f.File,relocated);scan.Scan(f.Source);var item=repo.All().Single();Equal(f.File,item.OriginalPath);Equal(item.Id,repo.SourcePlaylistEntries(repo.SourcePlaylists().Single().Id).Single().FileId);
        repo.Save(MakeReady(item),"ready");ApplyFolderReady(repo,f);var list=repo.SourcePlaylists().Single();Equal(1,ImportedLines(f,list).Length);
        // A new imported name must never replace a user-owned target playlist.
        File.WriteAllText(Path.Combine(f.Source,"new.m3u8"),"renamed.wav\n");scan.Scan(f.Source);var next=repo.SourcePlaylists().Single(x=>x.Name=="new");var collision=Path.Combine(f.Target,next.TargetPath);File.WriteAllText(collision,"user-owned");
        Throws<IOException>(()=>ApplyFolderReady(repo,f));Equal("user-owned",File.ReadAllText(collision));
    }
    static void ImportPlaylistSchemaMigrationAndAtomicScan()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using(var repo=new ProgressRepository(workspace)){new IncrementalProgressService(repo).Scan(f.Source);using var db=new SqliteDatabase(repo.DatabasePath);db.Query("PRAGMA user_version=2");}
        using(var repo=new ProgressRepository(workspace))
        {
            using(var db=new SqliteDatabase(repo.DatabasePath))Equal("3",db.Query("PRAGMA user_version")[0][0]);Equal(1,repo.All().Count);Equal(1,repo.Folders().Count);
            var first=Path.Combine(f.Source,"first.m3u8");var second=Path.Combine(f.Source,"second.m3u8");File.WriteAllText(first,"test.wav\n");File.WriteAllText(second,"test.wav\n");repo.RegisterSourcePlaylists(f.Source,[first,second],true,null);
            var before=JsonFormat.Serialize(repo.SourcePlaylists());Throws<InvalidOperationException>(()=>repo.RegisterSourcePlaylists(f.Source,[first],true,_=>throw new InvalidOperationException("simulated interruption")));Equal(before,JsonFormat.Serialize(repo.SourcePlaylists()));
        }
    }
}
