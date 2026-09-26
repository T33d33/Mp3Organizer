using System.Diagnostics;
using System.Net.Http;

namespace Mp3Organizer;

public sealed record IdentificationConfiguration(string ApiKey,string Contact,string? Fpcalc)
{
    public static IdentificationConfiguration Read() => new(Environment.GetEnvironmentVariable("ACOUSTID_API_KEY")??"",Environment.GetEnvironmentVariable("MP3ORGANIZER_CONTACT")??"",FindFpcalc(Environment.GetEnvironmentVariable("CHROMAPRINT_FPCALC"),Environment.GetEnvironmentVariable("PATH")??""));
    public static string? FindFpcalc(string? configured,string searchPath)
    {
        if(!string.IsNullOrWhiteSpace(configured))
        {
            configured=configured.Trim().Trim('"');
            if(Path.IsPathRooted(configured)||configured.Contains('/')||configured.Contains('\\'))return File.Exists(configured)?Path.GetFullPath(configured):null;
        }
        var name=string.IsNullOrWhiteSpace(configured)?"fpcalc":configured;
        foreach(var folder in searchPath.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
            foreach(var suffix in Path.HasExtension(name)?new[]{""}:new[]{".exe",""})
            {
                var candidate=Path.Combine(folder.Trim().Trim('"'),name+suffix);
                if(File.Exists(candidate))return Path.GetFullPath(candidate);
            }
        return null;
    }
    public string? Problem => string.IsNullOrWhiteSpace(ApiKey)?"AcoustID API key not configured (ACOUSTID_API_KEY)":
        string.IsNullOrWhiteSpace(Contact)?"MusicBrainz contact not configured (MP3ORGANIZER_CONTACT)":
        Contact.Any(char.IsControl)?"MusicBrainz contact contains invalid control characters":
        Fpcalc==null?"fpcalc executable not found (CHROMAPRINT_FPCALC or PATH)":null;
    public string Status(bool online,bool offline) => !online ? "Online lookup disabled: "+(offline?"--offline requested":"--online not specified (online lookup is opt-in)") :
        Problem is {} reason?"Online lookup disabled: "+reason:"Online lookup enabled";
}

public static class IdentificationDoctor
{
    public static async Task<string> ProbeVersion(string executable)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add("-version");
        using var process=Process.Start(info)??throw new IOException("Could not start fpcalc.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
        try {await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);throw new IOException("fpcalc version probe timed out.");}
        var text=((await stdout)+" "+(await stderr)).Trim();
        if(process.ExitCode!=0||text.Length==0)throw new IOException("fpcalc version probe failed or returned no version.");
        return text;
    }
    public static async Task<string> ProbeMusicBrainz(string contact)
    {
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mp3Organizer/1.1 ("+contact+")");
        using var response=await http.GetAsync("https://musicbrainz.org/ws/2/artist?query=artist%3AMozart&limit=1&fmt=json");
        if(!response.IsSuccessStatusCode)throw new IOException("MusicBrainz returned HTTP "+(int)response.StatusCode);
        return "reachable (HTTP "+(int)response.StatusCode+")";
    }
    public static int Run(string workspace,string? cachePath,bool offline,IdentificationConfiguration? configuration=null,Func<string,Task<string>>? versionProbe=null,Func<string,Task<string>>? connectivityProbe=null)
    {
        var config=configuration??IdentificationConfiguration.Read();var failed=false;
        void Check(string label,Func<string> action)
        {
            try{Console.WriteLine(label+": "+action());}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or HttpRequestException or OperationCanceledException)
            {failed=true;var message=e.Message;if(config.ApiKey.Length>0)message=message.Replace(config.ApiKey,"[redacted]",StringComparison.Ordinal);Console.WriteLine(label+": FAILED - "+message);}
        }
        Check("fpcalc",()=>config.Fpcalc==null?throw new IOException("executable not found (CHROMAPRINT_FPCALC or PATH)"):(versionProbe??ProbeVersion)(config.Fpcalc).GetAwaiter().GetResult());
        Check("AcoustID API key",()=>string.IsNullOrWhiteSpace(config.ApiKey)?throw new IOException("not configured (ACOUSTID_API_KEY)"):"configured (value hidden; validity not tested)");
        Check("MusicBrainz configuration",()=>string.IsNullOrWhiteSpace(config.Contact)||config.Contact.Any(char.IsControl)?throw new IOException("set MP3ORGANIZER_CONTACT to a contact email or URL"):"contact configured");
        Check("MusicBrainz connectivity",()=>offline?"skipped (--offline)":string.IsNullOrWhiteSpace(config.Contact)||config.Contact.Any(char.IsControl)?throw new IOException("contact configuration required"):(connectivityProbe??ProbeMusicBrainz)(config.Contact).GetAwaiter().GetResult());
        // No inventory scan or reference validation: only the explicitly selected workspace is accessed.
        var guard=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workspace))!,".doctor-source-guard");
        Check("Cache database",()=>{var cache=new IdentificationCache(cachePath??Path.Combine(workspace,"cache.db"),guard);cache.CheckWritable();return "writable: "+cache.DatabasePath;});
        Check("Manual resolutions",()=>{var store=new ManualResolutionStore(workspace,guard);var data=store.Load();return File.Exists(store.FilePath)?$"valid ({data.Folders.Count} folders, {data.Files.Count} files)":"not created yet (valid empty state)";});
        return failed?1:0;
    }
}
