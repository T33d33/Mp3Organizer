namespace Mp3Organizer;

public sealed record LlmStatistics(long Requests=0,long Resolved=0,long NeedsReview=0,long Unavailable=0,long DeterministicOnly=0)
{
    public void Print(Action<string> write)
    {
        write($"LLM requests:        {Requests}\nLLM resolved:        {Resolved}\nLLM needs review:    {NeedsReview}\nLLM unavailable:     {Unavailable}\nDeterministic only:  {DeterministicOnly}");
    }
}
public sealed class LlmObservability(Action<string> write,ProgressRepository? repository=null)
{
    private readonly string runId=Guid.NewGuid().ToString("N");
    public LlmStatistics Statistics {get;private set;}=new();
    private void Record(string kind,string path,string detail)
    {
        repository?.RecordLlmEvent(runId,kind,path,detail);
        Statistics=kind switch{
            "Request"=>Statistics with{Requests=Statistics.Requests+1},
            "Resolved"=>Statistics with{Resolved=Statistics.Resolved+1},
            "NeedsReview"=>Statistics with{NeedsReview=Statistics.NeedsReview+1},
            "Unavailable"=>Statistics with{Unavailable=Statistics.Unavailable+1},
            "DeterministicOnly"=>Statistics with{DeterministicOnly=Statistics.DeterministicOnly+1},_=>Statistics};
    }
    public void Request(CodexContext context,int attempt,string model)
    {
        var path=Path.Combine(context.FolderPath,context.FileName);
        Record("Request",path,$"model={model}; attempt={attempt}");
        write($"LLM: requesting decision... (OpenAI API HTTP attempt {attempt}; model={model})");
    }
    public void Deterministic(string path,string reason="not required")
    {Record("DeterministicOnly",path,reason);write("LLM: "+reason+" (no API request)");}
    public void Outcome(string path,bool resolved,string candidate,string reason)
    {
        Record(resolved?"Resolved":"NeedsReview",path,resolved?candidate:reason);
        write(resolved?"LLM: resolved candidate "+candidate:"LLM: unable to decide -> NeedsReview ("+reason+")");
    }
    public void Unavailable(string path,CodexServiceException error)
    {Record("Unavailable",path,error.Kind.ToString());write("LLM: unavailable - "+error.Kind+": "+error.Message);}
    public void DisableForRun()=>write("LLM: disabled for remainder of run - deterministic fallback enabled");
}
public sealed class DeterministicObservedResolver(IMetadataResolver inner,LlmObservability observer):IMetadataResolver,IInventoryAwareMetadataResolver
{
    public void SetInventory(IReadOnlyList<AudioMetadata> rows){if(inner is IInventoryAwareMetadataResolver aware)aware.SetInventory(rows);}
    public async Task<AudioMetadata> ResolveAsync(AudioMetadata file,IReadOnlyList<AudioMetadata> neighbors,bool identifyAll=false,CancellationToken ct=default)
    {
        var result=await inner.ResolveAsync(file,neighbors,identifyAll,ct);
        observer.Deterministic(file.FullPath,"disabled by configuration/offline mode");return result;
    }
}
