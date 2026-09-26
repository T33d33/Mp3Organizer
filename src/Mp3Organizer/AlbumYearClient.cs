using System.Net.Http;
using System.Text.Json;

namespace Mp3Organizer;

public sealed record AlbumYearCandidate(string GroupId,string Artist,string Album,uint OriginalYear,string[] Types,string Disambiguation="");
public sealed record AlbumYearLookup(List<AlbumYearCandidate> Candidates,bool Complete=true,string Error="",bool CacheHit=false);
public interface IAlbumYearClient {Task<AlbumYearLookup> LookupAsync(string artist,string album,bool online,CancellationToken ct=default);}
public sealed class AlbumYearClient(IdentificationHttp transport,IdentificationCache cache,string userAgent):IAlbumYearClient
{
    private readonly Dictionary<string,AlbumYearLookup> memory=new(StringComparer.Ordinal);
    public async Task<AlbumYearLookup> LookupAsync(string artist,string album,bool online,CancellationToken ct=default)
    {
        var query="artist:\""+Escape(MetadataNormalizer.Key(artist))+"\" AND releasegroup:\""+Escape(MetadataNormalizer.Key(album))+"\"";
        var key="http-album-year-v1:"+query;
        if(memory.TryGetValue(key,out var found))return found with{CacheHit=true};
        var saved=cache.External(key);if(saved!=null)return memory[key]=JsonFormat.Read<AlbumYearLookup>(saved) with{CacheHit=true};
        using var scope=cache.RefreshScope(cache.Status(key)!=null);
        var result=await transport.GetAsync("musicbrainz-album:"+query,()=>
        {
            var request=new HttpRequestMessage(HttpMethod.Get,"https://musicbrainz.org/ws/2/release-group?query="+Uri.EscapeDataString(query)+"&fmt=json&limit=100");
            request.Headers.UserAgent.ParseAdd(userAgent);request.Headers.Accept.ParseAdd("application/json");return request;
        },online,ct);
        AlbumYearLookup lookup;
        try
        {
            if(result.Error!="")lookup=new([],false,result.Error,result.CacheHit);
            else
            {
                using var doc=JsonDocument.Parse(result.Body);var root=doc.RootElement;var rows=root.GetProperty("release-groups");var candidates=new List<AlbumYearCandidate>();
                foreach(var row in rows.EnumerateArray())
                {
                    var id=Text(row,"id");if(!Guid.TryParse(id,out _))continue;
                    var name=string.Concat(row.GetProperty("artist-credit").EnumerateArray().Select(x=>Text(x,"name")+Text(x,"joinphrase")));
                    var types=new List<string>{Text(row,"primary-type")};if(row.TryGetProperty("secondary-types",out var secondary))types.AddRange(secondary.EnumerateArray().Select(x=>x.GetString()??""));
                    candidates.Add(new(id,name,Text(row,"title"),Year(Text(row,"first-release-date")),types.ToArray(),Text(row,"disambiguation")));
                }
                lookup=new(candidates,root.GetProperty("count").GetInt32()<=rows.GetArrayLength(),CacheHit:result.CacheHit);
            }
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){lookup=new([],false,"Malformed album-year response.");}
        var status=lookup.Error!=""?"TransientFailure":lookup.Complete&&lookup.Candidates.Count==1&&lookup.Candidates[0].OriginalYear>0?"Success":"Ambiguous";
        if(!lookup.Error.Contains("Offline",StringComparison.OrdinalIgnoreCase))cache.Put(key,JsonFormat.Serialize(lookup),status=="Success"?TimeSpan.MaxValue:status=="TransientFailure"?TimeSpan.FromMinutes(5):cache.NegativeRetryAge,status);
        memory[key]=lookup;return lookup;
    }
    private static string Escape(string text)=>text.Replace("\\","\\\\").Replace("\"","\\\"");
    private static string Text(JsonElement row,string name)=>row.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    public static uint Year(string date)=>date.Length>=4&&uint.TryParse(date[..4],out var year)&&year>=1000&&year<=DateTime.UtcNow.Year+1?year:0;
}
