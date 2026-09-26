using System.Net.Http;
using System.Text.Json;

namespace Mp3Organizer;

public sealed class MusicBrainzClient(IdentificationHttp transport, string userAgent) : IMusicBrainzClient
{
    public async Task<RecordingLookup> LookupAsync(string recordingId, bool online, CancellationToken ct = default)
    {
        if (!Guid.TryParse(recordingId, out _)) return new(null, [], "Invalid recording ID.");
        var recordingResponse = await Get("recording/" + recordingId + "?inc=artist-credits&fmt=json", online, ct);
        if (recordingResponse.Error != "") return new(null, [], recordingResponse.Error);
        RecordingInfo? recording = null;
        try
        {
            using var json = JsonDocument.Parse(recordingResponse.Body); var r = json.RootElement;
            if (Text(r,"id") != recordingId) return new(null, [], "MusicBrainz recording ID mismatch.");
            recording = new(recordingId, Artist(r), Text(r,"title"), r.TryGetProperty("length",out var length) && length.ValueKind == JsonValueKind.Number ? length.GetDouble()/1000 : 0, Text(r,"disambiguation"));
            var releases = new List<ReleaseInfo>();
            for (var offset = 0; offset < 500;)
            {
                var response = await Get($"release?recording={recordingId}&inc=artist-credits%2Brecordings%2Brelease-groups&fmt=json&limit=100&offset={offset}", online, ct);
                if (response.Error != "") return new(recording, [], "Release information unavailable: " + response.Error);
                using var releaseJson = JsonDocument.Parse(response.Body); var root = releaseJson.RootElement;
                var page = root.GetProperty("releases");
                foreach (var release in page.EnumerateArray())
                {
                    var id = Text(release,"id"); if (!Guid.TryParse(id, out _)) continue;
                    uint.TryParse(Text(release,"date").Split('-')[0],out var year);
                    var compilation = release.TryGetProperty("release-group", out var group) && group.TryGetProperty("secondary-types",out var types) && types.EnumerateArray().Any(x => x.GetString() == "Compilation");
                    if (!release.TryGetProperty("media", out var media)) continue;
                    foreach (var disc in media.EnumerateArray())
                    {
                        if (!disc.TryGetProperty("tracks",out var tracks)) continue;
                        foreach (var track in tracks.EnumerateArray())
                            if (track.TryGetProperty("recording",out var trackRecording) && Text(trackRecording,"id") == recordingId)
                                releases.Add(new(id, Text(release,"title"), Artist(release), year, Number(track,"position"), Number(disc,"position"), Text(release,"status"), compilation,group.ValueKind==JsonValueKind.Object?Text(group,"id"):"",group.ValueKind==JsonValueKind.Object?AlbumYearClient.Year(Text(group,"first-release-date")):0));
                    }
                }
                var total = root.GetProperty("release-count").GetInt32();
                var pageCount = page.GetArrayLength();
                if (pageCount == 0 && offset < total) return new(recording,[],"Incomplete release pagination; review album.");
                offset += pageCount;
                if (offset >= total) return new(recording, releases,"",recordingResponse.CacheHit);
            }
            return new(recording, [], "More than 500 releases; album requires review.");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return new(recording, [], "Invalid MusicBrainz response; review required."); }
    }
    private Task<CachedHttpResult> Get(string path, bool online, CancellationToken ct) => transport.GetAsync("musicbrainz:" + path, () =>
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://musicbrainz.org/ws/2/" + path);
        request.Headers.UserAgent.ParseAdd(userAgent); request.Headers.Accept.ParseAdd("application/json"); return request;
    }, online, ct);
    private static string Text(JsonElement element,string name) => element.TryGetProperty(name,out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static uint Number(JsonElement element,string name) => element.TryGetProperty(name,out var value) && value.TryGetUInt32(out var n) ? n : 0;
    private static string Artist(JsonElement element) => element.TryGetProperty("artist-credit",out var credit) ? string.Concat(credit.EnumerateArray().Select(x => Text(x,"name") + Text(x,"joinphrase"))) : "";
}
