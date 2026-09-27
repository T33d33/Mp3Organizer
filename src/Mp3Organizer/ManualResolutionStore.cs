using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mp3Organizer;

public sealed class ManualResolutionStore
{
    public string FilePath {get;}
    private readonly string source;
    private string? loadedHash;
    private static readonly JsonSerializerOptions Options=new(JsonFormat.Options){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public ManualResolutionStore(string workspace,string source,string? target=null)
    {
        PathSafetyGuard.Separate(source,workspace);if(target!=null)PathSafetyGuard.Separate(target,workspace);
        FilePath=PathSafetyGuard.Destination(workspace,"manual-resolutions.json",source);this.source=source;
    }
    public ManualResolutions Load()
    {
        PathSafetyGuard.Writable(FilePath,source);
        if(!File.Exists(FilePath)){loadedHash=null;return new();}
        var bytes=File.ReadAllBytes(FilePath);
        try
        {
            var jsonBytes=bytes.AsSpan().StartsWith(new byte[]{239,187,191})?bytes[3..]:bytes;
            using var document=JsonDocument.Parse(jsonBytes);RejectDuplicateProperties(document.RootElement,"$");
            if(document.RootElement.ValueKind!=JsonValueKind.Object||!document.RootElement.TryGetProperty("schemaVersion",out _)||!document.RootElement.TryGetProperty("folders",out _)||!document.RootElement.TryGetProperty("files",out _))throw new IOException("manual-resolutions.json: schemaVersion, folders and files are required.");
            var data=JsonSerializer.Deserialize<ManualResolutions>(jsonBytes,Options)??throw new IOException("Manual JSON root must be an object.");
            Validate(data);loadedHash=Convert.ToHexString(SHA256.HashData(bytes));return data;
        }
        catch(JsonException e){throw new IOException($"Invalid manual-resolutions.json: {e.Message}; path {e.Path}, line {e.LineNumber}, byte {e.BytePositionInLine}.",e);}
    }
    public string Revision(){Load();return loadedHash??"ABSENT";}
    public static void Validate(ManualResolutions data)
    {
        if(data.SchemaVersion!=1)throw new IOException("manual-resolutions.json: schemaVersion must be 1.");
        if(data.Folders==null||data.Files==null)throw new IOException("manual-resolutions.json: folders and files must be objects.");
        Check(data.Folders,"folders",@"^folder:[A-Za-z0-9_-]{1,128}$");Check(data.Files,"files",@"^sha256:[a-fA-F0-9]{64}$");
        if(data.Folders.Values.GroupBy(x=>PathSafetyGuard.Canonical(x.LastKnownPath),StringComparer.OrdinalIgnoreCase).Any(x=>x.Count()>1))throw new IOException("manual-resolutions.json: multiple folder overrides share a lastKnownPath.");
        static void Check(SortedDictionary<string,ManualOverride> entries,string section,string pattern)
        {
            var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var (key,item) in entries)
            {
                var location=$"$.{section}['{key}']";
                if(!Regex.IsMatch(key,pattern)||!keys.Add(key))throw new IOException(location+": invalid or duplicate stable identity.");
                if(item==null)throw new IOException(location+": override must be an object.");
                if(string.IsNullOrWhiteSpace(item.LastKnownPath)||!Path.IsPathFullyQualified(item.LastKnownPath))throw new IOException(location+".lastKnownPath must be an absolute path.");
                PathSafetyGuard.Canonical(item.LastKnownPath);
                foreach(var text in new[]{item.Artist,item.AlbumArtist,item.Album,item.Title})if(text!=null&&(text.Length>500||text.Any(char.IsControl)))throw new IOException(location+": text fields must be <=500 characters without control characters.");
                if(item.Year is >9999)throw new IOException(location+".year must be null or between 1 and 9999.");
                if(item.TrackNumber is >9999||item.DiscNumber is 0 or >9999)throw new IOException(location+": trackNumber/discNumber must be null or between 1 and 9999.");
                if(item.AnchorSha256!=null&&item.AnchorSha256.Any(x=>x==null||!Regex.IsMatch(x,@"^[a-fA-F0-9]{64}$")))throw new IOException(location+".anchorSha256 contains an invalid hash.");
            }
        }
    }
    private static void RejectDuplicateProperties(JsonElement element,string path)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var property in element.EnumerateObject()) {if(!names.Add(property.Name))throw new IOException($"manual-resolutions.json: duplicate property {path}.{property.Name}.");RejectDuplicateProperties(property.Value,path+"."+property.Name);}
        }
        else if(element.ValueKind==JsonValueKind.Array){var i=0;foreach(var value in element.EnumerateArray())RejectDuplicateProperties(value,path+"["+i+++"]");}
    }
    public void Save(ManualResolutions data)
    {
        Validate(data);
        var expected=loadedHash;
        // Always validate disk again. Never overwrite invalid JSON or concurrent direct edits.
        Load();if(expected!=loadedHash)throw new IOException("manual-resolutions.json changed since it was read; reload before saving.");
        var normalized=new ManualResolutions{Folders=new(data.Folders,StringComparer.Ordinal),Files=new(data.Files,StringComparer.Ordinal)};
        var text=JsonSerializer.Serialize(normalized,Options)+"\n";
        var temporary=FilePath+".tmp-"+Guid.NewGuid().ToString("N");
        new M3u8Writer().WriteNew(temporary,text,source);
        PathSafetyGuard.Writable(FilePath,source);
        if(File.Exists(FilePath))
        {
            var backup=FilePath+".bak-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+"-"+Guid.NewGuid().ToString("N");
            PathSafetyGuard.Writable(backup,source);File.Replace(temporary,FilePath,backup);
        }
        else File.Move(temporary,FilePath,false);
        loadedHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FilePath)));
        WriteOverview(normalized);
    }
    public ManualValidation ValidateReferences(ManualResolutions data)
    {
        Validate(data);var unresolved=new List<string>();
        foreach(var (key,item) in data.Folders)if(!Directory.Exists(item.LastKnownPath))unresolved.Add(key+": folder path not found: "+item.LastKnownPath);
        foreach(var (key,item) in data.Files)
        {
            if(!File.Exists(item.LastKnownPath)){unresolved.Add(key+": file path not found: "+item.LastKnownPath);continue;}
            using var stream=new ReadOnlySource().OpenRead(item.LastKnownPath);
            if(!key[7..].Equals(Convert.ToHexString(SHA256.HashData(stream)),StringComparison.OrdinalIgnoreCase))unresolved.Add(key+": file content no longer matches lastKnownPath.");
        }
        return new(data.Folders.Count,data.Files.Count,unresolved);
    }
    public void WriteOverview(ManualResolutions data)
    {
        string Q(object? value){var text=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";if(text.Length>0&&"=+-@".Contains(text[0]))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
        var rows=data.Folders.Select(x=>(Type:"Folder",Value:x.Value)).Concat(data.Files.Select(x=>(Type:"File",Value:x.Value)));
        var text="Type,Artist,Album,Title,TrackNumber,Year,LastKnownPath\r\n"+string.Join("\r\n",rows.Select(x=>string.Join(",",new object?[]{x.Type,x.Value.Artist,x.Value.Album,x.Value.Title,x.Value.TrackNumber,x.Value.Year,x.Value.LastKnownPath}.Select(Q))))+"\r\n";
        var path=Path.ChangeExtension(FilePath,".csv");var temporary=path+".tmp-"+Guid.NewGuid().ToString("N");new M3u8Writer().WriteNew(temporary,text,source);PathSafetyGuard.Writable(path,source);File.Move(temporary,path,true);
    }
}
