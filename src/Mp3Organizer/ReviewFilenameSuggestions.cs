namespace Mp3Organizer;
public static class ReviewFilenameSuggestions
{
    public static AudioMetadata Apply(AudioMetadata row)
    {
        bool Manual(string field)=>row.Identification?.FieldSources.GetValueOrDefault(field) is "FileManualOverride" or "FolderManualOverride";
        if(!MetadataQualityEvaluator.Invalid(row.Artist)||Manual("Artist")||Manual("Title"))return row;
        var suggestion=new FilenameMetadataFallback().Resolve(row);
        if(MetadataQualityEvaluator.Invalid(suggestion.Artist)||MetadataQualityEvaluator.Invalid(suggestion.Title))return row;
        return row with{Artist=suggestion.Artist,TrackArtists=suggestion.TrackArtists,Title=suggestion.Title};
    }
}
