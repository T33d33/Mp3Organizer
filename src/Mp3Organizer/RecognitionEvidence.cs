using System.Text.RegularExpressions;

namespace Mp3Organizer;

public sealed record RecognitionCandidate(TagMetadata Metadata,string RecordingId,string ReleaseId,double Confidence,Dictionary<string,double> Evidence,string Detail="");
public static class RecognitionScorer
{
    public static RecognitionCandidate Score(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,AcousticCandidate acoustic,RecordingInfo recording,ReleaseInfo? release)
    {
        var fallback=new FilenameMetadataFallback().Resolve(file);
        double Same(string a,string b)=>!MetadataQualityEvaluator.Invalid(a)&&MetadataNormalizer.Key(a)==MetadataNormalizer.Key(b)?1:0;
        var folder=FilenameMetadataFallback.Clean(Path.GetFileName(Path.GetDirectoryName(file.FullPath))??"");
        var evidence=new Dictionary<string,double>
        {
            ["Fingerprint"]=Math.Clamp(acoustic.Score,0,1)*.55,
            ["Duration"]=recording.Duration>0&&Math.Abs(recording.Duration-file.DurationSeconds)<=3?.15:0,
            ["Artist"]=Same(fallback.Artist,recording.Artist)*.08,
            ["Title"]=Same(fallback.Title,recording.Title)*.08,
            ["Album"]=release==null?0:Same(fallback.Album,release.Album)*.06,
            ["Track"]=release!=null&&fallback.Track>0&&fallback.Track==release.Track?.03:0,
            ["Folder"]=release!=null&&!MetadataQualityEvaluator.Invalid(release.Album)&&MetadataNormalizer.Key(folder).Contains(MetadataNormalizer.Key(release.Album),StringComparison.Ordinal)?.025:0,
            ["Neighbors"]=0
        };
        var year=release?.OriginalYear??0;
        return new(new(recording.Artist,release?.AlbumArtist??"",release?.Album??"",recording.Title,release?.Track??0,release?.Disc??0,year),recording.Id,release?.Id??"",Math.Round(evidence.Values.Sum(),4),evidence,recording.Disambiguation);
    }
    public static string[] Variants(string text)=>Regex.Matches(text,@"\b(live|acoustic|remix|remaster(?:ed)?|radio\s+edit|instrumental)\b",RegexOptions.IgnoreCase).Select(x=>MetadataNormalizer.Key(x.Value)).Distinct().Order().ToArray();
    public static string[] Reasons(string reason)
    {
        var result=new List<string>();
        if(reason.Contains("Ambiguous AcoustID",StringComparison.OrdinalIgnoreCase))result.AddRange(["AmbiguousRecording","MultipleStrongMatches"]);
        if(reason.Contains("below 0.90",StringComparison.OrdinalIgnoreCase))result.Add("WeakFingerprint");
        if(reason.Contains("No AcoustID",StringComparison.OrdinalIgnoreCase)||reason.Contains("No cached fingerprint",StringComparison.OrdinalIgnoreCase))result.Add("NoFingerprintMatch");
        if(reason.Contains("release unresolved",StringComparison.OrdinalIgnoreCase))result.Add("AmbiguousAlbum");
        if(reason.Contains("differs",StringComparison.OrdinalIgnoreCase)||reason.Contains("conflict",StringComparison.OrdinalIgnoreCase))result.Add("ConflictingMetadata");
        if(reason.Contains("duration",StringComparison.OrdinalIgnoreCase)&&reason.Contains("conflict",StringComparison.OrdinalIgnoreCase))result.Add("AmbiguousRecording");
        return result.Distinct().ToArray();
    }
}

