using System.Globalization;

namespace Mp3Organizer;

public sealed record PlaylistIndexEntry(string Code, string Type, string Artist, string Album, string PlaylistPath);

public static class PlaylistNames
{
    public static string ArtistCode(ArtistIdentity artist) => artist.ArtistId.ToString(CultureInfo.InvariantCulture);
    public static string AlbumCode(ArtistIdentity artist, AlbumIdentity album) => ArtistCode(artist) + album.AlbumId.ToString("00", CultureInfo.InvariantCulture);
    public static string ArtistPath(ArtistIdentity artist) => Path.Combine("_Playlists", "Artists", $"Code {ArtistCode(artist)} - {TargetPathBuilder.SafeName(artist.DisplayName)}.m3u8");
    public static string AlbumPath(ArtistIdentity artist, AlbumIdentity album) => Path.Combine("_Playlists", "Albums", $"Code {AlbumCode(artist, album)} - {TargetPathBuilder.SafeName(artist.DisplayName)} - {TargetPathBuilder.SafeName(album.DisplayName)}.m3u8");
}

// Pure projection of assigned identities: never allocates or changes an ID.
public sealed class PlaylistIndexBuilder
{
    public const string FileName = "playlist-index.csv";
    public List<PlaylistIndexEntry> Build(PlaylistMap map, IEnumerable<PlaylistDefinition> playlists)
    {
        PlaylistMapStore.Validate(map);
        var paths = playlists.Select(x => x.RelativePath.Replace('/', '\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlaylistIndexEntry>();
        var artists = map.Artists.Values.OrderBy(x => x.ArtistId).ToList();
        foreach (var artist in artists)
            if (MetadataNormalizer.Key(artist.DisplayName) != "VARIOUS ARTISTS")
                Add(PlaylistNames.ArtistCode(artist), "Artist", artist.DisplayName, "", PlaylistNames.ArtistPath(artist));
        foreach (var artist in artists)
            foreach (var album in artist.Albums.Values.OrderBy(x => x.AlbumId))
                Add(PlaylistNames.AlbumCode(artist, album), "Album", artist.DisplayName, album.DisplayName, PlaylistNames.AlbumPath(artist, album));
        foreach(var path in paths.Where(x=>x.StartsWith("_Playlists\\Folders\\",StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x,NaturalPathComparer.Instance))
        {
            var name=Path.GetFileNameWithoutExtension(path);var match=System.Text.RegularExpressions.Regex.Match(name,@"^\[(FOLDER-\d+)\] (.+)$");
            if(match.Success)result.Add(new(match.Groups[1].Value,"Folder","", "",path));
        }
        foreach(var path in paths.Where(x=>(x.StartsWith("_Playlist-Folder\\",StringComparison.OrdinalIgnoreCase)||x.StartsWith("_Playlists\\Original Playlists\\",StringComparison.OrdinalIgnoreCase))).OrderBy(x=>x,NaturalPathComparer.Instance))
        {
            var match=System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(path),@"^\[(PL-\d+)\] ");
            if(match.Success)result.Add(new(match.Groups[1].Value,"ImportedPlaylist","","",path));
        }
        return result;
        void Add(string code, string type, string artist, string album, string path)
        {
            path = path.Replace('/', '\\');
            if (paths.Contains(path)) result.Add(new(code, type, artist, album, path));
        }
    }
    public string Csv(PlaylistMap map, IEnumerable<PlaylistDefinition> playlists)
    {
        string Quote(string value)
        {
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return "Code,Type,Artist,Album,PlaylistPath\r\n" + string.Concat(Build(map, playlists).Select(x => string.Join(",", new[] { x.Code, x.Type, x.Artist, x.Album, x.PlaylistPath }.Select(Quote)) + "\r\n"));
    }
    public static void ValidateDestination(string target, string source, ManagedTargetState? state)
    {
        var path = PathSafetyGuard.Destination(target, FileName, source);
        if (Directory.Exists(path) || (File.Exists(path) && state?.IndexManaged != true)) throw new IOException("Unmanaged playlist-index.csv collision; preserve or relocate that file before continuing.");
    }
}
