using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Mp3Organizer;

public sealed record CachedHttpResult(string Body, string Error = "",bool CacheHit=false);
public sealed class IdentificationHttp(HttpClient http, IdentificationCache cache, RequestRateLimiter limiter, ILookupClock clock)
{
    public async Task<CachedHttpResult> GetAsync(string key, Func<HttpRequestMessage> request, bool online, CancellationToken ct)
    {
        var cached = cache.External("http-v1:" + key);
        if (cached != null) return JsonFormat.Read<CachedHttpResult>(cached) with {CacheHit=true};
        if (!online) return new("", "Offline: response not cached.");
        CachedHttpResult result = new("", "Service unavailable.");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await limiter.WaitAsync(ct);
            TimeSpan retryAfter = TimeSpan.Zero;
            try
            {
                using var message = request();
                using var response = await http.SendAsync(message, ct);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    using var parsed = JsonDocument.Parse(body);
                    result = new(body); break;
                }
                result = new("", "HTTP " + (int)response.StatusCode);
                if (response.StatusCode != HttpStatusCode.RequestTimeout && (int)response.StatusCode != 429 && (int)response.StatusCode < 500) break;
                retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - clock.Now) ?? TimeSpan.Zero;
            }
            catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
            { result = new("", e is JsonException ? "Malformed service JSON." : "Network request failed or timed out."); }
            if (attempt < 3) await clock.Delay(retryAfter > TimeSpan.FromSeconds(1 << attempt) ? retryAfter : TimeSpan.FromSeconds(1 << attempt), ct);
        }
        // Negative lookups (including empty successful results) and failures are cached as well.
        var status=result.Error!=""?"TransientFailure":"Success";
        if(status=="Success" && key.StartsWith("acoustid-"))
        {
            using var parsed=JsonDocument.Parse(result.Body);var root=parsed.RootElement;
            if(root.TryGetProperty("status",out var apiStatus)&&apiStatus.GetString()!="ok")status="TransientFailure";
            else if(root.TryGetProperty("results",out var results))
            {
                var scores=new Dictionary<string,double>();
                foreach(var candidate in results.EnumerateArray())
                    if(candidate.TryGetProperty("recordings",out var recordings)&&candidate.TryGetProperty("score",out var score)&&score.TryGetDouble(out var number))
                        foreach(var recording in recordings.EnumerateArray())if(recording.TryGetProperty("id",out var id)){var identity=id.GetString()??"";scores[identity]=Math.Max(scores.GetValueOrDefault(identity),number);}
                var ordered=scores.Values.OrderDescending().ToList();
                status=ordered.Count==0||ordered[0]<.9?"NoMatch":ordered.Count>1&&ordered[1]>=.85&&ordered[0]-ordered[1]<.05?"Ambiguous":"Success";
            }
        }
        cache.Put("http-v1:" + key, JsonFormat.Serialize(result), status=="Success"?TimeSpan.MaxValue:status=="TransientFailure"?TimeSpan.FromMinutes(5):cache.NegativeRetryAge,status);
        return result;
    }
}
