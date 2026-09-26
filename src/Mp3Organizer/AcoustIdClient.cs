using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mp3Organizer;

public sealed class AcoustIdClient(IdentificationHttp transport, string applicationKey) : IAcoustIdClient
{
    public async Task<AcousticLookup> LookupAsync(AudioFingerprint fingerprint, bool online, CancellationToken ct = default)
    {
        var duration = Math.Round(fingerprint.Duration, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        var key = "acoustid-recordingids:" + duration + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.Fingerprint)));
        var response = await transport.GetAsync(key, () => new HttpRequestMessage(HttpMethod.Post, "https://api.acoustid.org/v2/lookup")
        {
            // No paths, names, tags, or audio bytes are transmitted.
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client"] = applicationKey, ["duration"] = duration, ["fingerprint"] = fingerprint.Fingerprint, ["meta"] = "recordingids", ["format"] = "json" })
        }, online && !string.IsNullOrWhiteSpace(applicationKey), ct);
        if (response.Error != "") return new([], response.Error,response.CacheHit);
        try
        {
            using var json = JsonDocument.Parse(response.Body);
            var root = json.RootElement;
            if (root.GetProperty("status").GetString() != "ok") return new([], "AcoustID reported an API error.");
            var candidates = new List<AcousticCandidate>();
            foreach (var result in root.GetProperty("results").EnumerateArray())
            {
                if (!result.TryGetProperty("recordings", out var recordings)) continue;
                var score = result.GetProperty("score").GetDouble();
                if (!double.IsFinite(score) || score < 0 || score > 1) continue;
                foreach (var recording in recordings.EnumerateArray())
                {
                    var id = recording.GetProperty("id").GetString() ?? "";
                    if (Guid.TryParse(id, out _)) candidates.Add(new(result.GetProperty("id").GetString() ?? "", score, id));
                }
            }
            return new(candidates,"",response.CacheHit);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return new([], "Invalid AcoustID response."); }
    }
}
