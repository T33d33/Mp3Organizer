using System.Text.RegularExpressions;

namespace Mp3Organizer;

public static class AlbumGrouper
{
    public static string Owner(AudioMetadata row) => MetadataNormalizer.Key(row.AlbumArtist) == "VARIOUS ARTISTS" ? "Various Artists" : string.IsNullOrWhiteSpace(row.AlbumArtist) ? (string.IsNullOrWhiteSpace(row.Artist) ? "Unknown Artist" : row.Artist) : row.AlbumArtist;
    public static string Key(AudioMetadata row) => MetadataNormalizer.Key(row.Album) + "|" + row.Year;
}
public static class TargetPathBuilder
{
    public static string SafeName(string value)
    {
        var chars = value.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim().TrimEnd('.');
        if (name.Length == 0) name = "Unknown";
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
        // Do not truncate: collisions and overlong names must remain visible to planning.
        return name;
    }
    public static string Build(AudioMetadata row)
    {
        var title = SafeName(string.IsNullOrWhiteSpace(row.Title) ? Path.GetFileNameWithoutExtension(row.FileName) : row.Title);
        if (string.IsNullOrWhiteSpace(row.Album))
            return Path.Combine(ArtistFolder(string.IsNullOrWhiteSpace(row.Artist) ? "Unknown Artist" : row.Artist), title + row.Extension);
        var album = (row.Year == 0 ? "Unknown Year" : row.Year.ToString()) + " - " + SafeName(row.Album);
        var track = (row.Disc > 1 ? $"D{row.Disc:00} - " : "") + $"{row.Track:00} - {title}{row.Extension}";
        return Path.Combine(ArtistFolder(AlbumGrouper.Owner(row)), album, track);
    }
    private static string ArtistFolder(string artist)
    {
        var name = SafeName(artist);
        return new[] { "_Playlists", ".mp3organizer-backups", ".mp3organizer.json", "playlist-map.json", PlaylistIndexBuilder.FileName, TargetMetadataStore.FileName }.Contains(name, StringComparer.OrdinalIgnoreCase) ? "_Artist - " + name : name;
    }
}
