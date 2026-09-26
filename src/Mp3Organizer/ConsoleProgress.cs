using System.Diagnostics;

namespace Mp3Organizer;

public sealed class ConsoleProgress
{
    private readonly Stopwatch elapsed=Stopwatch.StartNew();
    private TimeSpan lastScan=TimeSpan.FromSeconds(-1);
    public void Write(string message) => Console.WriteLine($"[{elapsed.Elapsed:hh\\:mm\\:ss}] {message.Replace('\r',' ').Replace('\n',' ')}");
    public void Scan(int count,string directory)
    {
        if(elapsed.Elapsed-lastScan<TimeSpan.FromSeconds(1))return;
        lastScan=elapsed.Elapsed;Write($"Scanning: {count} audio files found; {directory}");
    }
    public List<AudioMetadata> Read(AudioFileScanner scanner,TagLibMetadataReader reader,string source)
    {
        Write("Scanning folders: "+source);
        var paths=Wait(()=>scanner.Scan(source,Scan),"Scanning folders");Write($"Scan complete: {paths.Count} audio files; {scanner.Errors.Count} warnings.");
        var rows=new List<AudioMetadata>();
        foreach(var path in paths)
        {
            Write($"Reading metadata and SHA-256 [{rows.Count+1}/{paths.Count}]: {Path.GetFileName(path)}");
            rows.Add(Wait(()=>reader.Read(path),"Reading metadata/SHA-256: "+Path.GetFileName(path)));
        }
        Write($"Metadata complete: {rows.Count} files.");return rows;
    }
    private T Wait<T>(Func<T> action,string stage)
    {
        var task=Task.Run(action);
        while(Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult()!=task)Write(stage+" — still working");
        return task.GetAwaiter().GetResult();
    }
}
