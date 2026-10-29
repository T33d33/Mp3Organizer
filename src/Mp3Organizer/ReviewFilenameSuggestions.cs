namespace Mp3Organizer;
public static class ReviewFilenameSuggestions
{
    public static AudioMetadata Apply(AudioMetadata row)
    {
        var original=row;
        // Old Enter/F decisions may have saved the entire numbered filename as Title.
        // Clean only an exact filename match with a corroborating track number.
        var stem=Path.GetFileNameWithoutExtension(row.FileName);
        if(!MetadataQualityEvaluator.Invalid(row.Artist)&&System.Text.RegularExpressions.Regex.IsMatch(stem,@"^\d{1,3}[ ._–—-]+")
            &&MetadataNormalizer.Key(row.Title)==MetadataNormalizer.Key(stem)
            &&FilenameTrackParser.Parse(row.FileName) is { } numbered
            &&!MetadataQualityEvaluator.Invalid(numbered.Title)
            &&(row.Track==0||row.Track==numbered.Number))
            row=row with{Title=numbered.Title,Track=row.Track==0?numbered.Number:row.Track};
        bool Manual(string field)=>row.Identification?.FieldSources.GetValueOrDefault(field) is "FileManualOverride" or "FolderManualOverride";
        if(!MetadataQualityEvaluator.Invalid(row.Artist)||Manual("Artist")||Manual("Title"))return row;
        var suggestion=new FilenameMetadataFallback().Resolve(original);
        if(MetadataQualityEvaluator.Invalid(suggestion.Artist)||MetadataQualityEvaluator.Invalid(suggestion.Title))return row;
        return row with{Artist=suggestion.Artist,TrackArtists=suggestion.TrackArtists,Title=suggestion.Title};
    }
}
