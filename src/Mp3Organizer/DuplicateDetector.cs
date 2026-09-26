namespace Mp3Organizer;

public interface IDuplicateEquivalenceVerifier
{
    bool Equivalent(AudioMetadata left, AudioMetadata right);
    string Evidence { get; }
}
public sealed class Sha256EquivalenceVerifier : IDuplicateEquivalenceVerifier
{
    public string Evidence => "Identical SHA-256 and size";
    public bool Equivalent(AudioMetadata left, AudioMetadata right) => left.Sha256.Length == 64 && left.Sha256 == right.Sha256 && left.Size == right.Size;
}
public sealed class DuplicateDetector(IDuplicateEquivalenceVerifier verifier)
{
    public List<List<AudioMetadata>> CandidateGroups(IEnumerable<AudioMetadata> rows)
    {
        var result = new List<List<AudioMetadata>>();
        foreach (var key in rows.Where(x => x.Error == "" && MetadataNormalizer.Key(x.Artist) != "" && MetadataNormalizer.Key(x.Title) != "" && double.IsFinite(x.DurationSeconds) && x.DurationSeconds > 0)
            .GroupBy(x => (MetadataNormalizer.Key(x.Artist), MetadataNormalizer.Key(x.Title))).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
        {
            var group = new List<AudioMetadata>();
            foreach (var row in key.OrderBy(x => x.DurationSeconds).ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count > 0 && row.DurationSeconds - group[0].DurationSeconds > 3)
                { if (group.Count > 1) result.Add(group); group = new(); }
                group.Add(row);
            }
            if (group.Count > 1) result.Add(group);
        }
        return result;
    }
    public (List<AudioMetadata> Selected, List<DuplicateDecision> Decisions) Select(IReadOnlyList<AudioMetadata> rows)
    {
        var selected = new List<AudioMetadata>();
        var decisions = new List<DuplicateDecision>();
        var hashes = new Dictionary<(string Hash, long Size), AudioMetadata>();
        foreach (var row in rows.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var same = verifier is Sha256EquivalenceVerifier
                ? (row.Sha256.Length == 64 && hashes.TryGetValue((row.Sha256, row.Size), out var match) ? match : null)
                : selected.FirstOrDefault(x => verifier.Equivalent(x, row));
            if (same == null) { selected.Add(row); if (row.Sha256.Length == 64) hashes.TryAdd((row.Sha256, row.Size), row); }
            else decisions.Add(new("SHA256-" + row.Sha256, row.FullPath, "SkipEquivalent", verifier.Evidence + "; selected " + same.FullPath));
        }
        var index = 0;
        var selectedPaths = selected.Select(x=>x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.Where(x => x.Identification?.ConfidentRecording == true && x.Identification.RecordingId != "").GroupBy(x => x.Identification!.RecordingId).Where(x => x.Count() > 1))
        {
            var candidates = group.Where(x=>selectedPaths.Contains(x.FullPath)).ToList();
            foreach (var row in candidates)
            {
                var preferred = candidates.FirstOrDefault(other => other != row && Math.Abs(other.DurationSeconds-row.DurationSeconds) <= 3 && new AudioQualityComparer().Compare(other,row) > 0);
                decisions.Add(new("Recording-" + group.Key,row.FullPath,"KeepBoth/Review","Confident MusicBrainz recording identity. Different masters/edits are not proven equivalent." + (preferred == null ? " Quality inconclusive or this copy is preferred." : " Quality preference for review: " + preferred.FullPath)));
            }
        }
        foreach (var group in CandidateGroups(rows))
        {
            index++;
            foreach (var row in group.Where(x => selectedPaths.Contains(x.FullPath)))
                decisions.Add(new("Candidate-" + index, row.FullPath, "Keep", "Metadata/duration match only; non-identical recordings require review. Quality never establishes equivalence."));
        }
        return (selected, decisions);
    }
}
