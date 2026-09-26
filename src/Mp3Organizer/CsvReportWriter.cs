using System.Globalization;
namespace Mp3Organizer;
public sealed class CsvReportWriter
{
    public void Write(string folder, string source, IReadOnlyList<AudioMetadata> rows, CopyPlan? plan, IEnumerable<string>? scanErrors = null)
    {
        PathSafetyGuard.Separate(source, folder);
        if (plan != null && (PathSafetyGuard.Within(folder, plan.Target) || PathSafetyGuard.Within(plan.Target, folder))) throw new IOException("Report location overlaps target.");
        Save("library.csv", ["FullPath", "FileName", "Artist", "AlbumArtist", "Album", "Title", "Track", "Disc", "Year", "Date", "DurationSeconds", "Extension", "Bitrate", "Codec", "Size", "SHA256", "Error"], rows.Select(x => new object?[] { x.FullPath, x.FileName, x.Artist, x.AlbumArtist, x.Album, x.Title, x.Track, x.Disc, x.Year == 0 ? "" : x.Year, x.Date, x.DurationSeconds, x.Extension, x.Bitrate, x.Codec, x.Size, x.Sha256, x.Error }));
        Save("missing-tags.csv", ["FullPath", "MissingFields"], rows.Where(x => x.Error == "").Select(x => new object?[] { x.FullPath, string.Join(";", new[] { string.IsNullOrWhiteSpace(x.Artist) ? "Artist" : "", string.IsNullOrWhiteSpace(x.Album) ? "Album" : "", x.Year == 0 ? "Year" : "" }.Where(s => s != "")) }).Where(x => (string)x[1]! != ""));
        var decisions = plan?.Duplicates ?? new DuplicateDetector(new Sha256EquivalenceVerifier()).Select(rows).Decisions;
        Save("duplicates.csv", ["GroupId", "FullPath", "Decision", "Reason"], decisions.Select(x => new object?[] { x.GroupId, x.Source, x.Decision, x.Reason }));
        Save("conflicts.csv", ["Severity", "Conflict"], (plan?.Conflicts ?? scanErrors?.ToList() ?? new()).Select(x => new object?[] { "Blocking", x }).Concat((plan?.ResolvedConflicts ?? new()).Select(x => new object?[] { "Resolved", x })));
        Save("albums.csv", ["AlbumArtist", "Album", "Year", "TrackCount", "Compilation", "Code"], rows.Where(x => !string.IsNullOrWhiteSpace(x.Album)).GroupBy(x => (Owner: MetadataNormalizer.Key(AlbumGrouper.Owner(x)), Album: AlbumGrouper.Key(x))).Select(g => new object?[] { AlbumGrouper.Owner(g.First()), g.First().Album, g.First().Year, g.Count(), g.Key.Owner == MetadataNormalizer.Key("Various Artists"), AlbumCode(g.Key.Owner, g.Key.Album) }));
        if (plan != null) new M3u8Writer().WriteNew(Path.Combine(folder, PlaylistIndexBuilder.FileName), new PlaylistIndexBuilder().Csv(plan.PlaylistMap, plan.Playlists), source);
        new IdentificationReportWriter().Write(folder,source,rows);
        string AlbumCode(string owner, string albumKey) => plan != null && plan.PlaylistMap.Artists.TryGetValue(owner, out var artist) && artist.Albums.TryGetValue(albumKey, out var album) ? PlaylistNames.AlbumCode(artist, album) : "";
        if (plan != null) Save("copy-plan.csv", ["Source", "Target", "Action", "SHA256", "Reason"], plan.Operations.Select(x => new object?[] { x.Metadata.FullPath, Path.Combine(plan.Target, x.RelativePath), x.Action, x.Metadata.Sha256, "" }).Concat(plan.Duplicates.Where(x => x.Decision == "SkipEquivalent").Select(x => new object?[] { x.Source, "", x.Decision, "", x.Reason })));
        void Save(string name, string[] header, IEnumerable<object?[]> data)
        {
            string Quote(object? value)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            var content = string.Join(",", header.Select(Quote)) + "\r\n" + string.Join("\r\n", data.Select(row => string.Join(",", row.Select(Quote)))) + "\r\n";
            new M3u8Writer().WriteNew(Path.Combine(folder, name), content, source);
        }
    }
    public static void Summary(IReadOnlyList<AudioMetadata> rows)
    {
        var readable = rows.Where(x => x.Error == "").ToList();
        Console.WriteLine($"Total audio files: {rows.Count}\nArtist count: {readable.Select(x => MetadataNormalizer.Key(x.Artist)).Where(x => x != "").Distinct().Count()}\nAlbum count: {readable.Where(x => !string.IsNullOrWhiteSpace(x.Album)).Select(x => (MetadataNormalizer.Key(AlbumGrouper.Owner(x)), AlbumGrouper.Key(x))).Distinct().Count()}\nFiles without artist: {readable.Count(x => string.IsNullOrWhiteSpace(x.Artist))}\nFiles without album: {readable.Count(x => string.IsNullOrWhiteSpace(x.Album))}\nFiles without year: {readable.Count(x => x.Year == 0)}\nLoose tracks: {readable.Count(x => string.IsNullOrWhiteSpace(x.Album))}\nPossible duplicate groups: {new DuplicateDetector(new Sha256EquivalenceVerifier()).CandidateGroups(readable).Count}\nMetadata read errors: {rows.Count - readable.Count}");
    }
}
