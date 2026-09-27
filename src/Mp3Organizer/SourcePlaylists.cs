using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Security.Cryptography;

namespace Mp3Organizer;

public sealed record SourcePlaylist(long Id,string SourceRoot,string Path,string Name,bool Present,string Error)
{
    public string OriginalPath {get;init;}="";
    public string Sha256 {get;init;}="";
    public string[] Header {get;init;}=[];
    public string[] Warnings {get;init;}=[];
    public string LastGeneratedUtc {get;init;}="";
    public string Code=>"PL-"+Id.ToString("D4",System.Globalization.CultureInfo.InvariantCulture);
    public string TargetPath=>System.IO.Path.Combine("_Playlists","Original Playlists",$"[{Code}] {TargetPathBuilder.SafeName(Name)}.m3u8");
}
public sealed record SourcePlaylistEntry(int Position,string OriginalEntry,string FileId,string Problem,string Result,string TargetPath)
{
    public string[] Metadata {get;init;}=[];
    public string Category {get;init;}="Pending/error";
}
public sealed record ParsedPlaylistEntry(string Path,string[] Metadata);
public sealed record ParsedPlaylist(string Sha256,List<ParsedPlaylistEntry> Entries,string[] Header,string[] Warnings);

public static class SourcePlaylistReader
{
    public static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".m3u",".m3u8",".pls",".wpl",".xspf",".asx",".zpl",".b4s",".fpl",".cue"};
    public static List<string> Read(string path)=>ReadDocument(path).Entries.Select(x=>x.Path).ToList();
    public static ParsedPlaylist ReadDocument(string path)
    {
        PathSafetyGuard.NoLinks(path);
        using var stream=new ReadOnlySource().OpenRead(path);
        if(stream.Length>8*1024*1024)throw new IOException("Playlist exceeds 8 MiB limit.");
        var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        ParsedPlaylist Simple(IEnumerable<string> values)=>new(hash,values.Select(x=>new ParsedPlaylistEntry(x.Trim(),[])).ToList(),[],[]);
        var extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(extension is ".wpl" or ".xspf" or ".asx")
        {
            using var input=new MemoryStream(bytes);
            using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});
            var doc=XDocument.Load(reader);var root=doc.Root?.Name.LocalName.ToLowerInvariant();
            if(root!=(extension==".wpl"?"smil":extension==".xspf"?"playlist":"asx"))throw new IOException("Unexpected XML playlist root.");
            var entries=extension==".xspf"?doc.Descendants().Where(x=>x.Name.LocalName=="track").Select(x=>{
                var locations=x.Elements().Where(y=>y.Name.LocalName=="location").ToList();
                if(locations.Count!=1)throw new IOException("XSPF track must have exactly one location.");var location=locations[0].Value;
                return Uri.TryCreate(location,UriKind.Absolute,out _)?location:Uri.UnescapeDataString(location);
            }):doc.Descendants().Where(x=>x.Name.LocalName.Equals(extension==".wpl"?"media":"ref",StringComparison.OrdinalIgnoreCase)).Select(x=>x.Attributes().FirstOrDefault(a=>a.Name.LocalName.Equals(extension==".wpl"?"src":"href",StringComparison.OrdinalIgnoreCase))?.Value??throw new IOException("Missing XML playlist location."));
            return Simple(entries);
        }
        if(extension is not (".m3u" or ".m3u8" or ".pls"))throw new IOException("Unsupported playlist format "+extension+"; export it as M3U8 to import its entries.");
        string text;
        try
        {
            if(extension!=".m3u8"&&bytes.Length>=2&&bytes[0]==255&&bytes[1]==254)text=new UnicodeEncoding(false,true,true).GetString(bytes.AsSpan(2));
            else if(extension!=".m3u8"&&bytes.Length>=2&&bytes[0]==254&&bytes[1]==255)text=new UnicodeEncoding(true,true,true).GetString(bytes.AsSpan(2));
            else text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch(DecoderFallbackException) when(extension!=".m3u8")
        {
            // Legacy Windows M3U/PLS commonly use the Western Windows code page.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding(1252).GetString(bytes);
        }
        var lines=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x!="").ToList();
        if(extension==".pls")
        {
            if(!lines.Any(x=>x.Equals("[playlist]",StringComparison.OrdinalIgnoreCase)))throw new IOException("Missing PLS playlist header.");
            var entries=new SortedDictionary<int,string>();int? declared=null;
            foreach(var line in lines)
            {
                var file=Regex.Match(line,@"^File(\d+)\s*=(.*)$",RegexOptions.IgnoreCase);
                if(file.Success){if(!int.TryParse(file.Groups[1].Value,out var number)||number<1||!entries.TryAdd(number,file.Groups[2].Value.Trim()))throw new IOException("Invalid/duplicate PLS entry number.");}
                if(line.StartsWith("NumberOfEntries=",StringComparison.OrdinalIgnoreCase)){if(!int.TryParse(line[(line.IndexOf('=')+1)..],out var count)||count<0)throw new IOException("Invalid PLS count.");declared=count;}
            }
            if(declared.HasValue&&declared.Value!=entries.Count)throw new IOException("PLS count does not match entries.");
            return Simple(entries.Values);
        }
        if(lines.Any(x=>x.StartsWith("#EXT-X-",StringComparison.OrdinalIgnoreCase)))throw new IOException("HLS streaming playlist is not a local music playlist.");
        var parsed=new List<ParsedPlaylistEntry>();var metadata=new List<string>();var header=new List<string>();var warnings=new List<string>();
        foreach(var line in lines)
        {
            if(!line.StartsWith('#')){parsed.Add(new(line,metadata.ToArray()));metadata.Clear();continue;}
            if(line.Equals("#EXTM3U",StringComparison.OrdinalIgnoreCase))continue;
            if(line.StartsWith("#PLAYLIST:",StringComparison.OrdinalIgnoreCase)){header.Add(line);continue;}
            if(Regex.IsMatch(line,@"^#EXTINF:-?\d+(?:\.\d+)?,",RegexOptions.IgnoreCase)||line.StartsWith("#EXTGRP:",StringComparison.OrdinalIgnoreCase)||line.StartsWith("#EXTART:",StringComparison.OrdinalIgnoreCase)||line.StartsWith("#EXTALB:",StringComparison.OrdinalIgnoreCase)||line.StartsWith("# ")||line=="#")metadata.Add(line);
            else warnings.Add("Omitted unsupported/invalid directive: "+line);
        }
        if(metadata.Count>0)warnings.Add("Omitted trailing comments/metadata without a following audio entry.");
        return new(hash,parsed,header.ToArray(),warnings.ToArray());
    }
    public static string ResolvePath(string playlist,string entry)
    {
        var value=entry.Trim().Trim('"');
        if(value.StartsWith("file:",StringComparison.OrdinalIgnoreCase))
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||!uri.IsFile)throw new IOException("Invalid file URI.");value=uri.LocalPath;
        }
        else if(Regex.IsMatch(value,@"^[A-Za-z][A-Za-z0-9+.-]+:")&&!Regex.IsMatch(value,@"^[A-Za-z]:[\\/]"))throw new IOException("Remote/unsupported URI; no network lookup performed.");
        if(string.IsNullOrWhiteSpace(value)||value.Contains('\0'))throw new IOException("Empty or invalid playlist entry.");
        if(value.Length>=2&&value[1]==':'&&!Path.IsPathFullyQualified(value))throw new IOException("Drive-relative playlist entry is ambiguous.");
        return PathSafetyGuard.Canonical(Path.Combine(System.IO.Path.GetDirectoryName(playlist)!,value.Replace('/',System.IO.Path.DirectorySeparatorChar)));
    }
}


