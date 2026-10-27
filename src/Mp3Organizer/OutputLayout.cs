namespace Mp3Organizer;

public static class OutputLayout
{
    private const string FileName="output-layout.txt";
    public static bool KeepFolders(string? workspace)
    {
        if(workspace==null)return false;
        var path=Path.Combine(workspace,FileName);PathSafetyGuard.NoLinks(path);
        if(!File.Exists(path))return false;
        return File.ReadAllText(path).Trim() switch{"folders"=>true,"albums"=>false,_=>throw new IOException("Invalid output layout setting.")};
    }
    public static int Configure(string[] args)
    {
        if(args.Length!=4||args[2]!="--workspace"||args[1] is not ("folders" or "albums"))throw new ArgumentException("output-layout folders|albums --workspace <path>");
        var workspace=PathSafetyGuard.Canonical(args[3]);PathSafetyGuard.Writable(workspace);Directory.CreateDirectory(workspace);
        var gatePath=Path.Combine(workspace,"music-organizer.lock");PathSafetyGuard.Writable(gatePath);
        using var gate=new FileStream(gatePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var repo=new ProgressRepository(workspace);
        if(KeepFolders(workspace)!=(args[1]=="folders")&&repo.All().Any(x=>x.Status==ProcessingStatus.Processed))throw new IOException("This workspace already has processed files. Use a fresh workspace/output to change layout; existing files were not moved.");
        var path=Path.Combine(workspace,FileName);PathSafetyGuard.Writable(path);File.WriteAllText(path,args[1]);
        Console.WriteLine(args[1]=="folders"?"Keep original source folders and filenames enabled. Existing review controls B/M/F/P/D and /back remain available. Album setup applies only to direct folder members. Use the usual 01-04 steps.":"Original album-based output enabled. Use the usual 01-04 steps.");return 0;
    }
}
