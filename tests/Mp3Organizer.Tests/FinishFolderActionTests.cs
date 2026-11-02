using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void FinishFolderMatchesEnterAndStopsAtBoundary()
    {
        var f=Fixture();var cd4=Path.Combine(f.Source,"MerryChristmasEverybody","CD4");var cd5=Path.Combine(f.Source,"MerryChristmasEverybody","CD5");Directory.CreateDirectory(cd4);Directory.CreateDirectory(cd5);
        File.Copy(f.File,Path.Combine(cd4,"01.wav"));File.Copy(f.File,Path.Combine(cd4,"02.wav"));File.Copy(f.File,Path.Combine(cd5,"01.wav"));
        var before=Snapshot(f.Source);var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())
        {
            var effective=MakeReady(row).Effective with{Album="Christmas",AlbumArtist="Various Artists",Year=2000};
            if(Path.GetFileName(row.CurrentPath)=="01.wav")effective=effective with{Identification=effective.Identification! with{Candidates=[new(TagMetadata.From(effective) with{Title="Lower score"},"","",.3,new()),new(TagMetadata.From(effective) with{Title="Best fit",Album="Other Album",Year=2009},"","",.9,new())]}};
            repo.Save(row with{Effective=effective,Status=row.CurrentPath==f.File?ProcessingStatus.Skipped:ProcessingStatus.NeedsReview},"review");
        }
        var store=new ManualResolutionStore(ws,f.Source);var manual=store.Load();manual.Folders["folder:scope"]=new(){LastKnownPath=cd4,AlbumSetupConfirmed=true,ReviewFileIds=repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==cd4).Select(x=>x.Id).ToArray(),Album="Christmas",AlbumArtist="Various Artists",Year=2000};store.Save(manual);
        var input=new GuidedReviewInput("F","/q");Equal(2,new CandidateReviewService(repo,ws,input,albumFirst:true).Run());
        var finished=repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==cd4).OrderBy(x=>x.CurrentPath).ToArray();True(finished.All(x=>x.Status==ProcessingStatus.Ready));Equal("Best fit",finished[0].Effective.Title);Equal("Test Song",finished[1].Effective.Title);Equal("Other Album",finished[0].Effective.Album);Equal(2009u,finished[0].Effective.Year);Equal("Christmas",finished[1].Effective.Album);Equal(2000u,finished[1].Effective.Year);
        Equal(ProcessingStatus.NeedsReview,repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==cd5).Status);
        True(input.Output.Any(x=>x.Contains("[F] Finish (MerryChristmasEverybody")));Equal(before,Snapshot(f.Source));
    }
    static void GenericFinishIsSeparateAndDirectOnly()
    {
        var f=Fixture();var folder=Path.Combine(f.Source,"Album");var child=Path.Combine(folder,"Disc2");var sibling=Path.Combine(f.Source,"Album-other");Directory.CreateDirectory(child);Directory.CreateDirectory(sibling);
        File.Copy(f.File,Path.Combine(folder,"01.wav"));File.Copy(f.File,Path.Combine(child,"Track 01.wav"));File.Copy(f.File,Path.Combine(sibling,"02.wav"));
        var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=row.CurrentPath==f.File?ProcessingStatus.Skipped:ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Title=""}},"fixture");
        // Force the sibling to remain outside F's scope, regardless of natural path ordering.
        repo.Save(repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==sibling) with{Status=ProcessingStatus.Skipped},"sibling");
        var store=new ManualResolutionStore(ws,f.Source);var data=store.Load();data.Folders["folder:parent"]=new(){LastKnownPath=folder,AlbumSetupConfirmed=true,ReviewFileIds=repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==folder).Select(x=>x.Id).ToArray(),Album="Christmas Collection",AlbumArtist="Test Artist",Year=2000};store.Save(data);
        Equal(1,new CandidateReviewService(repo,ws,new GuidedReviewInput("G","/q"),albumFirst:true).Run());
        foreach(var row in repo.All().Where(x=>Path.GetDirectoryName(x.CurrentPath)==folder))
        {
            Equal(ProcessingStatus.Ready,row.Status);Equal(Path.GetFileNameWithoutExtension(row.CurrentPath),row.Effective.Title);Equal(row.Effective.Title,row.Effective.ExplicitlyConfirmedTitle);True(!ManualMetadataResolver.NeedsInput(row.Effective));
        }
        Equal(ProcessingStatus.Skipped,repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==sibling).Status);
        Equal(ProcessingStatus.NeedsReview,repo.All().Single(x=>Path.GetDirectoryName(x.CurrentPath)==child).Status);
        Equal(0,new CandidateReviewService(repo,ws,new GuidedReviewInput("/q")).Run());
    }
    static void FinishFolderCleansControlCharactersAndContinuesLongFields()
    {
        var f=Fixture();var bad=Path.Combine(f.Source,"04 Il mio nome \u008f nessuno.wav");File.Move(f.File,bad);File.Copy(bad,Path.Combine(f.Source,"05 next.wav"));var ws=ProgressWorkspace(f);using var repo=new ProgressRepository(ws);new IncrementalProgressService(repo).Scan(f.Source);
        foreach(var row in repo.All())repo.Save(MakeReady(row) with{Status=ProcessingStatus.NeedsReview,Effective=MakeReady(row).Effective with{Title="",Artist="Artist\u0001 name"}},"fixture");
        Equal(2,new CandidateReviewService(repo,ws,new GuidedReviewInput("G")).Run());
        True(repo.All().All(x=>x.Status==ProcessingStatus.Ready&&!x.Effective.Title.Any(char.IsControl)&&!x.Effective.Artist.Any(char.IsControl)));
        True(repo.All().Any(x=>x.Effective.Title.Contains("Il mio nome nessuno")));
        foreach(var row in repo.All())repo.Save(row with{Status=ProcessingStatus.NeedsReview,Effective=row.Effective with{Album=new string('a',501)}},"long field");
        var input=new GuidedReviewInput("F");Equal(0,new CandidateReviewService(repo,ws,input).Run());True(input.Output.Any(x=>x.Contains("Text exceeds 500")));True(repo.All().All(x=>x.Status==ProcessingStatus.NeedsReview));
    }
}
