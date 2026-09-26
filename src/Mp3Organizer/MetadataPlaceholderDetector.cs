using System.Text.RegularExpressions;

namespace Mp3Organizer;

// Compare the whole compact label, never substrings of meaningful names.
public static class MetadataPlaceholderDetector
{
    private static string Compact(string value) => Regex.Replace(MetadataNormalizer.Key(value), @"[^\p{L}\p{Nd}]", "");
    public static bool IsPlaceholder(string value) => Regex.IsMatch(Compact(value),
        @"^(?:(?:NO|UNKNOWN|UNNAMED|UNTITLED)(?:ARTIST|ALBUMARTIST|ALBUM|TITLE|TRACK|AUDIO|AUDIOTRACK)?|ARTIST|ALBUMARTIST|ALBUM|TITLE|NA|VARIOUS)\p{Nd}*$");
    public static bool IsGenericTitle(string value)
    {
        var stem = AudioFileScanner.Extensions.Contains(Path.GetExtension(value)) ? Path.GetFileNameWithoutExtension(value) : value;
        var compact = Compact(stem);
        return compact.Length == 0 || IsPlaceholder(stem) || Regex.IsMatch(compact, @"^\p{Nd}*(?:AUDIOTRACK|TRACK|AUDIO|UNTITLED|UNKNOWN)?\p{Nd}*$");
    }
}
