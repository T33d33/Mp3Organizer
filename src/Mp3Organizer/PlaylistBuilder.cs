using System.Text;
using System.Text.Json;

namespace Mp3Organizer;

public static class JsonFormat
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options) ?? throw new IOException("Invalid JSON document.");
}
public sealed class PlaylistMapStore
{
    public PlaylistMap Load(string target)
    {
        var path = PathSafetyGuard.Destination(target, "playlist-map.json");
        if (!File.Exists(path))
        {
            if (Directory.Exists(Path.Combine(target, "_Playlists")) || File.Exists(Path.Combine(target, ManagedTarget.Marker))) throw new IOException("Playlist mapping is missing; restore its backup or the saved plan mapping before continuing.");
            return new();
        }
        var map = JsonFormat.Read<PlaylistMap>(File.ReadAllText(path));
        Validate(map);
        return map;
    }
    public static void Validate(PlaylistMap map)
    {
        if (map.SchemaVersion != 1 || map.NextArtistId < 1 || map.Artists.Values.Any(x => x.ArtistId < 1)
            || map.Artists.Values.Select(x => x.ArtistId).Distinct().Count() != map.Artists.Count)
            throw new IOException("Invalid artist IDs or mapping version.");
        if (map.Artists.Any(p => p.Key != MetadataNormalizer.Key(p.Key)) || map.Artists.Values.Any(x => x.ArtistId >= map.NextArtistId))
            throw new IOException("Invalid normalized keys or next artist ID.");
        foreach (var artist in map.Artists.Values)
        {
            if (artist.NextAlbumId < 1 || artist.NextAlbumId > 100 || artist.Albums.Values.Any(x => x.AlbumId < 1 || x.AlbumId >= artist.NextAlbumId || x.AlbumId > 99)
                || artist.Albums.Values.Select(x => x.AlbumId).Distinct().Count() != artist.Albums.Count
                || artist.Albums.Keys.Any(x => x != MetadataNormalizer.Key(x))) throw new IOException("Invalid album IDs or identity keys.");
        }
    }
    public static void ValidateExtension(PlaylistMap previous, PlaylistMap next)
    {
        Validate(next);
        if (next.NextArtistId < previous.NextArtistId) throw new IOException("Artist ID counter cannot decrease.");
        foreach (var (key, old) in previous.Artists)
        {
            if (!next.Artists.TryGetValue(key, out var current) || current.ArtistId != old.ArtistId || current.DisplayName != old.DisplayName || current.NextAlbumId < old.NextAlbumId)
                throw new IOException("Previously assigned artist identities cannot change or disappear.");
            foreach (var (albumKey, album) in old.Albums)
                if (!current.Albums.TryGetValue(albumKey, out var value) || value.AlbumId != album.AlbumId || value.DisplayName != album.DisplayName)
                    throw new IOException("Previously assigned album identities cannot change or disappear.");
            if (current.Albums.Any(x => !old.Albums.ContainsKey(x.Key) && x.Value.AlbumId < old.NextAlbumId)) throw new IOException("Album IDs cannot be reused.");
        }
        if (next.Artists.Any(x => !previous.Artists.ContainsKey(x.Key) && x.Value.ArtistId < previous.NextArtistId)) throw new IOException("Artist IDs cannot be reused.");
    }
}
public sealed class PlaylistBuilder
{
    public static void IncludeRetired(List<PlaylistDefinition> playlists, ManagedTargetState? state)
    {
        if (state == null) return;
        foreach (var retired in state.Playlists.Except(playlists.Select(x => x.RelativePath), StringComparer.OrdinalIgnoreCase)) playlists.Add(new(retired, "#EXTM3U\n"));
    }
    public List<PlaylistDefinition> Build(IReadOnlyList<CopyOperation> tracks, PlaylistMap map)
    {
        PlaylistMapStore.Validate(map);
        var result = new List<PlaylistDefinition>();
        var artistTracks = tracks.SelectMany(track => (track.Metadata.TrackArtists.Length > 0 ? track.Metadata.TrackArtists : [string.IsNullOrWhiteSpace(track.Metadata.Artist) ? "Unknown Artist" : track.Metadata.Artist])
            .Where(x => !string.IsNullOrWhiteSpace(x)).DistinctBy(MetadataNormalizer.Key).Select(artist => (Artist: artist, Track: track)));
        foreach (var group in artistTracks.GroupBy(x => MetadataNormalizer.Key(x.Artist)).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (group.Key == MetadataNormalizer.Key("Various Artists")) continue;
            var display = group.First().Artist;
            var identity = Artist(group.Key, display);
            Add(PlaylistNames.ArtistPath(identity), group.Select(x => x.Track).OrderBy(x => x.Metadata.Album).ThenBy(x => x.Metadata.Disc).ThenBy(x => x.Metadata.Track).ThenBy(x => x.RelativePath, StringComparer.Ordinal));
        }
        foreach (var group in tracks.Where(x => !string.IsNullOrWhiteSpace(x.Metadata.Album)).GroupBy(x => (Owner: MetadataNormalizer.Key(AlbumGrouper.Owner(x.Metadata)), Album: AlbumGrouper.Key(x.Metadata)))
            .OrderBy(x => x.Key.Owner, StringComparer.Ordinal).ThenBy(x => x.Key.Album, StringComparer.Ordinal))
        {
            var first = group.First().Metadata;
            var artist = Artist(group.Key.Owner, AlbumGrouper.Owner(first));
            if (!artist.Albums.TryGetValue(group.Key.Album, out var album)
                && !(first.Year>0&&tracks.Where(x=>MetadataNormalizer.Key(AlbumGrouper.Owner(x.Metadata))==group.Key.Owner&&MetadataNormalizer.Key(x.Metadata.Album)==MetadataNormalizer.Key(first.Album)).Select(x=>x.Metadata.Year).Distinct().Count()==1
                    &&artist.Albums.TryGetValue(MetadataNormalizer.Key(first.Album)+"|0",out album)))
            {
                // Fixed two-digit album IDs make concatenated spoken codes unambiguous.
                if (artist.NextAlbumId > 99) throw new IOException($"Artist {artist.DisplayName} has exhausted the 99 permanent album codes.");
                album = new() { AlbumId = artist.NextAlbumId++, DisplayName = first.Album };
                artist.Albums.Add(group.Key.Album, album);
            }
            Add(PlaylistNames.AlbumPath(artist, album), group.OrderBy(x => x.Metadata.Disc).ThenBy(x => x.Metadata.Track).ThenBy(x => x.RelativePath, StringComparer.Ordinal));
        }
        return result;
        ArtistIdentity Artist(string key, string display)
        {
            if (!map.Artists.TryGetValue(key, out var artist))
            {
                artist = new() { ArtistId = map.NextArtistId, DisplayName = display };
                map.NextArtistId = checked(map.NextArtistId + 1);
                map.Artists.Add(key, artist);
            }
            return artist;
        }
        void Add(string path, IEnumerable<CopyOperation> rows)
        {
            var lines = rows.Select(x => Path.GetRelativePath(Path.Combine(Path.GetTempPath(), "playlist-root", Path.GetDirectoryName(path)!), Path.Combine(Path.GetTempPath(), "playlist-root", x.RelativePath)).Replace('/', '\\'));
            result.Add(new(path, "#EXTM3U\n" + string.Join("\n", lines) + "\n"));
        }
    }
}
public sealed class M3u8Writer
{
    public static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    public void WriteNew(string path, string content, string source)
    {
        PathSafetyGuard.Writable(path, source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PathSafetyGuard.Writable(path, source);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, Utf8);
        writer.Write(content);
    }
}
