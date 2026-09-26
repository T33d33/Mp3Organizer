using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mp3Organizer;

public interface ILookupClock
{
    DateTimeOffset Now { get; }
    Task Delay(TimeSpan delay, CancellationToken ct);
}
public sealed class LookupClock : ILookupClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
    public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
public sealed class RequestRateLimiter(TimeSpan interval, ILookupClock clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset next = DateTimeOffset.MinValue;
    public async Task WaitAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var delay = next - clock.Now;
            if (delay > TimeSpan.Zero) await clock.Delay(delay, ct);
            next = clock.Now.Add(interval);
        }
        finally { gate.Release(); }
    }
}
