using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;

namespace Mp3Organizer;

// Compare the whole compact label, never substrings of meaningful names.
public static class MetadataPlaceholderDetector
{
    private static string Compact(string value) => Regex.Replace(MetadataNormalizer.Key(string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark))).Replace("Ł","L"), @"[^\p{L}\p{Nd}]", "");
    public static bool SuspiciousEncoding(string value) => Regex.IsMatch(value, @"(?i)(Œcie¿ka|Dêbiec|\uFFFD|Ã[\u0080-\u00BF]|Å[\u0080-\u00BF])");
    public static bool IsPlaceholder(string value) => SuspiciousEncoding(value) || Regex.IsMatch(Compact(value), @"^(?:NIEZNANY(?:WYKONAWCA|ARTYSTA|ALBUM|TYTUL)|SCIEZKA)\p{Nd}*$") || Regex.IsMatch(Compact(value),
        @"^(?:NO(?:ARTIST|ALBUMARTIST|ALBUM|TITLE|TRACK|AUDIO|AUDIOTRACK)|(?:UNKNOWN|UNNAMED|UNTITLED)(?:ARTIST|ALBUMARTIST|ALBUM|TITLE|TRACK|AUDIO|AUDIOTRACK)?|ARTIST|ALBUMARTIST|ALBUM|TITLE|NA|VARIOUS)\p{Nd}*$");
    public static bool IsGenericTitle(string value)
    {
        var stem = AudioFileScanner.Extensions.Contains(Path.GetExtension(value)) ? Path.GetFileNameWithoutExtension(value) : value;
        var compact = Compact(stem);
        return compact.Length == 0 || IsPlaceholder(stem) || Regex.IsMatch(compact, @"^\p{Nd}*(?:AUDIOTRACK|TRACK|AUDIO|UNTITLED|UNKNOWN)?\p{Nd}*$");
    }
}
