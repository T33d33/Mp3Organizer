namespace Mp3Organizer;
public sealed partial class ProgressRepository
{
    private readonly HashSet<string> excludedFolders=new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> ExcludedFolders=>excludedFolders;
    private static bool Within(string path,string folder)=>string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),folder,StringComparison.OrdinalIgnoreCase);
    private void InitializeExclusions()
    {
        db.Query("CREATE TABLE IF NOT EXISTS excluded_folders (path TEXT PRIMARY KEY COLLATE NOCASE)");
        foreach(var row in db.Query("SELECT path FROM excluded_folders"))excludedFolders.Add(row[0]!);
    }
    public bool IsExcluded(string path)=>excludedFolders.Any(folder=>Within(Path.GetFullPath(path),folder));
    public int ExcludeFolder(string path)
    {
        var folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var rows=All().Where(x=>Within(x.CurrentPath,folder)).ToList();
        if(!Directory.Exists(folder)&&rows.Count==0)throw new DirectoryNotFoundException(folder);
        db.Query("BEGIN IMMEDIATE");
        try
        {
            db.Query("INSERT OR IGNORE INTO excluded_folders VALUES(?)",folder);
            foreach(var row in rows)
            {
                db.Query("UPDATE music_files SET status='Skipped',error_message='' WHERE id=?",row.Id);
                db.Query("INSERT INTO processing_history(file_id,event_utc,status,reason) VALUES(?,?,?,?)",row.Id,DateTime.UtcNow.ToString("O"),"Skipped","Permanently excluded folder: "+folder);
            }
            db.Query("COMMIT");excludedFolders.Add(folder);return rows.Count;
        }
        catch{db.Query("ROLLBACK");throw;}
    }
    public void IncludeFolder(string path)
    {
        var folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if(!excludedFolders.Contains(folder))throw new ArgumentException("This exact folder is not excluded. Use excluded-folders to list exclusions.");
        db.Query("DELETE FROM excluded_folders WHERE path=?",folder);excludedFolders.Remove(folder);
        foreach(var row in All().Where(x=>x.Status==ProcessingStatus.Skipped&&Within(x.CurrentPath,folder)&&!IsExcluded(x.CurrentPath)))
            Save(row with{Status=ProcessingStatus.Discovered},"Folder exclusion removed");
    }
}
