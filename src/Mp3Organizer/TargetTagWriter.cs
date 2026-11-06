namespace Mp3Organizer;

// Decide once during planning from the resolved model; apply executes this exact patch.
public static class TargetTagWriter
{
    public static List<TagChange> Plan(AudioMetadata original, AudioMetadata resolved)
    {
        var changes=new List<TagChange>();
        if(!resolved.Extension.Equals(".mp3",StringComparison.OrdinalIgnoreCase))return changes;
        void Add(string field,string before,string after,bool reliable)
        {
            var provenance=resolved.Identification?.FieldSources.GetValueOrDefault(field) ?? resolved.Identification?.Source.ToString() ?? "Tags";
            if(reliable && provenance!="Unresolved" && before!=after)changes.Add(new(field,before,after,provenance));
        }
        Add("Title",original.Title,resolved.Title,resolved.Title.Length>0 && (resolved.ExplicitlyConfirmedTitle==resolved.Title || !MetadataQualityEvaluator.Invalid(resolved.Title)));
        Add("Artist",original.Artist,resolved.Artist,!MetadataQualityEvaluator.Invalid(resolved.Artist));
        Add("Album",original.Album,resolved.Album,!MetadataQualityEvaluator.InvalidAlbum(resolved.Album));
        Add("AlbumArtist",original.AlbumArtist,resolved.AlbumArtist,!MetadataQualityEvaluator.Invalid(resolved.AlbumArtist));
        Add("Year",original.Year.ToString(),resolved.Year.ToString(),resolved.Year is >=1000 and <=9999);
        Add("TrackNumber",original.Track.ToString(),resolved.Track.ToString(),resolved.Track>0);
        Add("DiscNumber",original.Disc.ToString(),resolved.Disc.ToString(),resolved.Disc>0);
        return changes;
    }
    public static AudioMetadata WriteTemporary(string path, string sourceRoot, AudioMetadata resolved, IReadOnlyList<TagChange> changes)
    {
        PathSafetyGuard.Writable(path,sourceRoot);
        if(changes.Count>0)
        {
            var payload=Mp3AudioPayload.Hash(path);
            using(var file=TagLib.File.Create(path))
            {
                foreach(var c in changes)switch(c.Field)
                {
                    case "Title":file.Tag.Title=c.After;break;
                    case "Artist":file.Tag.Performers=resolved.TrackArtists.Length>0&&string.Join("; ",resolved.TrackArtists)==c.After?resolved.TrackArtists:[c.After];break;
                    case "Album":file.Tag.Album=c.After;break;
                    case "AlbumArtist":file.Tag.AlbumArtists=[c.After];break;
                    case "Year":file.Tag.Year=uint.Parse(c.After);break;
                    case "TrackNumber":file.Tag.Track=uint.Parse(c.After);break;
                    case "DiscNumber":file.Tag.Disc=uint.Parse(c.After);break;
                    default:throw new IOException("Unknown planned tag field: "+c.Field);
                }
                file.Save();
            }
            if(Mp3AudioPayload.Hash(path)!=payload)throw new IOException("Target tag write altered audio payload; refusing publication.");
        }
        var actual=new TagLibMetadataReader(new ReadOnlySource()).Read(path);
        if(actual.Error!="")throw new IOException("Cannot verify target tags: "+actual.Error);
        foreach(var c in changes)
        {
            var value=c.Field switch {"Title"=>actual.Title,"Artist"=>actual.Artist,"Album"=>actual.Album,"AlbumArtist"=>actual.AlbumArtist,"Year"=>actual.Year.ToString(),"TrackNumber"=>actual.Track.ToString(),"DiscNumber"=>actual.Disc.ToString(),_=>""};
            if(value!=c.After)throw new IOException("Target tag read-back mismatch: "+c.Field);
        }
        return resolved with {SourceSha256=resolved.SourceSha256==""?resolved.Sha256:resolved.SourceSha256,SourceSize=resolved.SourceSha256==""?resolved.Size:resolved.SourceSize,Sha256=actual.Sha256,Size=actual.Size};
    }
    public static void Describe(CopyPlan plan,Action<string> output)
    {
        foreach(var op in plan.Operations)
        {
            output("SOURCE: "+op.Metadata.FullPath);output("TARGET: "+Path.Combine(plan.Target,op.RelativePath));
            if(op.OriginalRelativePath!=""&&op.OriginalRelativePath!=op.RelativePath)output("PATH TRANSFORMATION: "+op.OriginalRelativePath+" -> "+op.RelativePath);
            foreach(var c in op.TagChanges)output($"TAG {c.Field}: \"{c.Before}\" -> \"{c.After}\" [{c.Provenance}]");
        }
    }
}
