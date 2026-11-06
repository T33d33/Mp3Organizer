using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void ProcessedTrackRepairRenamesAndUpdatesPlaylists()
    {
        var f=Fixture();var path=Path.Combine(f.Source,"Choir - 05 - Kalinka - Legend.wav");File.Move(f.File,path);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        var item=MakeReady(repo.All().Single());repo.Save(item with{Effective=item.Effective with{Artist="Kalinka",TrackArtists=["Kalinka"],Title="Legend",Album="The Legend",AlbumArtist="Choir"}},"faulty parser");
        var run=new RunPipelineService(repo,ws);var targets=run.Targets(f.Target);run.ApplyReady(targets,f.Reports);var old=repo.Mappings(f.Target).Single().RelativePath;
        Equal(0,new TrackRepairService(repo,ws,new GuidedReviewInput("N","/q")).Run(f.Source));Equal("Legend",repo.All().Single().Effective.Title);
        Equal(1,new TrackRepairService(repo,ws,new GuidedReviewInput("N","")).Run(f.Source));Equal(ProcessingStatus.Processed,repo.All().Single().Status);
        run.ApplyReady(targets,f.Reports);var mapping=repo.Mappings(f.Target).Single();True(mapping.RelativePath.Contains("05 - Kalinka"));True(!File.Exists(Path.Combine(f.Target,old)));True(File.Exists(Path.Combine(f.Target,mapping.RelativePath)));True(File.Exists(path));
        var manifest=JsonFormat.Read<Dictionary<string,AudioMetadata>>(File.ReadAllText(Path.Combine(f.Target,TargetMetadataStore.FileName)));Equal("Choir",manifest[mapping.RelativePath].Artist);Equal("Kalinka",manifest[mapping.RelativePath].Title);
        var playlists=Directory.GetFiles(Path.Combine(f.Target,"_Playlists"),"*.m3u8",SearchOption.AllDirectories);True(playlists.Any(x=>x.Contains("Choir")&&File.ReadAllText(x).Contains("05 - Kalinka")));
        run.ApplyReady(targets,f.Reports);Equal(mapping.RelativePath,repo.Mappings(f.Target).Single().RelativePath);
    }
}
