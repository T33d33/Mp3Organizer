using System.Text;
using System.Text.RegularExpressions;

namespace Mp3Organizer;

public static class MetadataNormalizer
{
    public static string Key(string? value) => Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToUpperInvariant();
}
