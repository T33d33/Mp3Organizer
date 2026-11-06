using System.Text;
namespace Mp3Organizer;
public sealed record PlaylistFailure(int Line, string Kind, string Entry, string ResolvedPath);
public sealed record PlaylistValidation(string Path, int Entries, int Valid, int Missing, int Malformed, int Duplicates, int Escaping, List<PlaylistFailure> Failures)
{ public bool IsValid => Missing + Malformed + Duplicates + Escaping == 0; }
public sealed class PlaylistValidator
{
    public List<PlaylistValidation> Validate(string target)
    {
        target = PathSafetyGuard.Canonical(target);
        var state = ManagedTarget.Check(target) ?? throw new IOException("Expected a managed target library.");
        return state.Playlists.Where(x=>x.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).Select(Check).ToList();
        PlaylistValidation Check(string relative)
        {
            var path=PathSafetyGuard.Destination(target,relative,state.Source);
            int entries=0,valid=0,missing=0,malformed=0,duplicates=0,escaping=0;
            var failures=new List<PlaylistFailure>(); var seen=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
            string[] lines;
            try { PathSafetyGuard.NoLinks(path); lines=File.ReadAllLines(path,new UTF8Encoding(false,true)); }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or DecoderFallbackException)
            { failures.Add(new(0,"Unreadable playlist: "+e.Message,"",path));return new(path,0,0,0,1,0,0,failures); }
            for(int i=0;i<lines.Length;i++)
            {
                var entry=lines[i];if(string.IsNullOrWhiteSpace(entry)||entry.StartsWith('#'))continue;entries++;
                string resolved="";
                try
                {
                    if(entry.Any(char.IsControl)||entry.Contains("://")||entry.Contains(':'))throw new ArgumentException("Invalid path syntax");
                    resolved=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,entry.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar)));
                    if(!PathSafetyGuard.Within(resolved,target)){escaping++;failures.Add(new(i+1,"Escapes target",entry,resolved));continue;}
                    PathSafetyGuard.NoLinks(resolved);
                    if(!AudioFileScanner.Extensions.Contains(Path.GetExtension(resolved)))throw new ArgumentException("Not an audio path");
                    if(!seen.Add(resolved)){duplicates++;failures.Add(new(i+1,"Duplicate",entry,resolved));continue;}
                    if(!File.Exists(resolved)){missing++;failures.Add(new(i+1,"Missing",entry,resolved));continue;}
                    valid++;
                }
                catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
                { malformed++;failures.Add(new(i+1,"Malformed: "+e.Message,entry,resolved)); }
            }
            return new(path,entries,valid,missing,malformed,duplicates,escaping,failures);
        }
    }
    public int Report(string target,Action<string> output)
    {
        var results=Validate(target);
        foreach(var r in results){output($"{r.Path}: entries {r.Entries}; valid {r.Valid}; missing {r.Missing}; malformed {r.Malformed}; duplicate {r.Duplicates}; escaping {r.Escaping}");foreach(var f in r.Failures)output($"  Line {f.Line}: {f.Kind}: {f.Entry} -> {f.ResolvedPath}");}
        output($"Validated {results.Count} managed playlists; invalid {results.Count(x=>!x.IsValid)}.");return results.All(x=>x.IsValid)?0:3;
    }
}
