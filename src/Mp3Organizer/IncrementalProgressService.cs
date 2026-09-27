namespace Mp3Organizer;

public interface IProgressIdentityMatcher
{
    IndexedMusic? Match(AudioMetadata file,IReadOnlyList<IndexedMusic> missing);
}
public sealed class ShaProgressIdentityMatcher:IProgressIdentityMatcher
{
    public IndexedMusic? Match(AudioMetadata file,IReadOnlyList<IndexedMusic> missing)
    {
        if(file.Sha256.Length!=64)return null;
        var matches=missing.Where(x=>x.Basic.Sha256==file.Sha256&&x.Basic.Size==file.Size).ToList();return matches.Count==1?matches[0]:null;
    }
}
public sealed record IncrementalScanResult(int Added,int Updated,int Unchanged,IReadOnlyList<string> Errors);
public sealed class IncrementalProgressService(ProgressRepository repository,Func<string,AudioMetadata>? reader=null,IProgressIdentityMatcher? identity=null,Mp3TagWriteService? tagWriter=null,CodexRunState? codexState=null,Func<CodexServiceException,bool>? continueWithoutCodex=null)
{
    public int LastErrorCount {get;private set;}
    public bool StoppedForCodex {get;private set;}
    private readonly Func<string,AudioMetadata> read=reader??new TagLibMetadataReader(new ReadOnlySource()).Read;
    private readonly IProgressIdentityMatcher matcher=identity??new ShaProgressIdentityMatcher();
    public IncrementalScanResult Scan(string source,bool force=false,Action<string>? progress=null)
    {
        source=PathSafetyGuard.Canonical(source);PathSafetyGuard.Separate(source,Path.GetDirectoryName(repository.DatabasePath)!);
        var scanner=new AudioFileScanner();var paths=scanner.Scan(source);var pathSet=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known=repository.All();var byPath=known.ToDictionary(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase);
        var missing=known.Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)&&!pathSet.Contains(x.CurrentPath)&&!File.Exists(x.CurrentPath)).ToList();
        var added=0;var updated=0;var unchanged=0;var errors=new List<string>(scanner.Errors);
        foreach(var path in paths)
        {
            byPath.TryGetValue(path,out var old);
            try
            {
                var info=new FileInfo(path);
                if(!force&&old!=null&&info.Length==old.Basic.Size&&info.LastWriteTimeUtc==old.Basic.LastWriteTimeUtc){unchanged++;continue;}
                progress?.Invoke("Reading changed/new file: "+path);
                var basic=read(path);
                if(old==null){old=matcher.Match(basic,missing);if(old!=null)missing.Remove(old);}
                if(old!=null&&old.Basic.Sha256==basic.Sha256)basic=basic with{SuppressedAutomaticFields=old.Basic.SuppressedAutomaticFields};
                var same=old!=null&&basic.Sha256.Length==64&&basic.Sha256==old.Basic.Sha256&&TagMetadata.From(basic)==TagMetadata.From(old.Basic)&&basic.Error==old.Basic.Error;
                var effective=same?ProgressRepository.At(old!.Effective,path):basic;
                var status=basic.Error!=""?ProcessingStatus.Error:same?old!.Status:ProcessingStatus.Discovered;
                var item=new IndexedMusic(old?.Id??Guid.NewGuid().ToString("N"),source,old?.OriginalPath??path,path,basic,effective,status,old?.LastProcessedUtc,basic.Error);
                repository.Save(item,old==null?"Discovered":same?"Rescan or relocation; progress retained":"File changed; requires analysis");
                if(old==null)added++;else updated++;
                if(basic.Error!="")errors.Add(path+": "+basic.Error);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add(path+": "+e.Message);
                // Repository failures must not be silently reported as a successful persisted scan.
                var basic=old?.Basic??new AudioMetadata{FullPath=path,FileName=Path.GetFileName(path),OriginalSourcePath=path};
                repository.Save(new(old?.Id??Guid.NewGuid().ToString("N"),source,old?.OriginalPath??path,path,basic,old?.Effective??basic,ProcessingStatus.Error,old?.LastProcessedUtc,e.Message),"Scan error");
            }
        }
        repository.RegisterFolders(source,paths,scanner.Errors.Count==0,progress);
        repository.RegisterSourcePlaylists(source,scanner.Playlists,scanner.Errors.Count==0,progress);
        return new(added,updated,unchanged,errors);
    }
    public async Task<int> AnalyzeAsync(int limit,Func<string,IMetadataResolver> resolverForRoot,Action<string>? progress=null)
    {
        if(limit<1)throw new ArgumentOutOfRangeException(nameof(limit));
        LastErrorCount=0;
        StoppedForCodex=false;
        var all=repository.All();var pending=all.Where(x=>x.Status is ProcessingStatus.Discovered or ProcessingStatus.Analyzed).OrderBy(x=>x.CurrentPath,StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
        var folders=all.GroupBy(x=>Path.GetDirectoryName(x.CurrentPath)!,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>(IReadOnlyList<AudioMetadata>)x.Select(y=>y.Basic).ToList(),StringComparer.OrdinalIgnoreCase);
        var count=0;
        foreach(var item in pending)
        {
            progress?.Invoke($"Analyzing [{count+1}/{pending.Count}]: {item.CurrentPath}");
            IndexedMusic completed;
            try
            {
                PathSafetyGuard.NoLinks(item.CurrentPath);var info=new FileInfo(item.CurrentPath);
                if(!info.Exists)throw new FileNotFoundException("Indexed file no longer exists",item.CurrentPath);
                // Reread after a reset, or when observations changed since scanning. Never trust a stale failed read.
                var basic=item.Basic.Error!=""||info.Length!=item.Basic.Size||info.LastWriteTimeUtc!=item.Basic.LastWriteTimeUtc?read(item.CurrentPath):item.Basic;
                if(basic.Error!="")throw new IOException(basic.Error);
                var resolver=resolverForRoot(item.SourceRoot);
                repository.Save(item with{Basic=basic,Status=ProcessingStatus.Analyzed,ErrorMessage=""},"Analysis started; retryable until resolution completes");
                if(basic.Sha256==item.Basic.Sha256)basic=basic with{SuppressedAutomaticFields=item.Basic.SuppressedAutomaticFields};
                if(resolver is IInventoryAwareMetadataResolver aware)aware.SetInventory(all.Select(x=>MetadataPolicy.Sanitize(x.Basic)).ToList());
                var neighbors=folders[Path.GetDirectoryName(item.CurrentPath)!].Select(x=>MetadataPolicy.Sanitize(x.FullPath.Equals(item.CurrentPath,StringComparison.OrdinalIgnoreCase)?basic:x)).ToList();
                var task=Task.Run(()=>resolver.ResolveAsync(MetadataPolicy.Sanitize(basic),neighbors));
                while(await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(3)))!=task)progress?.Invoke("Still analyzing: "+item.CurrentPath);
                var resolved=await task;
                resolved=resolved with{Identification=(resolved.Identification??new()) with{MetadataPolicyVersion=2}};
                if(resolved.Error!="")throw new IOException(resolved.Error);
                var status=resolved.Identification?.Status=="Review"||ManualMetadataResolver.NeedsInput(resolved)?ProcessingStatus.NeedsReview:ProcessingStatus.Ready;
                completed=item with{Basic=basic,Effective=resolved,Status=status,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=""};
            }
            catch(CodexPendingException e)
            {
                // Commit before presenting a choice. Analyzed remains eligible on the next run.
                var waiting=item with{Effective=e.Metadata,Status=ProcessingStatus.Analyzed,ErrorMessage="Waiting for Codex: "+e.Message};
                repository.Save(waiting,"Codex pending; deterministic candidates preserved");
                progress?.Invoke("Progress saved. Codex "+e.Failure.Kind+": "+e.Message);
                if(e.Failure.Kind is CodexFailureKind.AuthenticationConfiguration or CodexFailureKind.InvalidRequest||codexState==null||continueWithoutCodex?.Invoke(e.Failure)!=true)
                {StoppedForCodex=true;break;}
                codexState.Unavailable=e.Failure;
                completed=waiting with{Effective=CodexRunState.Fallback(e.Metadata,e.Failure),Status=ProcessingStatus.NeedsReview,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=""};
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
            {completed=item with{Status=ProcessingStatus.Error,LastProcessedUtc=DateTime.UtcNow.ToString("O"),ErrorMessage=e.Message};}
            // Do not swallow database write failures: a file counts only after its commit succeeds.
            if(tagWriter!=null&&completed.Status==ProcessingStatus.Ready&&Path.GetExtension(completed.CurrentPath).Equals(".mp3",StringComparison.OrdinalIgnoreCase))
            {
                try{completed=tagWriter.WriteAndSave(completed);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
                {completed=completed with{Status=ProcessingStatus.Error,ErrorMessage=ex.Message};}
            }
            repository.Save(completed,"Analysis completed");if(completed.Status==ProcessingStatus.Error)LastErrorCount++;count++;progress?.Invoke(completed.Status+": "+item.CurrentPath);
        }
        return count;
    }
}
