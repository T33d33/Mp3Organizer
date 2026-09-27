using System.Text.RegularExpressions;

namespace Mp3Organizer;

public sealed class MetadataQualityEvaluator
{


    public static bool Invalid(string value) => string.IsNullOrWhiteSpace(value) || MetadataPlaceholderDetector.IsPlaceholder(value) || MetadataPlaceholderDetector.IsGenericTitle(value)
        || value.Any(char.IsControl) || value.Length > 300 || Regex.IsMatch(value, @"(?i)(upped\s*by|uploaded\s*by|https?://|www\.|\b\d{2,4}\s*kbps\b)");
    public static bool FilenameTitle(AudioMetadata file) => AudioFileScanner.Extensions.Contains(Path.GetExtension(file.Title)) || MetadataPlaceholderDetector.IsGenericTitle(file.Title)
        || MetadataNormalizer.Key(file.Title)==MetadataNormalizer.Key(Path.GetFileNameWithoutExtension(file.FileName)) && FilenameTrackParser.Parse(file.FileName) is { Title.Length: >0 };
    public List<string> Evaluate(AudioMetadata file, IReadOnlyList<AudioMetadata> neighbors)
    {
        var reasons = new List<string>();
        if (Invalid(file.Artist)) reasons.Add(MetadataPlaceholderDetector.IsPlaceholder(file.Artist) ? "PlaceholderArtist" : "Artist missing, generic, or malformed");
        if (Invalid(file.Title) || FilenameTitle(file)) reasons.Add(MetadataPlaceholderDetector.IsGenericTitle(file.Title) && !string.IsNullOrWhiteSpace(file.Title) ? "GenericTrackTitle" : "Title missing, malformed, or filename-like");
        if (Invalid(file.Album)) reasons.Add(MetadataPlaceholderDetector.IsPlaceholder(file.Album) ? "PlaceholderAlbum" : "Album missing or malformed");
        if (!string.IsNullOrWhiteSpace(file.AlbumArtist) && Invalid(file.AlbumArtist)) reasons.Add("Album artist malformed");
        return reasons;
    }
}
public sealed class FilenameMetadataFallback
{
    public static string Clean(string text) => Regex.Replace(Regex.Replace(text, @"(?i)[\s_\-]*(?:upped\s*by|uploaded\s*by|ripped\s*by).*", ""), @"(?i)[\[\(]?\b\d{2,4}\s*(?:kbps|kbit/s)\b[\]\)]?", "").Trim(' ', '-', '_');
    public AudioMetadata Resolve(AudioMetadata file)
    {
        var artist = MetadataQualityEvaluator.Invalid(file.Artist) ? "" : file.Artist;
        var album = MetadataQualityEvaluator.Invalid(file.Album) ? "" : file.Album;
        var title = file.Title; var track = file.Track;
        var filenameHint=FilenameTrackParser.Parse(file.FileName);
        if(track==0&&filenameHint!=null)track=filenameHint.Number;
        if (MetadataQualityEvaluator.Invalid(title) || MetadataQualityEvaluator.FilenameTitle(file))
        {
            title = filenameHint?.Title ?? Clean(Path.GetFileNameWithoutExtension(file.FileName));
            var match = Regex.Match(title, @"^(\d{1,3})[ ._-]+(.+)$");
            if (filenameHint==null && match.Success) { if (track == 0) uint.TryParse(match.Groups[1].Value,out track); title = match.Groups[2].Value; }
            var names = title.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (names.Length == 2 && artist == "") { artist = names[0]; title = names[1]; }
        }
        if (MetadataQualityEvaluator.Invalid(artist)) artist = "";
        if (MetadataQualityEvaluator.Invalid(album)) album = "";
        return file with { Artist = artist, TrackArtists = artist == file.Artist ? file.TrackArtists : artist == "" ? [] : [artist], AlbumArtist = MetadataQualityEvaluator.Invalid(file.AlbumArtist) ? "" : file.AlbumArtist, Album = album, Title = title, Track = track };
    }
}

