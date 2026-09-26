using System.Security.Cryptography;

namespace Mp3Organizer;

public interface IReadOnlySource
{
    Stream OpenRead(string path);
}
public sealed class ReadOnlySource : IReadOnlySource
{
    public Stream OpenRead(string path)
    {
        PathSafetyGuard.NoLinks(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
public sealed class ReadOnlyTagFile(string path, IReadOnlySource source) : TagLib.File.IFileAbstraction
{
    public string Name => path;
    public Stream ReadStream => source.OpenRead(path);
    public Stream WriteStream => throw new NotSupportedException("Source files are immutable.");
    public void CloseStream(Stream stream) => stream.Dispose();
}
public sealed class TagLibMetadataReader(IReadOnlySource source)
{
    public AudioMetadata Read(string path)
    {
        var row = new AudioMetadata { FullPath = Path.GetFullPath(path), OriginalSourcePath=Path.GetFullPath(path), FileName = Path.GetFileName(path), Extension = Path.GetExtension(path).ToLowerInvariant() };
        try
        {
            using var input = source.OpenRead(path);
            row = row with { Size = input.Length, LastWriteTimeUtc=System.IO.File.GetLastWriteTimeUtc(path), Sha256 = Convert.ToHexString(SHA256.HashData(input)) };
            using var file = TagLib.File.Create(new ReadOnlyTagFile(path, source));
            var tag = file.Tag;
            return row with
            {
                Artist = string.Join("; ", tag.Performers), TrackArtists = tag.Performers, AlbumArtist = string.Join("; ", tag.AlbumArtists),
                Album = tag.Album ?? "", Title = tag.Title ?? "", Track = tag.Track, Disc = tag.Disc, Year = tag.Year,
                Date = ReadDate(file),
                DurationSeconds = file.Properties.Duration.TotalSeconds, Bitrate = file.Properties.AudioBitrate,
                Codec = file.Properties.Description, CodecFamily = AudioQualityComparer.Classify(file.Properties.Description).Family,
                Lossless = AudioQualityComparer.Classify(file.Properties.Description).Lossless,
                SampleRate = file.Properties.AudioSampleRate, BitsPerSample = file.Properties.BitsPerSample
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        { return row with { Error = e.Message }; }
    }
    private static string ReadDate(TagLib.File file)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
        {
            var date = id3.GetTextAsString(new TagLib.ByteVector("TDRC"));
            if (!string.IsNullOrWhiteSpace(date)) return date;
        }
        if (file.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
        {
            var date = xiph.GetFirstField("DATE");
            if (!string.IsNullOrWhiteSpace(date)) return date;
        }
        return file.Tag.Year == 0 ? "" : file.Tag.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
