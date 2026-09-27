using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void AlbumYearRepairsProcessedTargetAndPlaylists()
    {
        var f=Fixture();File.Copy(f.File,Path.Combine(f.Source,"second.wav"));var ws=ProgressWorkspace(f);var before=Snapshot(f.Source);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var item in repo.All())repo.Save(MakeReady(item) with{Effective=MakeReady(item).Effective with{Year=0}},"unknown year");
        var run=new RunPipelineService(repo,ws);var targets=run.Targets(f.Target);run.ApplyReady(targets,f.Reports);
        var oldPath=new AudioFileScanner().Scan(f.Target).Single();True(oldPath.Contains("Unknown Year"));var mapBefore=File.ReadAllText(Path.Combine(f.Target,"playlist-map.json"));
        var first=repo.All()[0];repo.Save(first with{Effective=first.Effective with{Year=1999,Identification=first.Effective.Identification! with{FieldSources=new(){["Year"]="FileManualOverride"}}}},"manual album year");
        run.ApplyReady(targets,f.Reports);
        True(repo.All().All(x=>x.Status==ProcessingStatus.Processed&&x.Effective.Year==1999));
        var final=new AudioFileScanner().Scan(f.Target).Single();True(final.Contains("1999 - Test Album"));True(!File.Exists(oldPath));Equal(mapBefore,File.ReadAllText(Path.Combine(f.Target,"playlist-map.json")));
        True(repo.Mappings(f.Target).All(x=>x.RelativePath.Contains("1999 - Test Album")));
        foreach(var playlist in Directory.EnumerateFiles(Path.Combine(f.Target,"_Playlists"),"*.m3u8",SearchOption.AllDirectories))
            foreach(var line in File.ReadAllLines(playlist).Where(x=>x.Length>0&&!x.StartsWith('#')))True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(playlist)!,line))));
        Equal(before,Snapshot(f.Source));var after=Snapshot(f.Target);run.ApplyReady(targets,f.Reports);Equal(after,Snapshot(f.Target));
    }
    static void AlbumYearRepairCollisionAndRecovery()
    {
        var f=Fixture();var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);var row=repo.All().Single();repo.Save(MakeReady(row) with{Effective=MakeReady(row).Effective with{Year=0}},"year missing");
        var run=new RunPipelineService(repo,ws);var targets=run.Targets(f.Target);run.ApplyReady(targets,f.Reports);row=repo.All().Single();
        repo.Save(row with{Effective=row.Effective with{Year=1999,Identification=row.Effective.Identification! with{FieldSources=new(){["Year"]="MusicBrainz"},YearSource="MusicBrainz",YearEnriched=true,YearConfidence=.95,AlbumGroupId="verified-group"}}},"year answer");
        var manifest=JsonFormat.Read<Dictionary<string,AudioMetadata>>(File.ReadAllText(Path.Combine(f.Target,TargetMetadataStore.FileName)));var old=manifest.Single();var corrected=old.Value with{Year=1999};var next=TargetPathBuilder.Build(corrected);var full=Path.Combine(f.Target,next);Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllText(full,"collision");
        var repair=new AlbumYearCorrectionService(repo,ws);var threw=false;try{repair.Reconcile(f.Source,f.Target);}catch(IOException){threw=true;}True(threw);True(File.Exists(Path.Combine(f.Target,old.Key)));Equal("collision",File.ReadAllText(full));
        File.Delete(full); // synthetic collision only
        File.WriteAllText(Path.Combine(f.Target,".album-year-repair.json"),JsonFormat.Serialize(new[]{new AlbumYearCorrectionService.MoveEntry(old.Key,next,corrected)}));
        File.Move(Path.Combine(f.Target,old.Key),full);repair.Reconcile(f.Source,f.Target);True(!File.Exists(Path.Combine(f.Target,".album-year-repair.json")));True(repo.Mappings(f.Target).All(x=>x.RelativePath==next));
    }
}
