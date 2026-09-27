using Mp3Organizer;
namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    static string AddFolderTrack(FixturePaths f,string folder,string name,byte? variation=null)
    {
        var path=Path.Combine(f.Source,folder,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(f.File,path);
        if(variation!=null){using var stream=new FileStream(path,FileMode.Open,FileAccess.Write);stream.Position=stream.Length-1;stream.WriteByte(variation.Value);}
        return path;
    }
    static int ApplyFolderReady(ProgressRepository repo,FixturePaths f)
    {
        var pipeline=new RunPipelineService(repo,ProgressWorkspace(f));return pipeline.ApplyReady(pipeline.Targets(f.Target),f.Reports);
    }
    static string[] Playable(FixturePaths f,SourceFolder folder)=>File.ReadAllLines(Path.Combine(f.Target,folder.PlaylistPath)).Where(x=>x!=""&&!x.StartsWith('#')).ToArray();
    static void FolderNamesNeverSetMetadata()
    {
        foreach(var name in new[]{"Christmas","Party","Electro","Random MP3","Old Music","Artist - 1999 - Album","07 - Track"})
        {
            var row=Row(Path.Combine(root,name,"audio.mp3"),artist:"",title:"");
            var fallback=new FilenameMetadataFallback().Resolve(row);
            Equal("",fallback.Artist);Equal("",fallback.Album);Equal(0u,fallback.Year);Equal(0u,fallback.Track);
            var resolved=Resolve(new(new FakeFingerprint{Value=null},new FakeAcoust(),new FakeBrainz(),false,false),row);
            Equal("",resolved.Album);Equal("Review",resolved.Identification!.Status);
        }
        var tagged=Row(Path.Combine(root,"Christmas","song.mp3"),artist:"Real Artist",title:"Real Song") with{Album="Real Album",Track=3,Year=1999};
        var fp=new FakeFingerprint();var good=Resolve(Resolver(fp),tagged);Equal("TagsAccepted",good.Identification!.Status);Equal(0,fp.Calls);Equal(TagMetadata.From(tagged),TagMetadata.From(good));
    }
    static void FolderPolicyMigrationReassessesAndPreservesHistory()
    {
        var f=Fixture();var before=Snapshot(f.Source);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);
        var item=repo.All().Single();var basic=item.Basic with{Album="",Track=1};
        repo.Save(item with{Basic=basic,Effective=basic with{Album="Christmas",Identification=new(){Original=TagMetadata.From(basic),Status="Resolved",FieldSources=new(){["Album"]="FolderFallback"}}},Status=ProcessingStatus.Ready},"old folder inference");
        var folder=repo.Folders().Single();var occurrence=repo.Occurrences().Single();
        using(var db=new SqliteDatabase(repo.DatabasePath))db.Query("PRAGMA user_version=1");
        using(var reopened=new ProgressRepository(workspace))
        {
            Equal(1,MetadataPolicy.Migrate(reopened,workspace));var migrated=reopened.All().Single();Equal(ProcessingStatus.Discovered,migrated.Status);Equal(item.Id,migrated.Id);Equal(folder,reopened.Folders().Single());Equal(occurrence,reopened.Occurrences().Single());
            Equal(0,MetadataPolicy.Migrate(reopened,workspace));
            using var db=new SqliteDatabase(repo.DatabasePath);Equal("3",db.Query("PRAGMA user_version")[0][0]);Equal("1",db.Query("SELECT COUNT(*) FROM metadata_policy_audit")[0][0]);True(db.Query("SELECT reason FROM processing_history").Any(x=>x[0]=="old folder inference"));
            True(db.Query("SELECT previous_json FROM metadata_policy_audit")[0][0]!.Contains("Christmas"));
        }
        True(Directory.GetFiles(Path.Combine(workspace,"backups"),"*.db").Length>0);Equal(before,Snapshot(f.Source));
    }
    static void FolderPolicyMigrationPreservesManualAndVerified()
    {
        var f=ProgressFixture(3);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var rows=repo.All();
        var store=new ManualResolutionStore(workspace,f.Source);var data=store.Load();
        // These copies have distinct test identities so the manual override applies only to the first.
        rows[0]=rows[0] with{Basic=rows[0].Basic with{Sha256=new('A',64)}};
        data.Files["sha256:"+rows[0].Basic.Sha256]=new(){Artist="Manual Artist",Album="Manual Album",Title="Manual Title",Year=1984,TrackNumber=7,LastKnownPath=rows[0].CurrentPath};store.Save(data);
        repo.Save(rows[0] with{Status=ProcessingStatus.Ready,Effective=rows[0].Basic with{Album="Christmas",Identification=new(){Status="Resolved",FieldSources=new(){["Album"]="FolderFallback"}}}},"legacy manual pending");
        var basic=rows[1].Basic with{Artist="",Track=1};var online=basic with{Artist="Verified Artist",Identification=new(){Original=TagMetadata.From(basic),Status="Resolved",ConfidentRecording=true,AcoustIdScore=.99,RecordingId=RecordingId,Evidence="unique recording; duration consistent",FieldSources=new(){["Artist"]="AcoustIdMusicBrainz"}}};
        repo.Save(rows[1] with{Basic=basic,Effective=online,Status=ProcessingStatus.Ready},"verified online");
        var tags=rows[2].Basic with{Track=1};repo.Save(rows[2] with{Basic=tags,Effective=tags with{Identification=new(){Original=TagMetadata.From(tags),Status="TagsAccepted"}},Status=ProcessingStatus.Ready},"valid tags");
        Equal(0,MetadataPolicy.Migrate(repo,workspace));True(repo.All().All(x=>x.Status==ProcessingStatus.Ready));
        var manual=repo.All().Single(x=>x.Id==rows[0].Id).Effective;Equal("Manual Album",manual.Album);Equal("Manual Artist",manual.Artist);Equal(1984u,manual.Year);Equal("FileManualOverride",manual.Identification!.FieldSources["Album"]);
        Equal("Verified Artist",repo.All().Single(x=>x.Id==rows[1].Id).Effective.Artist);Equal(3,repo.All().Count(x=>x.Effective.Identification!.MetadataPolicyVersion==2));
    }
    static void FolderPolicySuppressesPreviouslyWrittenAutomaticTags()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var item=repo.All().Single();
        var original=item.Basic with{Artist="",Album="",Track=0,Year=0};var contaminated=item.Basic with{Artist="Folder Artist",Album="Christmas",Track=9,Year=1991};
        var sources=new Dictionary<string,string>{{"Artist","FolderFallback"},{"Album","FolderFallback"},{"TrackNumber","AcoustIdMusicBrainz"},{"Year","AcoustIdMusicBrainz"}};
        repo.Save(item with{Basic=contaminated,Effective=contaminated with{Identification=new(){Original=TagMetadata.From(original),FieldSources=sources,Status="Resolved"}},Status=ProcessingStatus.Ready},"old automatic tag writes");
        Equal(1,MetadataPolicy.Migrate(repo,workspace));var saved=repo.All().Single();var clean=MetadataPolicy.Sanitize(saved.Basic);Equal("",clean.Artist);Equal("",clean.Album);Equal(0u,clean.Track);Equal(0u,clean.Year);
        var resolver=new PolicyInputResolver(new MetadataResolver(new FakeFingerprint{Value=null},new FakeAcoust(),new FakeBrainz(),false,false),repo.All());
        var result=resolver.ResolveAsync(contaminated,[contaminated]).GetAwaiter().GetResult();Equal("",result.Album);Equal("Review",result.Identification!.Status);
    }
    static void FolderDuplicateOccurrencesAndCanonicalReuse()
    {
        var f=Fixture();AddFolderTrack(f,"Christmas","1.wav");AddFolderTrack(f,"Christmas","2.wav");AddFolderTrack(f,"Party","song.wav");
        File.WriteAllText(Path.Combine(f.Source,"Christmas","order.m3u8"),"#EXTM3U\n2.wav\n1.wav\n2.wav\n");var before=Snapshot(f.Source);
        using var repo=new ProgressRepository(ProgressWorkspace(f));new IncrementalProgressService(repo).Scan(f.Source);foreach(var item in repo.All())repo.Save(MakeReady(item),"resolved");
        Equal(4,ApplyFolderReady(repo,f));Equal(1,new AudioFileScanner().Scan(f.Target).Count);Equal(4,repo.Mappings(f.Target).Count);Equal(1,repo.Mappings(f.Target).Select(x=>x.RelativePath).Distinct().Count());
        var christmas=repo.Folders().Single(x=>x.Name=="Christmas");var party=repo.Folders().Single(x=>x.Name=="Party");var lines=Playable(f,christmas);Equal(3,lines.Length);Equal(lines[0],lines[1]);Equal(lines[0],lines[2]);Equal(lines[0],Playable(f,party).Single());
        foreach(var folder in repo.Folders())foreach(var line in Playable(f,folder)){True(!Path.IsPathRooted(line));True(File.Exists(Path.GetFullPath(Path.Combine(f.Target,Path.GetDirectoryName(folder.PlaylistPath)!,line))));}
        True(!File.ReadAllBytes(Path.Combine(f.Target,christmas.PlaylistPath)).Take(3).SequenceEqual(new byte[]{239,187,191}));
        True(File.ReadAllText(Path.Combine(f.Target,"playlist-index.csv")).Contains(christmas.Code+"\",\"Folder\","));Equal(before,Snapshot(f.Source));
    }
    static void FolderNewScanIdAndCrossRunDeduplication()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);long originalId;
        using(var repo=new ProgressRepository(workspace)){new IncrementalProgressService(repo).Scan(f.Source);repo.Save(MakeReady(repo.All().Single()),"ready");Equal(1,ApplyFolderReady(repo,f));originalId=repo.Folders().Single().Id;}
        var added=AddFolderTrack(f,Path.Combine("Container","Old Music"),"song.wav");AddFolderTrack(f,Path.Combine("Other","Old Music"),"song.wav");
        using(var repo=new ProgressRepository(workspace))
        {
            var scan=new IncrementalProgressService(repo);Equal(2,scan.Scan(f.Source).Added);Equal(originalId,repo.Folders().Single(x=>x.CurrentPath==f.Source).Id);Equal(3,repo.Folders().Count);Equal(2,repo.Folders().Count(x=>x.Name=="Old Music"));
            True(repo.Folders().Where(x=>x.Name=="Old Music").Select(x=>x.Code).Distinct().Count()==2);
            foreach(var row in repo.All().Where(x=>x.Status==ProcessingStatus.Discovered))repo.Save(MakeReady(row) with{Effective=MakeReady(row).Effective with{Title="Another manual spelling"}},"ready");
            Equal(2,ApplyFolderReady(repo,f));Equal(1,new AudioFileScanner().Scan(f.Target).Count);True(repo.Mappings(f.Target).All(x=>x.RelativePath==repo.Mappings(f.Target)[0].RelativePath));
            Equal(ProcessingStatus.Processed,repo.All().Single(x=>x.CurrentPath==added).Status);
            var ids=string.Join(",",repo.Folders().Select(x=>x.Code));scan.Scan(f.Source);Equal(ids,string.Join(",",repo.Folders().Select(x=>x.Code)));
        }
    }
    static void FolderRegeneratesOrderWithoutCopying()
    {
        var f=Fixture();AddFolderTrack(f,"Sequence","10.wav",10);AddFolderTrack(f,"Sequence","2.wav",2);AddFolderTrack(f,"Sequence","1.wav",1);
        using var repo=new ProgressRepository(ProgressWorkspace(f));var scan=new IncrementalProgressService(repo);scan.Scan(f.Source);foreach(var row in repo.All())repo.Save(MakeReady(row) with{Effective=MakeReady(row).Effective with{Title="Song "+Path.GetFileNameWithoutExtension(row.CurrentPath)}},"ready");
        Equal(4,ApplyFolderReady(repo,f));var folder=repo.Folders().Single(x=>x.Name=="Sequence");var initial=Playable(f,folder);Equal(3,initial.Length);True(initial[0].Contains("Song 1.wav"));True(initial[1].Contains("Song 2.wav"));True(initial[2].Contains("Song 10.wav"));
        var hashes=string.Join(";",new AudioFileScanner().Scan(f.Target).Select(x=>x+Hash(x)+File.GetLastWriteTimeUtc(x)));
        File.WriteAllText(Path.Combine(folder.CurrentPath,"order.m3u8"),"10.wav\n1.wav\n10.wav\n2.wav\n");scan.Scan(f.Source);Equal(0,ApplyFolderReady(repo,f));var next=Playable(f,folder);True(next.SequenceEqual(new[]{initial[2],initial[0],initial[2],initial[1]}));
        Equal(hashes,string.Join(";",new AudioFileScanner().Scan(f.Target).Select(x=>x+Hash(x)+File.GetLastWriteTimeUtc(x))));
        File.Delete(Path.Combine(f.Target,folder.PlaylistPath));Equal(0,ApplyFolderReady(repo,f));True(next.SequenceEqual(Playable(f,folder)));
    }
    static void FolderPendingInsertAndRemovedMembership()
    {
        var f=Fixture();AddFolderTrack(f,"Sequence","1.wav",1);var middle=AddFolderTrack(f,"Sequence","2.wav",2);AddFolderTrack(f,"Sequence","10.wav",10);
        using var repo=new ProgressRepository(ProgressWorkspace(f));var scan=new IncrementalProgressService(repo);scan.Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=row.CurrentPath==middle?ProcessingStatus.NeedsReview:ProcessingStatus.Ready,Effective=MakeReady(row).Effective with{Title="Song "+Path.GetFileNameWithoutExtension(row.CurrentPath)}},"fixture");
        Equal(3,ApplyFolderReady(repo,f));var folder=repo.Folders().Single(x=>x.Name=="Sequence");Equal(2,Playable(f,folder).Length);True(File.ReadAllText(Path.Combine(f.Target,folder.PlaylistPath)).Contains("#OMITTED:1"));Equal(ProcessingStatus.NeedsReview,repo.All().Single(x=>x.CurrentPath==middle).Status);
        var review=repo.All().Single(x=>x.CurrentPath==middle);repo.Save(review with{Status=ProcessingStatus.Ready},"review accepted");Equal(1,ApplyFolderReady(repo,f));Equal(3,Playable(f,folder).Length);True(Playable(f,folder)[1].Contains("Song 2.wav"));
        // Simulate an external removal from this synthetic fixture, never an application source deletion.
        File.Delete(middle);scan.Scan(f.Source);Equal(0,ApplyFolderReady(repo,f));Equal(2,Playable(f,folder).Length);True(repo.Occurrences().Any(x=>x.CurrentPath==middle&&!x.Present));Equal(4,new AudioFileScanner().Scan(f.Target).Count);
    }
    static void FolderRunCombinesReadyAndNewPending()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);using(var repo=new ProgressRepository(workspace)){new IncrementalProgressService(repo).Scan(f.Source);repo.Save(MakeReady(repo.All().Single()),"already Ready");}
        AddFolderTrack(f,"Added Album","01 - Test Song.wav");Equal(0,Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",workspace]));
        using(var repo=new ProgressRepository(workspace)){Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Ready));Equal(1,repo.All().Count(x=>x.Status==ProcessingStatus.Discovered));}
        Equal(0,Mp3Organizer.Program.Run(["run","--offline","--workspace",workspace,"--target",f.Target]));
        using(var repo=new ProgressRepository(workspace)){Equal(2,repo.All().Count(x=>x.Status==ProcessingStatus.Processed));Equal(2,repo.Folders().Count);True(repo.Folders().All(x=>Playable(f,x).Length==1));}
    }
    static void FolderIdsSurviveResetAndUntrustedOrder()
    {
        var f=Fixture();AddFolderTrack(f,"Old Music","2.wav");AddFolderTrack(f,"Old Music","10.wav");using var repo=new ProgressRepository(ProgressWorkspace(f));var scan=new IncrementalProgressService(repo);scan.Scan(f.Source);var ids=string.Join(",",repo.Folders().Select(x=>x.Code));var occurrences=repo.Occurrences().Select(x=>x.Id).Order().ToArray();
        File.WriteAllText(Path.Combine(f.Source,"Old Music","order.m3u8"),"../test.wav\n");var messages=new List<string>();scan.Scan(f.Source,progress:messages.Add);True(messages.Any(x=>x.Contains("Natural filename order")));
        var folder=repo.Folders().Single(x=>x.Name=="Old Music");var members=repo.Occurrences().ToDictionary(x=>x.Id);Equal("2.wav",Path.GetFileName(members[repo.FolderOrder(folder.Id)[0]].CurrentPath));
        repo.Clear();scan.Scan(f.Source);Equal(ids,string.Join(",",repo.Folders().Select(x=>x.Code)));True(occurrences.SequenceEqual(repo.Occurrences().Select(x=>x.Id).Order()));
        AddFolderTrack(f,"New Folder","1.wav");scan.Scan(f.Source);True(repo.Folders().Single(x=>x.Name=="New Folder").Id>folder.Id);
    }
    static void FolderOldPlanRejected()
    {
        var f=Fixture();var before=Snapshot(f.Source);var plan=new CopyPlanBuilder().Build(f.Source,f.Target);plan.MetadataPolicyVersion=0;
        Throws<IOException>(()=>new SafeCopyExecutor().Execute(plan,f.Source,f.Target,false));True(!Directory.Exists(f.Target));Equal(before,Snapshot(f.Source));
    }
    static void FolderMigrationLegacyProcessedAndFolderManual()
    {
        var f=ProgressFixture(2);var workspace=ProgressWorkspace(f);using var repo=new ProgressRepository(workspace);new IncrementalProgressService(repo).Scan(f.Source);var rows=repo.All();
        var old=rows[0].Basic with{Album="Christmas",Track=1};
        repo.Save(rows[0] with{Basic=old,Effective=old with{Identification=new(){Source=MetadataSource.FolderFallback,Original=TagMetadata.From(old) with{Album=""},Status="Resolved"}},Status=ProcessingStatus.Processed,LastProcessedUtc="2026-01-01T00:00:00Z"},"old processed");
        Equal(1,MetadataPolicy.Migrate(repo,workspace));var migrated=repo.All().Single(x=>x.Id==rows[0].Id);Equal(ProcessingStatus.NeedsReview,migrated.Status);Equal("2026-01-01T00:00:00Z",migrated.LastProcessedUtc);True(migrated.Basic.SuppressedAutomaticFields.Contains("Album"));
        var store=new ManualResolutionStore(workspace,f.Source);var data=store.Load();data.Folders["folder:explicit"] =new(){LastKnownPath=f.Source,Artist="Chosen Artist",Album="Chosen Album",Year=2001,TrackNumber=1};store.Save(data);
        repo.Save(rows[1] with{Status=ProcessingStatus.Ready,Effective=rows[1].Basic with{Album="Christmas",Identification=new(){Source=MetadataSource.FolderFallback,Original=TagMetadata.From(rows[1].Basic),Status="Resolved"}}},"old Ready with explicit folder override");
        Equal(0,MetadataPolicy.Migrate(repo,workspace));var manual=repo.All().Single(x=>x.Id==rows[1].Id);Equal(ProcessingStatus.Ready,manual.Status);Equal("Chosen Album",manual.Effective.Album);Equal("FolderManualOverride",manual.Effective.Identification!.FieldSources["Album"]);
    }
}




