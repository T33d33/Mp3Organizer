namespace Mp3Organizer;

// Projects persisted source playlist occurrences through verified canonical mappings.
// This stage never reads an entry's source path, guesses a basename, or copies audio.
public sealed class ImportedPlaylistBuilder(ProgressRepository repository)
{
    public List<PlaylistDefinition> Build(string source,string target,IReadOnlyDictionary<string,IndexedMusic> indexed,
        IReadOnlyDictionary<string,CanonicalMapping> mappings,IReadOnlyDictionary<string,AudioMetadata> targetFiles,Action<string>? progress)
    {
        var lists=new List<PlaylistDefinition>();
        foreach(var playlist in repository.SourcePlaylists().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)))
        {
            var lines=new List<string>();var omitted=0;
            foreach(var entry in repository.SourcePlaylistEntries(playlist.Id))
            {
                var problem=!playlist.Present?"Source playlist absent at last complete scan":playlist.Error!=""?"Playlist unreadable: "+playlist.Error:entry.Problem;
                var relative="";
                if(problem=="")
                {
                    if(!indexed.TryGetValue(entry.FileId,out var file))problem="Source file is not indexed; scan again";
                    else if(file.Status!=ProcessingStatus.Processed)problem="Source status: "+file.Status;
                    else if(file.Effective.Identification?.MetadataPolicyVersion!=MetadataPolicy.Version||file.Effective.Identification.Status=="Review")problem="Metadata still requires review";
                    else if(!mappings.TryGetValue(file.Id,out var mapping)||mapping.SourceHash!=file.Basic.Sha256)problem="No current verified target mapping";
                    else if(!targetFiles.TryGetValue(mapping.RelativePath,out var actual)||actual.Sha256!=mapping.TargetHash||actual.Size!=mapping.Size)problem="Canonical target missing or changed";
                    else
                    {
                        var destination=PathSafetyGuard.Destination(target,mapping.RelativePath,source);
                        relative=Path.GetRelativePath(Path.GetDirectoryName(Path.Combine(target,playlist.TargetPath))!,destination).Replace('/','\\');
                    }
                }
                repository.PlaylistEntryResult(playlist.Id,entry.Position,problem==""?"Playable":problem,relative);
                if(problem=="")lines.Add(relative);else omitted++;
            }
            lists.Add(new(playlist.TargetPath,$"#EXTM3U\n#SOURCE-PLAYLIST-ID:{playlist.Id}\n#PLAYABLE:{lines.Count}\n#OMITTED:{omitted}\n"+
                (playlist.Error!=""?"#IMPORT-ERROR:See playlist import report\n":"")+(!playlist.Present?"#SOURCE-PLAYLIST-ABSENT\n":"")+string.Join("\n",lines)+(lines.Count>0?"\n":"")));
            progress?.Invoke($"Code {playlist.Code} - {playlist.Name}: {lines.Count} playable; {omitted} omitted"+(playlist.Error!=""?"; "+playlist.Error:""));
        }
        return lists;
    }
    public void WriteReport(string source,string target,Action<string>? progress)
    {
        var playlists=repository.SourcePlaylists().Where(x=>x.SourceRoot.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();if(playlists.Count==0)return;
        var workspace=Path.GetDirectoryName(repository.DatabasePath)!;PathSafetyGuard.Separate(source,workspace);PathSafetyGuard.Separate(target,workspace);
        string Q(string value)=>"\""+((value.Length>0&&"=+-@\t\r".Contains(value[0])?"'":"")+value).Replace("\"","\"\"")+"\"";
        var rows=new List<string>{"Code,SourcePlaylist,PlaylistPath,Position,OriginalEntry,Result,TargetEntry"};
        foreach(var playlist in playlists)
        {
            var entries=repository.SourcePlaylistEntries(playlist.Id);
            if(entries.Count==0||playlist.Error!=""||!playlist.Present)rows.Add(string.Join(",",new[]{playlist.Code,playlist.Path,playlist.TargetPath,"","",!playlist.Present?"Source playlist absent":playlist.Error!=""?playlist.Error:"Empty playlist",""}.Select(Q)));
            foreach(var entry in entries)rows.Add(string.Join(",",new[]{playlist.Code,playlist.Path,playlist.TargetPath,entry.Position.ToString(),entry.OriginalEntry,entry.Result,entry.TargetPath}.Select(Q)));
        }
        var path=Path.Combine(workspace,"playlist-import-reports",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N")+".csv");
        new M3u8Writer().WriteNew(path,string.Join("\r\n",rows)+"\r\n",source);progress?.Invoke("Playlist import report: "+path);
    }
}
