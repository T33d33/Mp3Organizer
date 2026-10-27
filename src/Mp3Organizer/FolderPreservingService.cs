using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mp3Organizer;

// Independent of album organization, its workspace, and its metadata decisions.
public sealed class FolderPreservingService
{
    public sealed record Entry(string Name,string Hash,bool Audio);
    public sealed record Plan(string Source,string Target,string Name,List<Entry> Files);
    public sealed class State
    {
        public int Version {get;set;}=1;
        public Dictionary<string,Plan> Folders {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    }
    private const string Marker=".folder-preserving.json";
    private const string Playlist="_Folder.m3u8";
    private static readonly HashSet<string> PlaylistExtensions=new(StringComparer.OrdinalIgnoreCase){".m3u",".m3u8",".pls",".wpl",".asx",".xspf"};
    private static string Hash(string path)
    {
        using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static Plan Preview(string source,string target,string name)
    {
        source=PathSafetyGuard.Canonical(source);target=PathSafetyGuard.Canonical(target);
        PathSafetyGuard.Separate(source,target);
        if(!Directory.Exists(source))throw new DirectoryNotFoundException(source);
        if(string.IsNullOrWhiteSpace(name)||name!=name.Trim()||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name is "." or ".."||name.EndsWith('.')||name.StartsWith('.'))throw new ArgumentException("Use a simple output folder name, without path separators or trailing dots/spaces.");
        var stem=name.Split('.')[0];if(System.Text.RegularExpressions.Regex.IsMatch(stem,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new ArgumentException("Reserved Windows folder name.");
        PathSafetyGuard.Destination(target,name,source);
        var files=new List<Entry>();
        foreach(var path in Directory.EnumerateFiles(source).OrderBy(x=>x,NaturalPathComparer.Instance))
        {
            PathSafetyGuard.NoLinks(path);
            if(PlaylistExtensions.Contains(Path.GetExtension(path)))continue;
            var filename=Path.GetFileName(path);
            if(filename.Contains('\n')||filename.Contains('\r'))throw new IOException("Unsupported newline in filename.");
            files.Add(new(filename,Hash(path),AudioFileScanner.Extensions.Contains(Path.GetExtension(path))));
        }
        if(!files.Any(x=>x.Audio))throw new IOException("No audio files directly in this folder. Select an album subfolder instead.");
        var plan=new Plan(source,target,name,files);Validate(plan,ReadState(target));return plan;
    }
    private static State ReadState(string target)
    {
        var marker=Path.Combine(target,Marker);PathSafetyGuard.NoLinks(marker);
        if(File.Exists(marker))
        {
            var state=JsonSerializer.Deserialize<State>(File.ReadAllText(marker))??throw new IOException("Invalid folder-preserving manifest.");
            if(state.Version!=1)throw new IOException("Unsupported folder-preserving manifest.");return state;
        }
        if(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any(x=>Path.GetFileName(x)!=".folder-preserving.lock"))throw new IOException("Choose an empty output root for folder-preserving mode. Existing organized output is not modified.");
        return new();
    }
    private static void Validate(Plan plan,State state)
    {
        PathSafetyGuard.Separate(plan.Source,plan.Target);
        var previous=state.Folders.FirstOrDefault(x=>x.Key.Equals(plan.Name,StringComparison.OrdinalIgnoreCase));
        var sameSource=state.Folders.Values.FirstOrDefault(x=>x.Source.Equals(plan.Source,StringComparison.OrdinalIgnoreCase));
        if(sameSource!=null&&!sameSource.Name.Equals(plan.Name,StringComparison.OrdinalIgnoreCase))throw new IOException("This source was already assigned to output folder: "+sameSource.Name+". Reuse that name to resume.");
        var destination=PathSafetyGuard.Destination(plan.Target,plan.Name,plan.Source);
        if(previous.Value==null&&(Directory.Exists(destination)||File.Exists(destination)))throw new IOException("Output folder already exists and is not owned by this mode.");
        if(previous.Value!=null&&!previous.Value.Source.Equals(plan.Source,StringComparison.OrdinalIgnoreCase))throw new IOException("That output name belongs to another source folder. Choose a different name.");
        foreach(var file in plan.Files)
        {
            if(Path.GetFileName(file.Name)!=file.Name)throw new IOException("Invalid filename in plan.");
            var source=Path.Combine(plan.Source,file.Name);PathSafetyGuard.NoLinks(source);
            if(Hash(source)!=file.Hash)throw new IOException("Source changed since preview: "+file.Name);
            var path=PathSafetyGuard.Destination(destination,file.Name,plan.Source);
            if(File.Exists(path)&&Hash(path)!=file.Hash)throw new IOException("Existing output differs; nothing will be overwritten: "+path);
        }
        if(previous.Value!=null&&previous.Value.Files.Any(x=>!plan.Files.Any(y=>y.Name.Equals(x.Name,StringComparison.OrdinalIgnoreCase)&&y.Hash==x.Hash)))throw new IOException("Previously copied source files changed or disappeared. Inspect this folder before resuming.");
        PathSafetyGuard.Writable(Path.Combine(destination,Playlist),plan.Source);
    }
    public static void Apply(Plan plan)
    {
        var state=ReadState(plan.Target);Validate(plan,state);
        Directory.CreateDirectory(plan.Target);
        var lockPath=Path.Combine(plan.Target,".folder-preserving.lock");PathSafetyGuard.Writable(lockPath,plan.Source);
        using var gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        // Reload after acquiring the lock. A new root has only this lock file.
        if(File.Exists(Path.Combine(plan.Target,Marker)))state=ReadState(plan.Target);
        Validate(plan,state);
        var existingKey=state.Folders.Keys.FirstOrDefault(x=>x.Equals(plan.Name,StringComparison.OrdinalIgnoreCase));
        state.Folders[existingKey??plan.Name]=plan;
        AtomicWrite(Path.Combine(plan.Target,Marker),JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}),plan.Source);
        var destination=PathSafetyGuard.Destination(plan.Target,plan.Name,plan.Source);Directory.CreateDirectory(destination);
        foreach(var file in plan.Files)
        {
            var source=Path.Combine(plan.Source,file.Name);var path=PathSafetyGuard.Destination(destination,file.Name,plan.Source);
            PathSafetyGuard.NoLinks(source);
            if(!File.Exists(path))
            {
                var staging=path+".copy-"+Guid.NewGuid().ToString("N");
                try{File.Copy(source,staging,false);if(Hash(staging)!=file.Hash)throw new IOException("Copy verification failed: "+file.Name);File.Move(staging,path,false);}
                finally{if(File.Exists(staging))File.Delete(staging);}
            }
            if(Hash(path)!=file.Hash)throw new IOException("Output verification failed: "+file.Name);
        }
        AtomicWrite(Path.Combine(destination,Playlist),"#EXTM3U\n"+string.Join("\n",plan.Files.Where(x=>x.Audio).Select(x=>"./"+x.Name))+"\n",plan.Source);
    }
    private static void AtomicWrite(string path,string text,string source)
    {
        PathSafetyGuard.Writable(path,source);
        if(File.Exists(path)&&File.ReadAllText(path)==text)return;
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,text,new UTF8Encoding(false));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static int Run(string[] args)
    {
        string? source=null;string target=@"E:\Mp3-Ai-test-folders";string? name=null;
        for(var i=1;i<args.Length;i++)switch(args[i])
        {
            case "--target" when i+1<args.Length:target=args[++i];break;
            case "--name" when i+1<args.Length:name=args[++i];break;
            default:if(args[i].StartsWith('-')||source!=null)throw new ArgumentException("preserve-folder [source] [--target path] [--name folder-name]");source=args[i];break;
        }
        Console.WriteLine("FOLDER-PRESERVING MODE — one directory at a time. Existing organizer and review settings are not used.\nCopies retain their filenames and tags. Covers/tracklists are included. Subfolders and original playlist files are excluded; a new folder playlist is generated.\n/q = cancel.");
        if(source==null){Console.Write("Source folder (paste its full path): ");source=Console.ReadLine()?.Trim().Trim('"');}
        if(string.IsNullOrWhiteSpace(source)||source.Equals("/q",StringComparison.OrdinalIgnoreCase))return 0;
        if(name==null){Console.Write("Output folder name ["+Path.GetFileName(Path.TrimEndingDirectorySeparator(source))+"]: ");name=Console.ReadLine()?.Trim();if(name==null||name.Equals("/q",StringComparison.OrdinalIgnoreCase))return 0;if(name=="")name=Path.GetFileName(Path.TrimEndingDirectorySeparator(source));}
        var plan=Preview(source,target,name);
        Console.WriteLine("\nSource: "+plan.Source+"\nDestination: "+Path.Combine(plan.Target,plan.Name));
        foreach(var file in plan.Files)Console.WriteLine((file.Audio?"Audio: ":"Extra: ")+file.Name);
        Console.WriteLine($"Audio files: {plan.Files.Count(x=>x.Audio)}; extras: {plan.Files.Count(x=>!x.Audio)}; child folders excluded: {Directory.EnumerateDirectories(plan.Source).Count()}\nPlaylist: {Playlist}\nEnter = copy and verify this folder | anything else = cancel");
        if(Console.ReadLine()!="")return 0;
        Apply(plan);Console.WriteLine("Verified all copies and playlist entries. Check this output folder before starting the next one.\n"+Path.Combine(plan.Target,plan.Name,Playlist));return 0;
    }
}
