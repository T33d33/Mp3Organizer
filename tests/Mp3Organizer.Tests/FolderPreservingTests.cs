using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void PreserveFolderCopiesAndResumesWithoutMetadataGrouping()
    {
        var f=Fixture();var a=Path.Combine(f.Source,"01 - Żółć.wav");File.Move(f.File,a);
        File.Copy(a,Path.Combine(f.Source,"02 - Different album.wav"));
        File.WriteAllText(Path.Combine(f.Source,"cover.jpg"),"cover");File.WriteAllText(Path.Combine(f.Source,"tracklist.txt"),"tracklist");File.WriteAllText(Path.Combine(f.Source,"old.m3u"),"bad path");
        var sub=Path.Combine(f.Source,"Another album");Directory.CreateDirectory(sub);File.Copy(a,Path.Combine(sub,"child.wav"));
        var before=Snapshot(f.Source);var target=f.Target+"-preserved";var plan=FolderPreservingService.Preview(f.Source,target,"My Collection");Equal(4,plan.Files.Count);True(!Directory.Exists(target));
        FolderPreservingService.Apply(plan);var output=Path.Combine(target,"My Collection");True(File.Exists(Path.Combine(output,"cover.jpg")));True(!Directory.Exists(Path.Combine(output,"Another album")));True(!File.Exists(Path.Combine(output,"old.m3u")));
        var lines=File.ReadAllLines(Path.Combine(output,"_Folder.m3u8"));Equal(3,lines.Length);foreach(var line in lines.Skip(1))True(File.Exists(Path.Combine(output,line)));
        Equal(before,Snapshot(f.Source));var copied=Snapshot(target);FolderPreservingService.Apply(FolderPreservingService.Preview(f.Source,target,"My Collection"));Equal(copied,Snapshot(target));
        File.Delete(Path.Combine(output,"02 - Different album.wav"));FolderPreservingService.Apply(FolderPreservingService.Preview(f.Source,target,"My Collection"));True(File.Exists(Path.Combine(output,"02 - Different album.wav")));
        File.Copy(a,Path.Combine(f.Source,"03 - New.wav"));FolderPreservingService.Apply(FolderPreservingService.Preview(f.Source,target,"My Collection"));Equal(4,File.ReadAllLines(Path.Combine(output,"_Folder.m3u8")).Length);
    }
    static void PreserveFolderRejectsCollisionsAndChanges()
    {
        var f=Fixture();var target=f.Target+"-preserved";var plan=FolderPreservingService.Preview(f.Source,target,"Album");File.AppendAllText(f.File,"changed");var blocked=false;try{FolderPreservingService.Apply(plan);}catch(IOException){blocked=true;}True(blocked);True(!Directory.Exists(target));
        plan=FolderPreservingService.Preview(f.Source,target,"Album");FolderPreservingService.Apply(plan);var dest=Path.Combine(target,"Album",Path.GetFileName(f.File));File.AppendAllText(dest,"conflict");blocked=false;try{FolderPreservingService.Preview(f.Source,target,"Album");}catch(IOException){blocked=true;}True(blocked);True(File.ReadAllText(dest).EndsWith("conflict"));
        var other=Fixture();blocked=false;try{FolderPreservingService.Preview(other.Source,target,"Album");}catch(IOException){blocked=true;}True(blocked);
        blocked=false;try{FolderPreservingService.Preview(f.Source,target,"../escape");}catch(ArgumentException){blocked=true;}True(blocked);
        blocked=false;try{FolderPreservingService.Preview(f.Source,Path.Combine(f.Source,"out"),"Album");}catch(IOException){blocked=true;}True(blocked);
    }
    static void PreserveFolderKeepsOldOutputAndCancellationUntouched()
    {
        var f=Fixture();Directory.CreateDirectory(f.Target);File.WriteAllText(Path.Combine(f.Target,".mp3organizer.json"),"old mode");var before=Snapshot(f.Target);var blocked=false;try{FolderPreservingService.Preview(f.Source,f.Target,"Album");}catch(IOException){blocked=true;}True(blocked);Equal(before,Snapshot(f.Target));
        var input=Console.In;try{Console.SetIn(new StringReader("cancel\n"));Equal(0,FolderPreservingService.Run(["preserve-folder",f.Source,"--target",f.Target+"-fresh","--name","Album"]));}finally{Console.SetIn(input);}True(!Directory.Exists(f.Target+"-fresh"));
    }
}
