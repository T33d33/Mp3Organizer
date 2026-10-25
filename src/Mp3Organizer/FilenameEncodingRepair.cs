using System.Text;
namespace Mp3Organizer;

public static class FilenameEncodingRepair
{
    public static AudioMetadata Apply(AudioMetadata file)
    {
        var artist=Field(file,"Artist",file.Artist)??file.Artist;
        return file with {Artist=artist,TrackArtists=artist==file.Artist?file.TrackArtists:[artist],
            AlbumArtist=Field(file,"AlbumArtist",file.AlbumArtist)??file.AlbumArtist,
            Album=Field(file,"Album",file.Album)??file.Album,
            Title=Title(file)??Field(file,"Title",file.Title)??file.Title};
    }
    public static string? Field(AudioMetadata file,string field,string value)
    {
        if(file.Identification?.FieldSources.GetValueOrDefault(field) is "FileManualOverride" or "FolderManualOverride" or "AcoustIdMusicBrainz" or "MusicBrainz")return null;
        if(string.IsNullOrWhiteSpace(value))return null;
        // Require the complete repaired value in the actual filename or immediate folder.
        // Never guess diacritics or replace tags merely because folder text differs.
        var evidence=Path.GetFileNameWithoutExtension(file.FileName)+" / "+Path.GetFileName(Path.GetDirectoryName(file.FullPath));
        var matches=Repairs(value).Where(x=>x!=value&&System.Text.RegularExpressions.Regex.IsMatch(evidence,
            @"(?<![\p{L}\p{N}])"+System.Text.RegularExpressions.Regex.Escape(x)+@"(?![\p{L}\p{N}])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant)).Distinct().ToArray();
        return matches.Length==1?matches[0]:null;
    }
    private static IEnumerable<string> Repairs(string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach(var wrong in new[]{28591,1252})
        foreach(var correct in new[]{1250,28592,65001})
        {
            string repaired;
            try { repaired=Encoding.GetEncoding(correct,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetString(
                Encoding.GetEncoding(wrong,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetBytes(value)); }
            catch(EncoderFallbackException){continue;}
            catch(DecoderFallbackException){continue;}
            yield return repaired;
        }
    }
    public static string? Title(AudioMetadata file)
    {
        var source=file.Identification?.FieldSources.GetValueOrDefault("Title");
        if(source is "FileManualOverride" or "FolderManualOverride" or "AcoustIdMusicBrainz" or "MusicBrainz")return null;
        var expected=FilenameTrackParser.Parse(file.FileName)?.Title;
        if(string.IsNullOrWhiteSpace(expected)||MetadataQualityEvaluator.Invalid(expected)||MetadataNormalizer.Key(expected)==MetadataNormalizer.Key(file.Title))return null;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach(var wrong in new[]{28591,1252})
        foreach(var correct in new[]{1250,65001})
        {
            try
            {
                var bytes=Encoding.GetEncoding(wrong,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetBytes(file.Title);
                var repaired=Encoding.GetEncoding(correct,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetString(bytes);
                if(MetadataNormalizer.Key(repaired)==MetadataNormalizer.Key(expected))return expected;
            }
            catch(EncoderFallbackException){}
            catch(DecoderFallbackException){}
        }
        return null;
    }
}
