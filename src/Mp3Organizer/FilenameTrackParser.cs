using System.Text.RegularExpressions;

namespace Mp3Organizer;

public sealed record FilenameTrack(uint Number,string Title);
public static class FilenameTrackParser
{
    public static FilenameTrack? Parse(string filename)
    {
        var stem=Path.GetFileNameWithoutExtension(filename);
        // Number-only ripper labels carry a track hint, never a meaningful song title.
        var label=Regex.Match(stem,@"^(?:audio[\s._-]*)?track[\s._-]*(?<number>\d{1,3})$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(label.Success&&uint.TryParse(label.Groups["number"].Value,out var labelNumber)&&labelNumber>0)
            return new(labelNumber,"");
        // A track token must start the name or follow an explicit spaced dash.
        var match=Regex.Match(stem,@"^(?:(?!\d).*?\s[-–—]\s+)?(?<first>\d{1,3})(?:\s+(?<second>\d{1,3})\.)?[ ._–—-]+(?<title>\S.*)$");
        if(!match.Success||!uint.TryParse(match.Groups["first"].Value,out var number)||number==0)return null;
        if(match.Groups["second"].Success&&uint.Parse(match.Groups["second"].Value)!=number)return null;
        var title=match.Groups["title"].Value.Trim();
        if(MetadataQualityEvaluator.Invalid(title))return null;
        return new(number,title);
    }
}
