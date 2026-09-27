namespace Mp3Organizer;

public sealed record AudioMetadata
{
    public string[] SuppressedAutomaticFields {get;init;}=[];
    public string FullPath { get; init; } = "";
    public string OriginalSourcePath {get;init;}="";
    public string FileName { get; init; } = "";
    public string Artist { get; init; } = "";
    public string[] TrackArtists { get; init; } = [];
    public string AlbumArtist { get; init; } = "";
    public string Album { get; init; } = "";
    public string Title { get; init; } = "";
    public uint Track { get; init; }
    public uint Disc { get; init; }
    public uint Year { get; init; }
    public string Date { get; init; } = "";
    public double DurationSeconds { get; init; }
    public string Extension { get; init; } = "";
    public int Bitrate { get; init; }
    public string Codec { get; init; } = "";
    public string CodecFamily { get; init; } = "";
    public bool? Lossless { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public long Size { get; init; }
    public DateTime LastWriteTimeUtc {get;init;}
    public string Sha256 { get; init; } = "";
    public string Error { get; init; } = "";
    public IdentificationEvidence? Identification { get; init; }
}
public sealed record DuplicateDecision(string GroupId, string Source, string Decision, string Reason);
public sealed record CopyOperation(AudioMetadata Metadata, string RelativePath, string Action);
public sealed record PlaylistDefinition(string RelativePath, string Content);
public sealed class ArtistIdentity
{
    public int ArtistId { get; set; }
    public string DisplayName { get; set; } = "";
    public int NextAlbumId { get; set; } = 1;
    public Dictionary<string, AlbumIdentity> Albums { get; set; } = new();
}
public sealed class AlbumIdentity
{
    public int AlbumId { get; set; }
    public string DisplayName { get; set; } = "";
}
public sealed class PlaylistMap
{
    public int SchemaVersion { get; set; } = 1;
    public int NextArtistId { get; set; } = 1;
    public Dictionary<string, ArtistIdentity> Artists { get; set; } = new();
}
public sealed class CopyPlan
{
    public bool SelectedInventoryOnly {get;set;}
    public string ProgressWorkspace {get;set;}="";
    public int MetadataPolicyVersion {get;set;}
    public int SchemaVersion { get; set; } = 1;
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string ExpectedTargetSnapshot { get; set; } = "";
    public string ManualFilePath {get;set;}="";
    public string ManualFileRevision {get;set;}="";
    public List<CopyOperation> Operations { get; set; } = new();
    public List<AudioMetadata> Library { get; set; } = new();
    public List<DuplicateDecision> Duplicates { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
    public List<string> ResolvedConflicts { get; set; } = new();
    public List<PlaylistDefinition> Playlists { get; set; } = new();
    public PlaylistMap PlaylistMap { get; set; } = new();
    public Dictionary<string, AudioMetadata> TargetMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
