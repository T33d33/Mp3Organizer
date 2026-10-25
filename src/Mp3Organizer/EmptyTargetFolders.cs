namespace Mp3Organizer;

public static class EmptyTargetFolders
{
    public static int Remove(string source,string target,Action<string>? progress=null)
    {
        PathSafetyGuard.Separate(source,target);
        if(ManagedTarget.Check(target,source)?.MetadataManaged!=true)return 0;
        var count=0;
        foreach(var directory in Directory.EnumerateDirectories(target).ToArray())
        {
            var name=Path.GetFileName(directory);
            if(name.StartsWith('.')||name.StartsWith('_'))continue;
            Visit(directory);
        }
        if(count>0)progress?.Invoke($"Removed {count} empty organized folders.");
        return count;
        void Visit(string directory)
        {
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return;
            PathSafetyGuard.Writable(directory,source);
            foreach(var child in Directory.EnumerateDirectories(directory).ToArray())Visit(child);
            if(!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory,false);count++;
            }
        }
    }
}
