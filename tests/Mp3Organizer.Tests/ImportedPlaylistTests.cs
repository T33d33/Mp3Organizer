using Mp3Organizer;
using System.Text;
namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    static string[] ImportedLines(FixturePaths f,SourcePlaylist playlist)=>File.ReadAllLines(Path.Combine(f.Target,playlist.TargetPath)).Where(x=>x!=""&&!x.StartsWith('#')).ToArray();
    static void ImportedPlaylistLegacyFolderMigration()
    {
        var f=Fixture();File.WriteAllText(Path.Combine(f.Source,"mix.m3u8"),"test.wav\n");var before=Snapshot(f.Source);using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row),"ready");ApplyFolderReady(repo,f);
        var list=repo.SourcePlaylists().Single();var current=Path.Combine(f.Target,list.TargetPath);var legacy=Path.Combine("_Playlist-Folder",Path.GetFileName(current));Directory.CreateDirectory(Path.Combine(f.Target,"_Playlist-Folder"));
        File.Move(current,Path.Combine(f.Target,legacy));File.WriteAllText(Path.Combine(f.Target,legacy),File.ReadAllText(Path.Combine(f.Target,legacy)).Replace("..\\..\\","..\\"));var marker=Path.Combine(f.Target,ManagedTarget.Marker);var state=JsonFormat.Read<ManagedTargetState>(File.ReadAllText(marker));
        File.WriteAllText(marker,JsonFormat.Serialize(state with{Playlists=state.Playlists.Select(x=>x==list.TargetPath?legacy:x).ToList()}));
        var code=list.Code;Equal(0,ApplyFolderReady(repo,f));True(File.Exists(current));True(!Directory.Exists(Path.Combine(f.Target,"_Playlist-Folder")));Equal(code,repo.SourcePlaylists().Single().Code);
        foreach(var line in ImportedLines(f,list))True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!,line))));
        True(!ManagedTarget.Check(f.Target)!.Playlists.Contains(legacy));Equal(before,Snapshot(f.Source));
    }
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
        True(lines[0].StartsWith("..\\..\\Test Artist\\"));True(!lines[0].Contains("Album A"));foreach(var line in lines)True(File.Exists(Path.GetFullPath(Path.Combine(f.Target,"_Playlists","Original Playlists",line))));
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
            using(var db=new SqliteDatabase(repo.DatabasePath))Equal("4",db.Query("PRAGMA user_version")[0][0]);Equal(1,repo.All().Count);Equal(1,repo.Folders().Count);
            var first=Path.Combine(f.Source,"first.m3u8");var second=Path.Combine(f.Source,"second.m3u8");File.WriteAllText(first,"test.wav\n");File.WriteAllText(second,"test.wav\n");repo.RegisterSourcePlaylists(f.Source,[first,second],true,null);
            var before=JsonFormat.Serialize(repo.SourcePlaylists());Throws<InvalidOperationException>(()=>repo.RegisterSourcePlaylists(f.Source,[first],true,_=>throw new InvalidOperationException("simulated interruption")));Equal(before,JsonFormat.Serialize(repo.SourcePlaylists()));
        }
    }
    static void ImportPlaylistParentTraversalUnicodeAndEqualNames()
    {
        var f=Fixture();var audio=AddFolderTrack(f,"Ü Album","01 - Märchen Song.wav");
        var one=Path.Combine(f.Source,"Lists","Nested","Favourites.m3u8");Directory.CreateDirectory(Path.GetDirectoryName(one)!);
        File.WriteAllText(one,"../../Ü Album/01 - Märchen Song.wav\n");var two=Path.Combine(f.Source,"Favourites.m3u8");File.WriteAllText(two,audio+"\n");
        using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);var lists=repo.SourcePlaylists();Equal(2,lists.Count);True(lists.Select(x=>x.Code).Distinct().Count()==2);True(lists.All(x=>x.Code.StartsWith("PL-")));
        var id=repo.All().Single(x=>x.CurrentPath==audio).Id;True(lists.All(x=>repo.SourcePlaylistEntries(x.Id).Single().FileId==id));
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Effective=MakeReady(row).Effective with{Artist="Björk",AlbumArtist="Björk",Album="Début",Title="Human Behaviour",Year=1993}},"fixture");
        ApplyFolderReady(repo,f);foreach(var list in lists){var entry=ImportedLines(f,list).Single();True(entry.Contains("Björk"));True(entry.Contains("1993 - Début"));True(File.Exists(Path.GetFullPath(Path.Combine(f.Target,"_Playlists","Original Playlists",entry))));}
    }
    static void ImportPlaylistReliableRenamePreservesIdentityAndBindings()
    {
        var f=Fixture();var original=Path.Combine(f.Source,"Original.m3u8");File.WriteAllText(original,"#EXTM3U\ntest.wav\n");long id;string fileId;
        using(var repo=new ProgressRepository(ProgressWorkspace(f))){new IncrementalProgressService(repo).Scan(f.Source);id=repo.SourcePlaylists().Single().Id;fileId=repo.SourcePlaylistEntries(id).Single().FileId;repo.Save(MakeReady(repo.All().Single()),"ready");ApplyFolderReady(repo,f);}
        var renamed=Path.Combine(f.Source,"Elsewhere","Renamed.m3u8");Directory.CreateDirectory(Path.GetDirectoryName(renamed)!);File.Move(original,renamed);
        using(var repo=new ProgressRepository(ProgressWorkspace(f)))
        {
            new IncrementalProgressService(repo).Scan(f.Source);var list=repo.SourcePlaylists().Single();Equal(id,list.Id);Equal(original,list.OriginalPath);Equal(renamed,list.Path);Equal("Original",list.Name);Equal(fileId,repo.SourcePlaylistEntries(id).Single().FileId);
            Equal(0,ApplyFolderReady(repo,f));Equal(1,ImportedLines(f,list).Length);True(File.Exists(Path.Combine(f.Target,$"_Playlists\\Original Playlists\\[PL-{id:D4}] Original.m3u8")));
        }
    }
    static void ImportPlaylistAmbiguousRenameDoesNotStealId()
    {
        var f=Fixture();var a=Path.Combine(f.Source,"a.m3u8");var b=Path.Combine(f.Source,"b.m3u8");File.WriteAllText(a,"test.wav\n");File.WriteAllText(b,"test.wav\n");
        using var repo=new ProgressRepository(ProgressWorkspace(f));var scan=new IncrementalProgressService(repo);scan.Scan(f.Source);var max=repo.SourcePlaylists().Max(x=>x.Id);
        File.Delete(a);File.Delete(b);File.WriteAllText(Path.Combine(f.Source,"c.m3u8"),"test.wav\n");scan.Scan(f.Source);Equal(3,repo.SourcePlaylists().Count);Equal(2,repo.SourcePlaylists().Count(x=>!x.Present));True(repo.SourcePlaylists().Single(x=>x.Present).Id>max);
    }
    static void ImportPlaylistCommentsAndDetailedStatus()
    {
        var f=Fixture();var second=AddFolderTrack(f,"Album","second.wav",2);var path=Path.Combine(f.Source,"comments.m3u8");
        File.WriteAllText(path,"#EXTM3U\n#PLAYLIST:My Mix\n#EXTINF:1,First\ntest.wav\n#EXTINF:1,Second\n# this comment belongs to Second\nAlbum/second.wav\n#EXTVLCOPT:input-slave=old-path.wav\n#EXTINF:1,First again\ntest.wav\n");
        var before=Snapshot(f.Source);using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var item in repo.All())repo.Save(MakeReady(item) with{Status=item.CurrentPath==second?ProcessingStatus.NeedsReview:ProcessingStatus.Ready},"fixture");
        ApplyFolderReady(repo,f);var list=repo.SourcePlaylists().Single();var content=File.ReadAllText(Path.Combine(f.Target,list.TargetPath));Equal(1,content.Split("#EXTM3U").Length-1);True(content.Contains("#EXTINF:1,First\n..\\"));True(!content.Contains("#EXTINF:1,Second"));True(!content.Contains("input-slave"));True(content.Contains("#PLAYLIST:My Mix"));
        var status=new List<string>();repo.PrintPlaylistStatus(status.Add);var summary=string.Join("\n",status);True(summary.Contains("Playlist entries: 3"));True(summary.Contains("Migrated entries: 2"));True(summary.Contains("Awaiting review: 1"));True(summary.Contains("Migrated playlists: 1"));
        True(list.Warnings.Single().Contains("EXTVLCOPT"));var pending=repo.All().Single(x=>x.CurrentPath==second);repo.Save(pending with{Status=ProcessingStatus.Ready},"confirmed");ApplyFolderReady(repo,f);
        content=File.ReadAllText(Path.Combine(f.Target,list.TargetPath));True(content.IndexOf("#EXTINF:1,First\n")<content.IndexOf("#EXTINF:1,Second\n"));True(content.IndexOf("#EXTINF:1,Second\n")<content.IndexOf("#EXTINF:1,First again\n"));True(content.Contains("# this comment belongs to Second\n..\\"));Equal(before,Snapshot(f.Source));
        status.Clear();repo.PrintPlaylistStatus(status.Add);True(string.Join("\n",status).Contains("Migrated entries: 3"));
    }
    static void ImportPlaylistSchemaThreeUpgradePreservesData()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);Directory.CreateDirectory(workspace);var path=Path.Combine(workspace,"music-organizer.db");var sourceList=Path.Combine(f.Source,"legacy.m3u8");
        using(var db=new SqliteDatabase(path))
        {
            db.Query("CREATE TABLE source_playlists (id INTEGER PRIMARY KEY AUTOINCREMENT,source_root TEXT NOT NULL COLLATE NOCASE,path TEXT NOT NULL COLLATE NOCASE,name TEXT NOT NULL,present INTEGER NOT NULL,error TEXT NOT NULL,UNIQUE(source_root,path))");
            db.Query("CREATE TABLE source_playlist_entries (playlist_id INTEGER NOT NULL,position INTEGER NOT NULL,original_entry TEXT NOT NULL,file_id TEXT NOT NULL,problem TEXT NOT NULL,result TEXT NOT NULL,target_path TEXT NOT NULL,PRIMARY KEY(playlist_id,position))");
            db.Query("INSERT INTO source_playlists VALUES(17,?,?,?,1,'')",f.Source,sourceList,"Legacy");db.Query("INSERT INTO source_playlist_entries VALUES(17,1,'test.wav','known-source-id','','Playable','../target.wav')");db.Query("PRAGMA user_version=3");
        }
        using(var repo=new ProgressRepository(workspace)){var list=repo.SourcePlaylists().Single();Equal("PL-0017",list.Code);Equal(sourceList,list.OriginalPath);Equal("known-source-id",repo.SourcePlaylistEntries(17).Single().FileId);using var db=new SqliteDatabase(path);Equal("4",db.Query("PRAGMA user_version")[0][0]);}
    }
    static void ImportPlaylistWorkflowExample()
    {
        var f=Fixture();var template=SyntheticMp3(f,2017);var album=Path.Combine(f.Source,"Cuphead OST");var lists=Path.Combine(f.Source,"Playlists");Directory.CreateDirectory(album);Directory.CreateDirectory(lists);
        var a=Path.Combine(album,"45 - The Airship.mp3");var b=Path.Combine(album,"55 - Winner Takes All.mp3");File.Copy(template,a);File.Copy(template,b);
        using(var tags=TagLib.File.Create(b)){tags.Tag.Title="Different fixture content identity";tags.Save();}
        var playlist=Path.Combine(lists,"Cuphead OST.m3u8");File.WriteAllText(playlist,"#EXTM3U\n#EXTINF:-1,Kristofer Maddigan - The Airship\n../Cuphead OST/45 - The Airship.mp3\n#EXTINF:-1,Kristofer Maddigan - Winner Takes All\n../Cuphead OST/55 - Winner Takes All.mp3\n#EXTINF:-1,Kristofer Maddigan - The Airship\n../Cuphead OST/45 - The Airship.mp3\n");
        var before=Snapshot(f.Source);var workspace=ProgressWorkspace(f);Equal(0,Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",workspace]));
        using(var repo=new ProgressRepository(workspace))foreach(var item in repo.All())
        {
            if(item.CurrentPath!=a&&item.CurrentPath!=b){repo.Save(item with{Status=ProcessingStatus.Skipped},"test setup files");continue;}
            repo.Save(MakeReady(item) with{Effective=MakeReady(item).Effective with{Artist="Kristofer Maddigan",AlbumArtist="Kristofer Maddigan",Album="Cuphead OST",Year=2017,Track=item.CurrentPath==a?45u:55u,Title=item.CurrentPath==a?"The Airship":"Winner Takes All"}},"verified fixture metadata");
        }
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace,"--target",f.Target]));Equal(before,Snapshot(f.Source));
        using(var repo=new ProgressRepository(workspace))
        {
            var list=repo.SourcePlaylists().Single();var lines=ImportedLines(f,list);Equal(3,lines.Length);Equal(lines[0],lines[2]);Equal(2,new AudioFileScanner().Scan(f.Target).Count);
            var mappings=repo.Mappings(f.Target).ToDictionary(x=>x.FileId);var entries=repo.SourcePlaylistEntries(list.Id);True(entries.All(x=>x.Category=="Migrated"));
            var example="# Verified synthetic MP3 playlist example\n\nGenerated test audio only; no real music was accessed. This example was produced by the actual scan + run commands in the full test suite.\n\nSource playlist: `Playlists\\Cuphead OST.m3u8`\n\n| Original entry | Persisted source-file identity | Verified canonical target |\n|---|---|---|\n"+
                string.Join("\n",entries.Select(x=>$"| `{x.OriginalEntry}` | `{x.FileId}` | `{mappings[x.FileId].RelativePath}` |"))+"\n\nGenerated: `"+list.TargetPath+"`\n\n```m3u\n"+File.ReadAllText(Path.Combine(f.Target,list.TargetPath))+"```\n\nThe repeated Airship entry uses the same persisted source identity and canonical file. Both input audio and the original playlist remained unchanged.\n";
            Directory.CreateDirectory(f.Reports);File.WriteAllText(Path.Combine(f.Reports,"playlist-example.md"),example);
        }
    }
}

