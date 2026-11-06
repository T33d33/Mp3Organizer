using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace Mp3Organizer;

// Filesystem-only policy. Never use this to normalize metadata or playlist identities.
public sealed class FileSystemNaming
{
    public string Mode { get; }
    public FileSystemNaming(string mode = "unicode")
    {
        Mode = mode.ToLowerInvariant();
        if (Mode is not ("unicode" or "ascii")) throw new ArgumentException("--filesystem-naming must be unicode or ascii.");
    }
    private static readonly Dictionary<char,string> Special = new()
    {
        ['ä']="ae",['ö']="oe",['ü']="ue",['Ä']="Ae",['Ö']="Oe",['Ü']="Ue",['ß']="ss",['ẞ']="SS",
        ['ł']="l",['Ł']="L",['æ']="ae",['Æ']="Ae",['œ']="oe",['Œ']="Oe",['ø']="o",['Ø']="O",['đ']="d",['Đ']="D",
        ['а']="a",['б']="b",['в']="v",['г']="g",['д']="d",['е']="e",['ё']="yo",['ж']="zh",['з']="z",['и']="i",['й']="y",['к']="k",['л']="l",['м']="m",['н']="n",['о']="o",['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",['ф']="f",['х']="kh",['ц']="ts",['ч']="ch",['ш']="sh",['щ']="shch",['ъ']="",['ы']="y",['ь']="",['э']="e",['ю']="yu",['я']="ya",['і']="i",['ї']="yi",['є']="ye",['ґ']="g",['ў']="u",['ј']="j",['љ']="lj",['њ']="nj",['ћ']="c",['ђ']="dj",['џ']="dzh"
    };
    public string Name(string value)
    {
        if (Mode == "unicode") return value;
        var result = new StringBuilder();
        foreach (var rune in value.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            var c = rune.IsBmp ? (char)rune.Value : '\0';
            if (Special.TryGetValue(c, out var mapped)) { result.Append(mapped); continue; }
            if (char.IsUpper(c) && Special.TryGetValue(char.ToLowerInvariant(c), out mapped))
            { result.Append(mapped.Length == 0 ? "" : char.ToUpperInvariant(mapped[0]) + mapped[1..]); continue; }
            var decomposed = rune.ToString().Normalize(NormalizationForm.FormD);
            var letters = string.Concat(decomposed.Where(x => x < 128 && char.IsLetterOrDigit(x)));
            if (letters.Length > 0) { result.Append(letters); continue; }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.DashPunctuation or UnicodeCategory.ConnectorPunctuation) result.Append(' ');
            else if (category is UnicodeCategory.OtherPunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation) continue;
            else result.Append(" u" + rune.Value.ToString("X", CultureInfo.InvariantCulture) + " ");
        }
        return TargetPathBuilder.SafeName(Regex.Replace(result.ToString(), @"\s+", " ").Trim());
    }
    public string RelativePath(string relative)
    {
        if (Mode == "unicode") return relative;
        var parts = relative.Replace('\\','/').Split('/');
        for (var i=0;i<parts.Length;i++) parts[i] = i==parts.Length-1
            ? Name(Path.GetFileNameWithoutExtension(parts[i])) + Path.GetExtension(parts[i]).ToLowerInvariant() : Name(parts[i]);
        return Path.Combine(parts);
    }
}
