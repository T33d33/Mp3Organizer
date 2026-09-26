using System.Text.Json.Serialization;

namespace Mp3Organizer;

public sealed record ManualOverride
{
    public string LastKnownPath {get;init;}="";
    public string[]? AnchorSha256 {get;init;}
    public string? Artist {get;init;}
    public string? AlbumArtist {get;init;}
    public string? Album {get;init;}
    public string? Title {get;init;}
    public uint? TrackNumber {get;init;}
    public uint? DiscNumber {get;init;}
    public uint? Year {get;init;}
}
public sealed class ManualResolutions
{
    public int SchemaVersion {get;set;}=1;
    public SortedDictionary<string,ManualOverride> Folders {get;set;}=new(StringComparer.Ordinal);
    public SortedDictionary<string,ManualOverride> Files {get;set;}=new(StringComparer.Ordinal);
}
public sealed record ManualValidation(int FolderCount,int FileCount,List<string> UnresolvedReferences);
public interface IInventoryAwareMetadataResolver {void SetInventory(IReadOnlyList<AudioMetadata> rows);}
