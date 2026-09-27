namespace Mp3Organizer;

public sealed class CodexPendingException(AudioMetadata metadata,CodexServiceException failure):Exception(failure.Message,failure)
{public AudioMetadata Metadata {get;}=metadata;public CodexServiceException Failure {get;}=failure;}
public sealed class CodexRunState
{
    public CodexServiceException? Unavailable {get;set;}
    public static AudioMetadata Fallback(AudioMetadata row,CodexServiceException error)=>row with{Identification=(row.Identification??new()) with{
        RecognitionMethod="DeterministicWithoutCodexFallback",CodexUnavailable=true,CodexFailureKind=error.Kind.ToString(),Status="Review",
        ReviewReason=(row.Identification?.ReviewReason??"")+"; Codex unavailable: "+error.Message,
        ReviewReasons=(row.Identification?.ReviewReasons??[]).Append("CodexUnavailable").Distinct().ToArray()}};
}
public sealed class CodexMetadataResolver(IMetadataResolver inner,ICodexClient client,CodexRunState state,Action<string>? progress=null,LlmObservability? observer=null):IMetadataResolver,IInventoryAwareMetadataResolver
{
    public void SetInventory(IReadOnlyList<AudioMetadata> inventory){if(inner is IInventoryAwareMetadataResolver aware)aware.SetInventory(inventory);}
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        var row=await inner.ResolveAsync(file,neighbors,identifyAll,ct);var evidence=row.Identification??new(){Original=TagMetadata.From(file)};
        if(evidence.Status!="Review"&&!ManualMetadataResolver.NeedsInput(row)){observer?.Deterministic(file.FullPath);return row;}
        if(state.Unavailable!=null){observer?.Deterministic(file.FullPath,"disabled for remainder of run -> NeedsReview");return CodexRunState.Fallback(row,state.Unavailable);}
        var candidates=evidence.Candidates.Take(12).Select((x,i)=>new CodexCandidate("candidate-"+(i+1),x)).ToList();
        var context=new CodexContext(file.FileName,Path.GetDirectoryName(file.FullPath)??"",TagMetadata.From(file),TagMetadata.From(row),file.DurationSeconds,
            neighbors.Where(x=>x.FullPath!=file.FullPath).OrderBy(x=>Math.Abs((long)x.Track-file.Track)).Take(30).Select(x=>(object)new{x.FileName,Metadata=TagMetadata.From(x),x.DurationSeconds}).ToList(),
            new{evidence.AcoustId,evidence.AcoustIdScore,evidence.RecordingId,evidence.LookupStatus,evidence.FingerprintCacheHit},candidates,evidence.ReviewReason,file.Bitrate);
        progress?.Invoke("Codex reasoning: "+file.FullPath);
        CodexDecision decision;
        try{decision=await client.DecideAsync(context,ct);}
        catch(CodexServiceException ex){observer?.Unavailable(file.FullPath,ex);throw new CodexPendingException(row with{Identification=evidence with{CodexModel=client.Model,CodexUnavailable=true,CodexFailureKind=ex.Kind.ToString()}},ex);}
        var validation=evidence.CandidatesComplete?Validate(file,row,candidates,decision):"Candidate set is incomplete; human review required";
        var resultEvidence=evidence with{RecognitionMethod="Codex",CodexModel=client.Model,CodexDecision=decision,CodexUnavailable=false,CodexFailureKind="",CodexValidation=validation};
        if(validation!="Accepted")
        {
            observer?.Outcome(file.FullPath,false,"",validation);
            return row with{Identification=resultEvidence with{Status="Review",ReviewReason=evidence.ReviewReason+"; Codex: "+validation,
                ReviewReasons=evidence.ReviewReasons.Append("CodexNeedsReview").Distinct().ToArray()}};
        }
        var candidate=candidates.Single(x=>x.Id==decision.SelectedCandidateId).Evidence;var m=candidate.Metadata;
        // A model selects factual evidence; it never supplies replacement text. Preserve manual fields.
        var fields=new Dictionary<string,string>(evidence.FieldSources);
        bool Manual(string field)=>fields.TryGetValue(field,out var origin)&&(origin.Contains("Manual",StringComparison.Ordinal)||origin=="Tags");
        string Text(string field,string old,string proposed){if(Manual(field)||MetadataQualityEvaluator.Invalid(proposed))return old;fields[field]="CodexValidatedMusicBrainz";return proposed;}
        uint Number(string field,uint old,uint proposed){if(Manual(field)||proposed==0)return old;fields[field]="CodexValidatedMusicBrainz";return proposed;}
        var artist=Text("Artist",row.Artist,m.Artist);var year=row.Year>0?row.Year:Number("Year",row.Year,m.Year);
        var resolved=row with{Artist=artist,TrackArtists=artist==row.Artist?row.TrackArtists:[artist],AlbumArtist=Text("AlbumArtist",row.AlbumArtist,m.AlbumArtist),Album=Text("Album",row.Album,m.Album),
            Title=Text("Title",row.Title,m.Title),Track=Number("TrackNumber",row.Track,m.Track),Disc=Number("DiscNumber",row.Disc,m.Disc),Year=year,Date=year==row.Year?row.Date:year.ToString(),
            Identification=resultEvidence with{FieldSources=fields,RecordingId=candidate.RecordingId,ReleaseId=candidate.ReleaseId,RecognitionConfidence=candidate.Confidence,
                YearEnriched=year>0&&file.Year==0,YearSource=year>0&&file.Year==0?"CodexValidatedMusicBrainz":evidence.YearSource,YearConfidence=year>0&&file.Year==0?.95:evidence.YearConfidence}};
        var complete=!ManualMetadataResolver.NeedsInput(resolved)&&resolved.Year>0;
        observer?.Outcome(file.FullPath,complete,decision.SelectedCandidateId??"","Required metadata remains incomplete");
        return resolved with{Identification=resolved.Identification! with{AutoRecognized=complete,Status=complete?"Resolved":"Review",
            ReviewReason=complete?"":"Codex selected a candidate; required fields remain unresolved.",ReviewReasons=complete?[]:["IncompleteMetadata"]}};
    }
    public static string Validate(AudioMetadata original,AudioMetadata current,IReadOnlyList<CodexCandidate> candidates,CodexDecision decision)
    {
        if(decision.HumanReviewRequired||decision.SelectedCandidateId==null)return "No reliable decision";
        if(!double.IsFinite(decision.Confidence)||decision.Confidence<.9||decision.Confidence>1||decision.Conflicts==null||decision.Conflicts.Length>0)return "Model reported uncertainty or conflicts";
        var selected=candidates.SingleOrDefault(x=>x.Id==decision.SelectedCandidateId)?.Evidence;
        if(selected==null)return "Unknown candidate ID";
        if((decision.ReleaseId??"")!=selected.ReleaseId||decision.Year.HasValue&&decision.Year.Value!=selected.Metadata.Year)return "Release/year is not supported by the selected candidate";
        if(original.Year>0&&decision.Year>0&&decision.Year!=original.Year)return "Existing year must be preserved";
        if(selected.Metadata.Year>0&&(selected.Metadata.Year<1000||selected.Metadata.Year>DateTime.UtcNow.Year+1))return "Invalid factual year";
        if(selected.Evidence.GetValueOrDefault("Fingerprint")<.95*.55||selected.Evidence.GetValueOrDefault("Duration")<.15||selected.Confidence<.78)return "Insufficient independent fingerprint/duration/context evidence";
        var next=candidates.Where(x=>x.Id!=decision.SelectedCandidateId&&x.Evidence.Metadata!=selected.Metadata).Select(x=>x.Evidence.Confidence).DefaultIfEmpty(0).Max();
        if(selected.Confidence-next<.05)return "Candidates remain too close on factual evidence";
        var localVariants=RecognitionScorer.Variants(current.Title);var matchedVariants=RecognitionScorer.Variants(selected.Metadata.Title+" "+selected.Detail);
        if(!localVariants.SequenceEqual(matchedVariants))return "Recording variant conflict";
        foreach(var pair in new[]{("Artist",original.Artist,selected.Metadata.Artist),("Title",original.Title,selected.Metadata.Title),("Album",original.Album,selected.Metadata.Album),("AlbumArtist",original.AlbumArtist,selected.Metadata.AlbumArtist)})
            if(!MetadataQualityEvaluator.Invalid(pair.Item2)&&!MetadataQualityEvaluator.Invalid(pair.Item3)&&!(pair.Item1=="Title"&&MetadataQualityEvaluator.FilenameTitle(original))&&MetadataNormalizer.Key(pair.Item2)!=MetadataNormalizer.Key(pair.Item3))return "Reliable embedded "+pair.Item1+" conflicts with candidate";
        return "Accepted";
    }
}
