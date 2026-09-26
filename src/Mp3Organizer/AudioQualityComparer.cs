namespace Mp3Organizer;

// This policy is only meaningful AFTER a verifier establishes equivalence.
// Version 1 uses SHA-256, so different encodings are never eliminated by quality.
public sealed class AudioQualityComparer
{
    public int? Compare(AudioMetadata left, AudioMetadata right)
    {
        if (left.Lossless == null || right.Lossless == null) return null;
        if (left.Lossless != right.Lossless) return left.Lossless == true ? 1 : -1;
        if (left.Lossless == true && left.SampleRate > 0 && right.SampleRate > 0 && left.BitsPerSample > 0 && right.BitsPerSample > 0)
        {
            if (left.SampleRate >= right.SampleRate && left.BitsPerSample >= right.BitsPerSample && (left.SampleRate > right.SampleRate || left.BitsPerSample > right.BitsPerSample)) return 1;
            if (left.SampleRate <= right.SampleRate && left.BitsPerSample <= right.BitsPerSample && (left.SampleRate < right.SampleRate || left.BitsPerSample < right.BitsPerSample)) return -1;
        }
        if (left.CodecFamily == "" || left.CodecFamily != right.CodecFamily || left.SampleRate != right.SampleRate || left.BitsPerSample != right.BitsPerSample) return null;
        // Compressed lossless bitrate measures compression efficiency, not fidelity.
        if (left.Lossless == true) return 0;
        return left.Bitrate > 0 && right.Bitrate > 0 ? left.Bitrate.CompareTo(right.Bitrate) : null;
    }
    public static (string Family, bool? Lossless) Classify(string description)
    {
        var value = description.ToUpperInvariant();
        if (value.Contains("FLAC")) return ("FLAC", true);
        if (value.Contains("APPLE LOSSLESS") || value.Contains("ALAC")) return ("ALAC", true);
        if (value.StartsWith("PCM ") || value == "PCM") return ("PCM", true);
        if (value.Contains("LAYER 3") || value.Contains("LAYER III")) return ("MP3", false);
        if (value.Contains("AAC")) return ("AAC", false);
        if (value.Contains("VORBIS")) return ("VORBIS", false);
        return ("", null);
    }
}
