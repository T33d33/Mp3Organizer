namespace Mp3Organizer;

public sealed class MetadataResolver(IAudioFingerprintService fingerprints, IAcoustIdClient acoustId, IMusicBrainzClient musicBrainz, bool online, bool computeFingerprints, Action<string>? progress=null) : IMetadataResolver
{
    private readonly MetadataQualityEvaluator evaluator = new();
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file, IReadOnlyList<AudioMetadata> neighbors, bool identifyAll = false, CancellationToken ct = default)
    {
        progress?.Invoke("Resolution: evaluating metadata");
        var quality = evaluator.Evaluate(file, neighbors);
        var evidence = new IdentificationEvidence { Original = file.Identification?.Original ?? TagMetadata.From(file), FieldSources=MetadataFieldMerger.Merge(file,file).Identification!.FieldSources, Source = MetadataSource.Tags, ReviewReason = string.Join("; ", quality) };
        if (file.Error != "") return file with { Identification = evidence with { Status = "Review", ReviewReason = "Metadata read failed: " + file.Error } };
        if (quality.Count == 0 && !identifyAll)
        {
            var filled=MetadataFieldMerger.Merge(file,new FolderMetadataFallback().Resolve(file));
            return filled with{Identification=evidence with{FieldSources=filled.Identification!.FieldSources}};
        }
        try
        {
            progress?.Invoke("Fingerprinting: local computation or cache");
            var fingerprint = await fingerprints.FingerprintAsync(file, computeFingerprints, ct);
            if (fingerprint == null) return Fallback("No cached fingerprint; online identification is disabled.");
            evidence=evidence with{FingerprintCacheHit=fingerprint.CacheHit};
            progress?.Invoke("AcoustID: lookup or cache");
            var lookup = await acoustId.LookupAsync(fingerprint, online, ct);
            evidence=evidence with{IdentificationCacheHit=lookup.CacheHit};
            if (lookup.Error != "") return Fallback(lookup.Error);
            var candidates = lookup.Candidates.GroupBy(x => x.RecordingId).Select(g => g.OrderByDescending(x => x.Score).First()).OrderByDescending(x => x.Score).ToList();
            if (candidates.Count == 0) return Fallback("No AcoustID recording match.");
            var best = candidates[0];
            evidence = evidence with { AcoustIdScore = best.Score };
            if (best.Score < .90) return Fallback("AcoustID score below 0.90; no reliable recording.");
            if (candidates.Skip(1).Any(x => x.Score >= .85 && best.Score - x.Score < .05))
            {
                var choices=new List<RecognitionCandidate>();
                foreach(var candidate in candidates.Take(3))
                {
                    var detail=await musicBrainz.LookupAsync(candidate.RecordingId,online,ct);
                    if(detail.Recording!=null)
                        foreach(var edition in detail.Releases.Take(3).Cast<ReleaseInfo?>().DefaultIfEmpty(null))choices.Add(RecognitionScorer.Score(file,neighbors,candidate,detail.Recording,edition));
                }
                evidence=evidence with{Candidates=choices.OrderByDescending(x=>x.Confidence).Take(9).ToList()};
                return Fallback("Ambiguous AcoustID recordings; no ID selected.");
            }
            evidence = evidence with { AcoustId = best.AcoustId };
            progress?.Invoke("MusicBrainz: recording and release lookup or cache");
            var mb = await musicBrainz.LookupAsync(best.RecordingId, online, ct);
            evidence=evidence with{IdentificationCacheHit=evidence.IdentificationCacheHit||mb.CacheHit};
            var recording = mb.Recording;
            if (recording == null) return Fallback("MusicBrainz recording unavailable: " + mb.Error);
            if (recording.Id != best.RecordingId || MetadataQualityEvaluator.Invalid(recording.Artist) || MetadataQualityEvaluator.Invalid(recording.Title)) return Fallback("Incomplete or mismatched MusicBrainz recording.");
            if (recording.Duration > 0 && Math.Abs(recording.Duration - fingerprint.Duration) > 3) return Fallback("MusicBrainz recording duration conflicts with audio; review edit/version.");
            evidence=evidence with{Candidates=mb.Releases.Take(9).Cast<ReleaseInfo?>().DefaultIfEmpty(null).Select(x=>RecognitionScorer.Score(file,neighbors,best,recording,x)).OrderByDescending(x=>x.Confidence).ToList()};
            var conflicts = new List<string>();
            var existingVariants=RecognitionScorer.Variants(file.Title);
            var candidateVariants=RecognitionScorer.Variants(recording.Title+" "+recording.Disambiguation);
            if(!existingVariants.SequenceEqual(candidateVariants))conflicts.Add("Recording version conflicts (live/acoustic/edit/remaster)");
            if (!MetadataQualityEvaluator.Invalid(file.Artist) && MetadataNormalizer.Key(file.Artist) != MetadataNormalizer.Key(recording.Artist)) conflicts.Add("Good embedded artist differs from recording");
            if (!MetadataQualityEvaluator.Invalid(file.Title) && !MetadataQualityEvaluator.FilenameTitle(file) && MetadataNormalizer.Key(file.Title) != MetadataNormalizer.Key(recording.Title)) conflicts.Add("Good embedded title differs from recording (possible edit/version)");
            if (conflicts.Count > 0) return file with { Identification = evidence with { ReviewReasons=RecognitionScorer.Reasons(string.Join("; ",conflicts)).Concat(candidateVariants.Contains("LIVE")?new[]{"PossibleLiveVersion"}:Array.Empty<string>()).ToArray(),LookupStatus="Ambiguous",Status = "Review", ReviewReason = string.Join("; ",quality.Concat(conflicts)), Evidence = "Embedded metadata retained; candidate recording " + recording.Id } };
            evidence = evidence with { LookupStatus="Success", RecordingId = recording.Id, ConfidentRecording = true, Evidence = $"AcoustID score {best.Score:F3}; unique recording with >=0.05 margin; {(recording.Duration > 0 ? "duration consistent" : "MusicBrainz duration unavailable")}. {recording.Disambiguation}" };
            var release = ChooseRelease(file, neighbors, mb.Releases);
            if(release!=null&&release.OriginalYear>0)release=release with{Year=release.OriginalYear};
            var scored=RecognitionScorer.Score(file,neighbors,best,recording,release);
            evidence=evidence with{RecognitionConfidence=scored.Confidence};
            // Explicit identification is evidence gathering; complete plausible embedded metadata wins.
            if (quality.Count == 0) return file with { Identification = evidence with { ReleaseId = release?.Id ?? "", Evidence = evidence.Evidence + "; good tags retained" } };
            progress?.Invoke("Resolution: merging individual fields");
            var merged=MetadataFieldMerger.Merge(file,new FolderMetadataFallback().Resolve(file),recording,release);
            var effective = merged with
            {
                Identification = evidence with { FieldSources=merged.Identification!.FieldSources, LookupStatus=release==null?mb.Error!=""?"TransientFailure":"Ambiguous":"Success", Source = MetadataSource.AcoustIdMusicBrainz, ReleaseId = release?.Id ?? "", Status = release == null ? "Review" : "Resolved",
                    ReviewReason = string.Join("; ", quality) + (release == null ? "; Recording identified; album release unresolved. " + mb.Error : ""), Evidence = evidence.Evidence + (release == null ? "" : "; release agrees with album context") }
            };
            var automatic=release!=null&&best.Score>=.95&&recording.Duration>0&&scored.Confidence>=.78;
            var finalEvidence=effective.Identification!;
            effective=effective with{Identification=finalEvidence with{AutoRecognized=automatic,Status=automatic?"Resolved":"Review",ReviewReasons=automatic?[]:new[]{release==null?"AmbiguousAlbum":"WeakFingerprint"},YearSource=file.Year==0&&release?.OriginalYear>0?"MusicBrainz":"",YearConfidence=file.Year==0&&release?.OriginalYear>0?.95:0,YearEnriched=file.Year==0&&release?.OriginalYear>0,AlbumGroupId=release?.GroupId??"",ReviewReason=automatic?"":finalEvidence.ReviewReason}};
            return effective;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { return Fallback(e is System.ComponentModel.Win32Exception ? "fpcalc unavailable; configure CHROMAPRINT_FPCALC." : "Identification unavailable: " + e.Message); }

        AudioMetadata Fallback(string reason)
        {
            var status=reason.Contains("Offline",StringComparison.OrdinalIgnoreCase)||reason.Contains("disabled")?"Offline":reason.Contains("Ambiguous",StringComparison.OrdinalIgnoreCase)?"Ambiguous":reason.Contains("HTTP")||reason.Contains("unavailable",StringComparison.OrdinalIgnoreCase)||reason.Contains("error",StringComparison.OrdinalIgnoreCase)||reason.Contains("failed",StringComparison.OrdinalIgnoreCase)?"TransientFailure":"NoMatch";
            evidence=evidence with{LookupStatus=status,ReviewReasons=RecognitionScorer.Reasons(reason)};
            if (quality.Count == 0) return file with { Identification = evidence with { Status = "Review", ReviewReason = reason, Evidence = "Good embedded metadata retained" } };
            var fallback = MetadataFieldMerger.Merge(file,new FolderMetadataFallback().Resolve(file));
            var changed = TagMetadata.From(fallback) != TagMetadata.From(file);
            return fallback with { Identification = evidence with { FieldSources=fallback.Identification!.FieldSources, Source = changed ? MetadataSource.FolderFallback : MetadataSource.Tags, Status = "Review", ReviewReason = string.Join("; ", quality) + "; " + reason, Evidence = changed ? "Low-confidence folder/file fallback; original tags preserved" : "Embedded tags retained; identification incomplete" } };
        }
    }
    public static ReleaseInfo? ChooseRelease(AudioMetadata file, IReadOnlyList<AudioMetadata> neighbors, IReadOnlyList<ReleaseInfo> releases)
    {
        file=new FolderMetadataFallback().Resolve(file);
        var otherAlbums = neighbors.Where(x => x.FullPath != file.FullPath && !MetadataQualityEvaluator.Invalid(x.Album)).GroupBy(x => MetadataNormalizer.Key(x.Album)).OrderByDescending(x => x.Count()).FirstOrDefault();
        var neighborAlbum = otherAlbums != null && otherAlbums.Count() >= 2 && otherAlbums.Count() >= neighbors.Count * .6 ? otherAlbums.Key : "";
        var years = neighbors.Where(x=>x.FullPath!=file.FullPath && x.Year>0 && MetadataNormalizer.Key(x.Album)==neighborAlbum).GroupBy(x=>x.Year).OrderByDescending(x=>x.Count()).FirstOrDefault();
        var expectedYear = file.Year != 0 ? file.Year : years != null && years.Count() >= 2 && years.Count() >= (otherAlbums?.Count() ?? neighbors.Count) * .8 ? years.Key : 0;
        var editions=releases.GroupBy(x=>x.GroupId!=""&&x.OriginalYear>0?$"{x.GroupId}|{x.Track}|{x.Disc}|{x.OriginalYear}|{MetadataNormalizer.Key(x.Album)}|{MetadataNormalizer.Key(x.AlbumArtist)}":x.Id).Select(g=>g.OrderBy(x=>x.Year).First());
        var ranked = editions.Where(x => x.Status == "Official" && !MetadataQualityEvaluator.Invalid(x.Album)).Select(x =>
        {
            var tagMatch = !MetadataQualityEvaluator.Invalid(file.Album) && MetadataNormalizer.Key(x.Album) == MetadataNormalizer.Key(file.Album);
            var neighborMatch = neighborAlbum != "" && MetadataNormalizer.Key(x.Album) == neighborAlbum;
            var score = tagMatch ? 5 : neighborMatch ? 5 : 0;
            if (!tagMatch && !neighborMatch) return (Release: x, Score: -100);
            if (expectedYear != 0 && x.Year != 0) score += expectedYear == x.Year ? 3 : -4;
            if (file.Track != 0 && x.Track != 0) score += file.Track == x.Track ? 2 : -3;
            if (file.Disc != 0 && x.Disc != 0) score += file.Disc == x.Disc ? 1 : -3;
            if (!MetadataQualityEvaluator.Invalid(file.AlbumArtist) && MetadataNormalizer.Key(file.AlbumArtist) == MetadataNormalizer.Key(x.AlbumArtist)) score++;
            return (Release: x, Score: score);
        }).OrderByDescending(x => x.Score).ToList();
        if (ranked.Count == 0 || ranked[0].Score < 5 || ranked.Count > 1 && ranked[0].Score - ranked[1].Score < 2) return null;
        return ranked[0].Release;
    }
}
