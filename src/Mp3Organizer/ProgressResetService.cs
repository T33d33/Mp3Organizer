namespace Mp3Organizer;

public sealed class ProgressResetService(ProgressRepository repository)
{
    public int Reset(string command,string? path=null)
    {
        var canonical=path==null?null:PathSafetyGuard.Canonical(path);
        return repository.Reset(item=>command switch
        {
            "reset-file"=>string.Equals(item.CurrentPath,canonical,StringComparison.OrdinalIgnoreCase),
            "reset-folder"=>PathSafetyGuard.Within(item.CurrentPath,canonical!),
            "reset-errors"=>item.Status==ProcessingStatus.Error,
            "reset-review"=>item.Status==ProcessingStatus.NeedsReview,
            "reset-progress"=>true,
            _=>throw new ArgumentException("Unknown progress reset")
        });
    }
    public bool ResetAll(Func<string?> confirm,Action<string> output)
    {
        var count=repository.All().Count;var backup=repository.Backup();
        output($"This will reset progress for {count:N0} indexed files.\nDatabase backup created: {backup}\nManual resolutions and identification cache are retained.\nType RESET to continue:");
        if(confirm()!="RESET"){output("Cancelled; progress unchanged.");return false;}
        repository.Clear();output("Progress index cleared.");return true;
    }
}
