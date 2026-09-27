using System.Security.Cryptography;

namespace Mp3Organizer;

public sealed class FolderPlaylistService(ProgressRepository repository)
{
    public static void PreserveExisting(string target,List<PlaylistDefinition> lists)
    {
        var state=ManagedTarget.Check(target);if(state==null)return;
        foreach(var path in state.Playlists.Where(IsFolder))
        {
            if(lists.Any(x=>x.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase)))continue;
            var full=PathSafetyGuard.Destination(target,path,state.Source);if(File.Exists(full))lists.Add(new(path,File.ReadAllText(full)));
        }
    }
    private static bool IsFolder(string path)=>path.Replace('\\','/').StartsWith("_Playlists/Folders/",StringComparison.OrdinalIgnoreCase)||path.Replace('\\','/').StartsWith("_Playlist-Folder/",StringComparison.OrdinalIgnoreCase);
    public void Regenerate(string source,string target,Action<string>? progress=null)
    {
        var state=ManagedTarget.Check(target,source);if(state==null)return;
        var scanner=new AudioFileScanner();var reader=new TagLibMetadataReader(new ReadOnlySource());
        var actual=scanner.Scan(target).Select(reader.Read).ToList();
        if(scanner.Errors.Count>0||actual.Any(x=>x.Error!=""))throw new IOException("Cannot verify target audio for folder playlists.");
        var tracks=TargetMetadataStore.Overlay(target,actual);
        var indexed=repository.All().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)).ToDictionary(x=>x.Id);
        // Backfill older successful copies only from verified target content; never from inferred filenames.
        var mappings=repository.Mappings(target).ToDictionary(x=>x.FileId);
        foreach(var item in indexed.Values.Where(x=>x.Status==ProcessingStatus.Processed&&x.Effective.Identification?.MetadataPolicyVersion==MetadataPolicy.Version&&x.Effective.Identification.Status!="Review"))
        {
            if(mappings.TryGetValue(item.Id,out var saved)&&saved.SourceHash==item.Basic.Sha256&&actual.Any(x=>Path.GetRelativePath(target,x.FullPath).Equals(saved.RelativePath,StringComparison.OrdinalIgnoreCase)&&x.Sha256==saved.TargetHash&&x.Size==saved.Size))continue;
            var match=actual.Where(x=>x.Sha256==item.Basic.Sha256&&x.Size==item.Basic.Size).OrderBy(x=>x.FullPath,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if(match!=null)repository.MapCanonical(item,target,Path.GetRelativePath(target,match.FullPath),match);
        }
        mappings=repository.Mappings(target).ToDictionary(x=>x.FileId);
        var actualByPath=actual.ToDictionary(x=>Path.GetRelativePath(target,x.FullPath),StringComparer.OrdinalIgnoreCase);
        var map=new PlaylistMapStore().Load(target);
        var lists=new PlaylistBuilder().Build(tracks.Select(x=>new CopyOperation(x,Path.GetRelativePath(target,x.FullPath),"Existing")).ToList(),map);
        var occurrences=repository.Occurrences().ToDictionary(x=>x.Id);
        foreach(var folder in repository.Folders().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)))
        {
            var lines=new List<string>();var pending=0;
            foreach(var id in repository.FolderOrder(folder.Id))
            {
                if(!occurrences.TryGetValue(id,out var occurrence)||!occurrence.Present)continue;
                if(!indexed.TryGetValue(occurrence.FileId,out var item)||item.Status!=ProcessingStatus.Processed||item.Effective.Identification?.MetadataPolicyVersion!=MetadataPolicy.Version||item.Effective.Identification.Status=="Review"||!mappings.TryGetValue(item.Id,out var mapping)||mapping.SourceHash!=item.Basic.Sha256||
                    !actualByPath.TryGetValue(mapping.RelativePath,out var file)||file.Sha256!=mapping.TargetHash||file.Size!=mapping.Size)
                {pending++;continue;}
                var destination=PathSafetyGuard.Destination(target,mapping.RelativePath,source);
                lines.Add(Path.GetRelativePath(Path.GetDirectoryName(Path.Combine(target,folder.PlaylistPath))!,destination).Replace('/','\\'));
            }
            lists.Add(new(folder.PlaylistPath,"#EXTM3U\n"+$"#FOLDER-ID:{folder.Code}\n#PLAYABLE:{lines.Count}\n#OMITTED:{pending}\n"+string.Join("\n",lines)+(lines.Count>0?"\n":"")));
            progress?.Invoke($"{folder.Code} - {folder.Name}: {lines.Count} playable occurrences; {pending} pending/review/error/skipped or unavailable.");
        }
        var imports=new ImportedPlaylistBuilder(repository);
        lists.AddRange(imports.Build(source,target,indexed,mappings,actualByPath,progress));
        PreserveExisting(target,lists);PlaylistBuilder.IncludeRetired(lists,state);
        new ManagedPlaylistWriter().Write(target,source,lists,map,tracks.ToDictionary(x=>Path.GetRelativePath(target,x.FullPath),StringComparer.OrdinalIgnoreCase));
        imports.WriteReport(source,target,progress);
    }
}
