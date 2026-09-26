using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mp3Organizer;

public interface IFingerprintGenerator {Task<AudioFingerprint> GenerateAsync(AudioMetadata file,CancellationToken ct);}
public sealed class ChromaprintFingerprintService(string executable, IdentificationCache cache,IFingerprintGenerator? generator=null) : IAudioFingerprintService
{
    private readonly IFingerprintGenerator fingerprintGenerator=generator??new ChromaprintGenerator(executable);
    public async Task<AudioFingerprint?> FingerprintAsync(AudioMetadata file, bool allowCompute, CancellationToken ct = default)
    {
        var key = "chromaprint-v1-length120:" + file.Sha256;
        var cached = cache.ShouldRebuild(key)?null:cache.Get(key, true);
        if (cached != null) return JsonFormat.Read<AudioFingerprint>(cached) with{CacheHit=true};
        if (!allowCompute) return null;
        // Hold a read-only, non-write-sharing source handle for the entire subprocess lifetime.
        using var sourceLock = new ReadOnlySource().OpenRead(file.FullPath);
        if (sourceLock.Length != file.Size || Convert.ToHexString(SHA256.HashData(sourceLock)) != file.Sha256) throw new IOException("Source changed since metadata scan; fingerprint not cached.");
        var result=await fingerprintGenerator.GenerateAsync(file,ct);
        cache.Observe(file);cache.Put(key,JsonFormat.Serialize(result with{CacheHit=false}),TimeSpan.MaxValue);
        return result;
    }
}
public sealed class ChromaprintGenerator(string executable):IFingerprintGenerator
{
    private string? version;
    public async Task<AudioFingerprint> GenerateAsync(AudioMetadata file,CancellationToken ct)
    {
        if(version==null)
        {
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add("-version");
            using var probe=Process.Start(info)??throw new IOException("Could not start fpcalc version probe.");
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var outputTask=probe.StandardOutput.ReadToEndAsync(deadline.Token);var errorTask=probe.StandardError.ReadToEndAsync(deadline.Token);
            try{await probe.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){if(!probe.HasExited)probe.Kill(true);throw;}
            var outputVersion=await outputTask;var errorVersion=await errorTask;
            if(probe.ExitCode!=0)throw new IOException("fpcalc version probe failed.");
            version=(outputVersion+" "+errorVersion).Trim();if(version.Length==0)version="fpcalc (version not reported)";
        }
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-json"); start.ArgumentList.Add("-algorithm"); start.ArgumentList.Add("2"); start.ArgumentList.Add("-length"); start.ArgumentList.Add("120"); start.ArgumentList.Add("--"); start.ArgumentList.Add(Path.GetFullPath(file.FullPath));
        using var process = Process.Start(start) ?? throw new IOException("Could not start fpcalc.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        var output = await stdout; await stderr;
        if (process.ExitCode != 0) throw new IOException("fpcalc failed to decode this file (exit " + process.ExitCode + ").");
        using var json = JsonDocument.Parse(output);
        var fingerprint = json.RootElement.GetProperty("fingerprint").GetString() ?? "";
        var duration = json.RootElement.GetProperty("duration").GetDouble();
        if (fingerprint.Length == 0 || fingerprint.Length > 200000 || duration <= 0 || !double.IsFinite(duration)) throw new IOException("Invalid fpcalc output.");
        var result = new AudioFingerprint(duration, fingerprint,GeneratorVersion:version);
        return result;
    }
}
