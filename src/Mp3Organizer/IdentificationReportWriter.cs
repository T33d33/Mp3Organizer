using System.Globalization;

namespace Mp3Organizer;

public sealed class IdentificationReportWriter
{
    public void Write(string folder, string source, IEnumerable<AudioMetadata> rows)
    {
        PathSafetyGuard.Separate(source,folder);
        var header = "SourcePath,OriginalArtist,OriginalAlbum,OriginalTitle,OriginalAlbumArtist,OriginalTrack,OriginalDisc,OriginalYear,ResolvedArtist,ResolvedAlbumArtist,ResolvedAlbum,ResolvedTitle,ResolvedTrack,ResolvedDisc,ResolvedYear,MetadataSource,AcoustIdScore,AcoustId,MusicBrainzRecordingId,MusicBrainzReleaseId,Status,ReviewReason,Evidence,FingerprintCacheHit,IdentificationCacheHit,ManualOverrideHit,ResolutionStatus,ArtistSource,AlbumArtistSource,AlbumSource,TitleSource,TrackNumberSource,DiscNumberSource,YearSource,RecognitionConfidence,AutoRecognized,YearConfidence,YearEnriched,ReviewReasons,Candidates\r\n";
        string Quote(object? value)
        {
            var text = Convert.ToString(value,CultureInfo.InvariantCulture) ?? "";
            if(text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"","\"\"") + "\"";
        }
        var lines = rows.Select(x =>
        {
            var e = x.Identification ?? new IdentificationEvidence {Original = TagMetadata.From(x)};
            return string.Join(",",new object?[] {x.FullPath,e.Original.Artist,e.Original.Album,e.Original.Title,e.Original.AlbumArtist,e.Original.Track,e.Original.Disc,e.Original.Year,x.Artist,x.AlbumArtist,x.Album,x.Title,x.Track,x.Disc,x.Year,e.Source.ToString(),e.AcoustIdScore,e.AcoustId,e.RecordingId,e.ReleaseId,e.Status,e.ReviewReason,e.Evidence,e.FingerprintCacheHit,e.IdentificationCacheHit,e.ManualOverrideHit,e.Status}.Concat(MetadataFieldMerger.Fields.Select(field=>(object?)e.FieldSources.GetValueOrDefault(field,"Unknown"))).Concat(new object?[]{e.RecognitionConfidence,e.AutoRecognized,e.YearConfidence,e.YearEnriched,string.Join(";",e.ReviewReasons),JsonFormat.Serialize(e.Candidates)}).Select(Quote));
        });
        new M3u8Writer().WriteNew(Path.Combine(folder,"identification.csv"),header+string.Join("\r\n",lines)+"\r\n",source);
    }
}
