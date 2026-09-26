namespace Mp3Organizer;

public enum MetadataSource { Tags, AcoustIdMusicBrainz, FolderFallback, FolderManualOverride, FileManualOverride }
public sealed record TagMetadata(string Artist, string AlbumArtist, string Album, string Title, uint Track, uint Disc, uint Year)
{
    public static TagMetadata From(AudioMetadata x) => new(x.Artist, x.AlbumArtist, x.Album, x.Title, x.Track, x.Disc, x.Year);
}
public sealed record IdentificationEvidence
{
    public TagMetadata Original { get; init; } = new("", "", "", "", 0, 0, 0);
    public Dictionary<string,string> FieldSources {get;init;}=new();
    public Dictionary<string,string>? BeforeManualFieldSources {get;init;}
    public MetadataSource Source { get; init; }
    public double? AcoustIdScore { get; init; }
    public string AcoustId { get; init; } = "";
    public string RecordingId { get; init; } = "";
    public string ReleaseId { get; init; } = "";
    public bool ConfidentRecording { get; init; }
    public string Status { get; init; } = "TagsAccepted";
    public string ReviewReason { get; init; } = "";
    public string Evidence { get; init; } = "";
    public bool FingerprintCacheHit {get;init;}
    public bool IdentificationCacheHit {get;init;}
    public bool ManualOverrideHit {get;init;}
    public string LookupStatus {get;init;}="NotNeeded";
    public TagMetadata? BeforeManualMetadata {get;init;}
    public MetadataSource BeforeManualSource {get;init;}=MetadataSource.Tags;
}
public sealed record AudioFingerprint(double Duration, string Fingerprint, bool CacheHit=false, string Algorithm="Chromaprint/2",string GeneratorVersion="fpcalc-json-v1");
public sealed record AcousticCandidate(string AcoustId, double Score, string RecordingId);
public sealed record AcousticLookup(List<AcousticCandidate> Candidates, string Error = "",bool CacheHit=false);
public sealed record RecordingInfo(string Id, string Artist, string Title, double Duration, string Disambiguation = "");
public sealed record ReleaseInfo(string Id, string Album, string AlbumArtist, uint Year, uint Track, uint Disc, string Status = "Official", bool Compilation = false);
public sealed record RecordingLookup(RecordingInfo? Recording, List<ReleaseInfo> Releases, string Error = "",bool CacheHit=false);
public interface IAudioFingerprintService { Task<AudioFingerprint?> FingerprintAsync(AudioMetadata file, bool allowCompute, CancellationToken ct = default); }
public interface IAcoustIdClient { Task<AcousticLookup> LookupAsync(AudioFingerprint fingerprint, bool online, CancellationToken ct = default); }
public interface IMusicBrainzClient { Task<RecordingLookup> LookupAsync(string recordingId, bool online, CancellationToken ct = default); }
public interface IMetadataResolver { Task<AudioMetadata> ResolveAsync(AudioMetadata file, IReadOnlyList<AudioMetadata> neighbors, bool identifyAll = false, CancellationToken ct = default); }
