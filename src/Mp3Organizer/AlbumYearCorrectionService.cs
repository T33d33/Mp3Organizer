using System.Security.Cryptography;
namespace Mp3Organizer;

// Explicit manual years only; matching includes source root, folder, artist and album.
public sealed class AlbumYearCorrectionService(ProgressRepository repository,string workspace)
{
    private static string Key(IndexedMusic x)=>x.SourceRoot+"|"+Path.GetDirectoryName(x.CurrentPath)+"|"+MetadataNormalizer.Key(AlbumGrouper.Owner(x.Effective))+"|"+MetadataNormalizer.Key(x.Effective.Album);
    public void Propagate()
    {
        var all=repository.All();
        foreach(var group in all.GroupBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            var years=group.Where(x=>x.Effective.Identification?.FieldSources.GetValueOrDefault("Year") is "FileManualOverride" or "FolderManualOverride").Select(x=>x.Effective.Year).Where(x=>x>=1000&&x<=9999).Distinct().ToArray();
            if(years.Length!=1)continue;
            foreach(var item in group.Where(x=>x.Effective.Year==0&&x.Status!=ProcessingStatus.Skipped))
            {
                var store=new ManualResolutionStore(workspace,item.SourceRoot);var manual=store.Load();var key="sha256:"+item.Basic.Sha256.ToLowerInvariant();manual.Files.TryGetValue(key,out var old);
                manual.Files[key]=(old??new()) with{LastKnownPath=item.CurrentPath,Year=years[0]};store.Save(manual);
                var evidence=item.Effective.Identification??new();var sources=new Dictionary<string,string>(evidence.FieldSources){["Year"]="FileManualOverride"};
                repository.Save(item with{Effective=item.Effective with{Year=years[0],Date=years[0].ToString(),Identification=evidence with{FieldSources=sources,ManualOverrideHit=true}}},"Confirmed album year applied to missing year; target reconciliation pending");
            }
        }
    }
    public static bool TrustedYear(AudioMetadata row)
    {
        var evidence=row.Identification;if(evidence==null)return false;
        return evidence.FieldSources.GetValueOrDefault("Year") is "FileManualOverride" or "FolderManualOverride"
            ||evidence.FieldSources.GetValueOrDefault("Year")=="MusicBrainz"&&evidence.YearSource=="MusicBrainz"&&evidence.YearEnriched&&evidence.YearConfidence>=.95&&!string.IsNullOrWhiteSpace(evidence.AlbumGroupId);
    }
    public sealed record MoveEntry(string OldPath,string NewPath,AudioMetadata Metadata);
    public void Reconcile(string source,string target,Action<string>? progress=null)
    {
        var state=ManagedTarget.Check(target,source);if(state?.MetadataManaged!=true)return;
        var journal=PathSafetyGuard.Destination(target,".album-year-repair.json",source);
        var manifestPath=PathSafetyGuard.Destination(target,TargetMetadataStore.FileName,source);
        var manifest=JsonFormat.Read<Dictionary<string,AudioMetadata>>(File.ReadAllText(manifestPath));
        List<MoveEntry> moves;
        if(File.Exists(journal))moves=JsonFormat.Read<List<MoveEntry>>(File.ReadAllText(journal));
        else
        {
            moves=new();var items=repository.All().ToDictionary(x=>x.Id);
            foreach(var group in repository.Mappings(target).GroupBy(x=>x.RelativePath,StringComparer.OrdinalIgnoreCase))
            {
                if(!manifest.TryGetValue(group.Key,out var old))continue;
                var matches=group.Select(x=>items.GetValueOrDefault(x.FileId)).Where(x=>x!=null&&x.Status==ProcessingStatus.Processed).Select(x=>x!).ToList();
                var years=matches.Select(x=>x.Effective.Year).Where(x=>x>=1000&&x<=9999).Distinct().ToArray();
                var corrected=old;
                var decisions=new ManualResolutionStore(workspace,source).Load().Folders.Values.Where(x=>x.AlbumSetupConfirmed&&x.ReviewFileIds!=null&&matches.Any(m=>x.ReviewFileIds.Contains(m.Id))).Select(x=>(x.Album,x.AlbumArtist,x.Year)).Distinct().ToArray();
                if(decisions.Length==1){var context=decisions[0];corrected=corrected with{Album=context.Album??old.Album,AlbumArtist=context.AlbumArtist??old.AlbumArtist,Year=context.Year??old.Year,Date=(context.Year??old.Year).ToString()};}

                if(old.Year==0&&years.Length==1&&matches.Any(x=>TrustedYear(x.Effective)))corrected=corrected with{Year=years[0],Date=years[0].ToString()};
                string? Shared(string field,Func<AudioMetadata,string> get)
                {
                    var values=matches.Where(x=>x.Effective.Identification?.FieldSources.GetValueOrDefault(field) is "FolderManualOverride" or "FileManualOverride").Select(x=>get(x.Effective)).Where(x=>!MetadataQualityEvaluator.Invalid(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    return values.Length==1?values[0]:null;
                }
                if(MetadataQualityEvaluator.Invalid(old.Album)&&Shared("Album",x=>x.Album) is {} album)corrected=corrected with{Album=album};
                if((MetadataQualityEvaluator.Invalid(old.AlbumArtist)||Shared("AlbumArtist",x=>x.AlbumArtist)=="Various Artists")&&Shared("AlbumArtist",x=>x.AlbumArtist) is {} owner)corrected=corrected with{AlbumArtist=owner};
                if(corrected==old)continue;
                // Preserve the existing filename, including collision suffixes. Only replace the album directory.
                var desired=Path.Combine(Path.GetDirectoryName(TargetPathBuilder.Build(corrected))!,Path.GetFileName(group.Key));
                if(!desired.Equals(group.Key,StringComparison.OrdinalIgnoreCase))moves.Add(new(group.Key,desired,corrected));
            }
            if(moves.Count==0)return;
            if(moves.Select(x=>x.NewPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=moves.Count)throw new IOException("Album year correction has conflicting destinations.");
            foreach(var move in moves)
            {
                Verify(move.OldPath,move.Metadata);
                var destination=PathSafetyGuard.Destination(target,move.NewPath,source);
                if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("Album year correction conflict; destination already exists: "+destination);
            }
            new M3u8Writer().WriteNew(journal,JsonFormat.Serialize(moves),source);
        }
        foreach(var move in moves)
        {
            var old=PathSafetyGuard.Destination(target,move.OldPath,source);var next=PathSafetyGuard.Destination(target,move.NewPath,source);
            if(File.Exists(old))
            {
                Verify(move.OldPath,move.Metadata);if(File.Exists(next)||Directory.Exists(next))throw new IOException("Album correction collision: "+next);
                Directory.CreateDirectory(Path.GetDirectoryName(next)!);File.Move(old,next,false);
            }
            Verify(move.NewPath,move.Metadata);manifest.Remove(move.OldPath);manifest[move.NewPath]=move.Metadata;
            foreach(var mapping in repository.Mappings(target).Where(x=>x.RelativePath.Equals(move.OldPath,StringComparison.OrdinalIgnoreCase)))
            {
                var item=repository.All().Single(x=>x.Id==mapping.FileId);repository.MapCanonical(item,target,move.NewPath,move.Metadata);
            }
            progress?.Invoke("Corrected album year: "+move.OldPath+" -> "+move.NewPath);
        }
        var map=new PlaylistMapStore().Load(target);
        var lists=new PlaylistBuilder().Build(manifest.Select(x=>new CopyOperation(x.Value,x.Key,"Existing")).ToList(),map);
        FolderPlaylistService.PreserveExisting(target,lists);PlaylistBuilder.IncludeRetired(lists,state);
        new ManagedPlaylistWriter().Write(target,source,lists,map,manifest);
        new FolderPlaylistService(repository).Regenerate(source,target,progress);
        foreach(var dir in moves.Select(x=>Path.GetDirectoryName(PathSafetyGuard.Destination(target,x.OldPath,source))!).Distinct())
        {PathSafetyGuard.Writable(dir,source);if(Directory.Exists(dir)&&!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir,false);}
        PathSafetyGuard.Writable(journal,source);File.Delete(journal);
        void Verify(string relative,AudioMetadata metadata)
        {
            var path=PathSafetyGuard.Destination(target,relative,source);using var input=new ReadOnlySource().OpenRead(path);
            if(input.Length!=metadata.Size||Convert.ToHexString(SHA256.HashData(input))!=metadata.Sha256)throw new IOException("Album year correction content mismatch: "+path);
        }
    }
}
