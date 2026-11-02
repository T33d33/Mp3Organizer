namespace Mp3Organizer;
public sealed class FolderRepairService(ProgressRepository repository,string workspace,IReviewConsole console)
{
    public bool Run(string path)
    {
        var folder=Path.TrimEndingDirectorySeparator(PathSafetyGuard.Canonical(path));
        var current=repository.All().FirstOrDefault(x=>string.Equals(Path.GetDirectoryName(x.CurrentPath),folder,StringComparison.OrdinalIgnoreCase)&&x.Status is ProcessingStatus.Processed or ProcessingStatus.Ready or ProcessingStatus.NeedsReview);
        if(current==null)
        {
            console.Write("No eligible indexed tracks directly in this source folder. Select the original source folder containing the audio files (not the organized output). Subfolders and permanently ignored files are not included.");return false;
        }
        console.Write("REPAIR THIS FOLDER — includes previously processed tracks, even with an existing local album decision. No subfolders. Song Artist, Title and Track number are preserved. /cancel or /q leaves it unchanged.");
        return new FolderAlbumEditor(repository,workspace,console).Edit(current).Saved;
    }
}
