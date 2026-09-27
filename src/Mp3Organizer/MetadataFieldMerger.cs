namespace Mp3Organizer;

public static class MetadataFieldMerger
{
    public static readonly string[] Fields=["Artist","AlbumArtist","Album","Title","TrackNumber","DiscNumber","Year"];
    public static AudioMetadata Merge(AudioMetadata tags,AudioMetadata fallback,RecordingInfo? recording=null,ReleaseInfo? release=null)
    {
        var sources=new Dictionary<string,string>();
        string Text(string name,string original,string? online,string lower)
        {
            bool Valid(string? value)=>!string.IsNullOrWhiteSpace(value)&&!MetadataQualityEvaluator.Invalid(value)&&(name!="Title"||!MetadataQualityEvaluator.FilenameTitle(tags with{Title=value}));
            if(Valid(original)){sources[name]="Tags";return original;}
            if(Valid(online)){sources[name]="AcoustIdMusicBrainz";return online!;}
            if(Valid(lower)){sources[name]="FilenameFallback";return lower;}
            sources[name]="Unresolved";return "";
        }
        uint Number(string name,uint original,uint? online,uint lower)
        {
            if(original>0){sources[name]="Tags";return original;}
            if(online>0){sources[name]="AcoustIdMusicBrainz";return online.Value;}
            sources[name]=lower>0?"FilenameFallback":"Unresolved";return lower;
        }
        var artist=Text("Artist",tags.Artist,recording?.Artist,fallback.Artist);
        var year=Number("Year",tags.Year,release?.Year,fallback.Year);
        return tags with{Artist=artist,TrackArtists=artist==tags.Artist?tags.TrackArtists:artist==""?[]:[artist],
            AlbumArtist=Text("AlbumArtist",tags.AlbumArtist,release?.AlbumArtist,fallback.AlbumArtist),Album=Text("Album",tags.Album,release?.Album,fallback.Album),
            Title=Text("Title",tags.Title,recording?.Title,fallback.Title),Track=Number("TrackNumber",tags.Track,release?.Track,fallback.Track),Disc=Number("DiscNumber",tags.Disc,release?.Disc,fallback.Disc),Year=year,
            Date=year==tags.Year?tags.Date:year==0?"":year.ToString(System.Globalization.CultureInfo.InvariantCulture),Identification=(tags.Identification??new(){Original=TagMetadata.From(tags)}) with{FieldSources=sources}};
    }
    public static Dictionary<string,string> ManualSources(IdentificationEvidence evidence,ManualOverride? folder,ManualOverride? file)
    {
        var result=new Dictionary<string,string>(evidence.FieldSources);
        foreach(var pair in new[]{(Value:folder,Source:"FolderManualOverride"),(Value:file,Source:"FileManualOverride")})
        {
            if(pair.Value==null)continue;var v=pair.Value;
            object?[] values=[v.Artist,v.AlbumArtist,v.Album,v.Title,v.TrackNumber,v.DiscNumber,v.Year];
            for(var i=0;i<Fields.Length;i++)if(values[i]!=null)result[Fields[i]]=Fields[i]=="Year"&&v.YearTrackOnly?"FileManualTrackYear":pair.Source;
        }
        return result;
    }
}
