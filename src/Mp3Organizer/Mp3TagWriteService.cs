using System.Security.Cryptography;

namespace Mp3Organizer;

public sealed record TagWriteJournal(string State,IndexedMusic Before,IndexedMusic After,string BackupPath);
public sealed class Mp3TagWriteService(string workspace,ProgressRepository repository)
{
    private string JournalDirectory=>Path.Combine(workspace,"tag-writes");
    public IndexedMusic WriteAndSave(IndexedMusic item,bool manual=false)
    {
        var path=item.CurrentPath;PathSafetyGuard.NoLinks(path);PathSafetyGuard.Separate(item.SourceRoot,workspace);
        if(!PathSafetyGuard.Within(path,item.SourceRoot)||!Path.GetExtension(path).Equals(".mp3",StringComparison.OrdinalIgnoreCase))throw new IOException("Tag writing is limited to indexed MP3 source files.");
        var e=item.Effective.Identification;
        if(item.Status==ProcessingStatus.NeedsReview||e?.Status=="Review")throw new IOException("Unresolved results cannot write tags.");
        if(!manual&&e?.AutoRecognized!=true&&!(e?.YearEnriched==true&&e.YearConfidence>=.95))return item;
        var reader=new TagLibMetadataReader(new ReadOnlySource());var original=reader.Read(path);
        if(original.Error!=""||original.Sha256!=item.Basic.Sha256)throw new IOException("Source changed before tag writing; scan again.");
        var desired=item.Effective;
        if(!manual&&original.Year!=0)desired=desired with{Year=original.Year};
        if(desired.Year!=0&&(desired.Year<1000||desired.Year>9999))throw new IOException("Year tag must be four digits.");
        var fingerprint=Mp3AudioPayload.Hash(path);
        PathSafetyGuard.Writable(JournalDirectory,item.SourceRoot);Directory.CreateDirectory(JournalDirectory);
        var token=Guid.NewGuid().ToString("N");var backup=Path.Combine(JournalDirectory,token+".original.mp3");
        var journalPath=Path.Combine(JournalDirectory,token+".json");
        // This is the sole opt-in source-write boundary. The legacy read-only abstraction is unchanged.
        var temporary=Path.Combine(Path.GetDirectoryName(path)!,".mp3organizer-tags-"+token+".mp3");
        PathSafetyGuard.NoLinks(temporary);
        File.Copy(path,backup,false);File.Copy(path,temporary,false);
        using(var tags=TagLib.File.Create(temporary))
        {
            bool Replace(string existing)=>manual||MetadataQualityEvaluator.Invalid(existing);
            if(Replace(string.Join("; ",tags.Tag.Performers))&&!MetadataQualityEvaluator.Invalid(desired.Artist))tags.Tag.Performers=desired.TrackArtists.Length>0?desired.TrackArtists:[desired.Artist];
            if(Replace(string.Join("; ",tags.Tag.AlbumArtists))&&!MetadataQualityEvaluator.Invalid(desired.AlbumArtist))tags.Tag.AlbumArtists=[desired.AlbumArtist];
            if(Replace(tags.Tag.Album??"")&&!MetadataQualityEvaluator.Invalid(desired.Album))tags.Tag.Album=desired.Album;
            if((Replace(tags.Tag.Title??"")||MetadataQualityEvaluator.FilenameTitle(original))&&!MetadataQualityEvaluator.Invalid(desired.Title))tags.Tag.Title=desired.Title;
            if((manual||tags.Tag.Track==0)&&desired.Track>0)tags.Tag.Track=desired.Track;
            if((manual||tags.Tag.Disc==0)&&desired.Disc>0)tags.Tag.Disc=desired.Disc;
            if((manual||tags.Tag.Year==0)&&desired.Year>=1000)tags.Tag.Year=desired.Year;
            tags.Save();
        }
        if(Mp3AudioPayload.Hash(temporary)!=fingerprint)throw new IOException("Audio bytes changed in staged file; original left untouched.");
        var staged=reader.Read(temporary);if(staged.Error!="")throw new IOException("Staged MP3 metadata could not be verified.");
        if(desired.Year>=1000&&staged.Year!=desired.Year)throw new IOException("Staged year does not match approved year.");
        var basic=staged with{FullPath=path,OriginalSourcePath=original.OriginalSourcePath,FileName=Path.GetFileName(path)};
        var after=item with{Basic=basic,Effective=desired with{Sha256=basic.Sha256,Size=basic.Size,LastWriteTimeUtc=basic.LastWriteTimeUtc}};
        var journal=new TagWriteJournal("Prepared",item,after,backup);SaveJournal(journalPath,journal);
        using(var input=new ReadOnlySource().OpenRead(path))if(Convert.ToHexString(SHA256.HashData(input))!=original.Sha256)throw new IOException("Source changed during tag preparation; original not replaced.");
        File.Replace(temporary,path,null);
        return Finish(journalPath,journal);
    }
    public void Recover()
    {
        if(!Directory.Exists(JournalDirectory))return;PathSafetyGuard.NoLinks(JournalDirectory);
        foreach(var path in Directory.EnumerateFiles(JournalDirectory,"*.json"))
        {
            PathSafetyGuard.NoLinks(path);var journal=JsonFormat.Read<TagWriteJournal>(File.ReadAllText(path));if(journal.State!="Prepared")continue;
            var row=repository.All().SingleOrDefault(x=>x.Id==journal.Before.Id);
            if(row==null)continue;
            if(!row.CurrentPath.Equals(journal.Before.CurrentPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Pending tag journal path differs from index.");
            using var input=new ReadOnlySource().OpenRead(row.CurrentPath);var hash=Convert.ToHexString(SHA256.HashData(input));
            if(hash==journal.After.Basic.Sha256)Finish(path,journal);
            else if(hash==journal.Before.Basic.Sha256)SaveJournal(path,journal with{State="NotApplied"});
            else throw new IOException("Pending tag write cannot be reconciled; retain backup and inspect "+path);
        }
    }
    private IndexedMusic Finish(string journalPath,TagWriteJournal journal)
    {
        var store=new ManualResolutionStore(workspace,journal.Before.SourceRoot);var decisions=store.Load();var changed=false;
        var oldKey="sha256:"+journal.Before.Basic.Sha256.ToLowerInvariant();var newKey="sha256:"+journal.After.Basic.Sha256.ToLowerInvariant();
        var old=decisions.Files.FirstOrDefault(x=>x.Key.Equals(oldKey,StringComparison.OrdinalIgnoreCase));
        if(old.Value!=null&&!decisions.Files.ContainsKey(newKey)){decisions.Files[newKey]=old.Value;changed=true;}
        // Keep old decisions/anchors for any unchanged byte-identical copies; add the new content identity.
        foreach(var pair in decisions.Folders.ToList())if(pair.Value.AnchorSha256?.Contains(journal.Before.Basic.Sha256,StringComparer.OrdinalIgnoreCase)==true&&!pair.Value.AnchorSha256.Contains(journal.After.Basic.Sha256,StringComparer.OrdinalIgnoreCase))
        {decisions.Folders[pair.Key]=pair.Value with{AnchorSha256=pair.Value.AnchorSha256.Append(journal.After.Basic.Sha256).ToArray()};changed=true;}
        if(changed)store.Save(decisions);
        var after=journal.After with{Basic=journal.After.Basic with{LastWriteTimeUtc=File.GetLastWriteTimeUtc(journal.After.CurrentPath)},Effective=journal.After.Effective with{LastWriteTimeUtc=File.GetLastWriteTimeUtc(journal.After.CurrentPath)}};
        repository.Save(after,"Verified tag-only write; audio SHA-256 unchanged; backup "+journal.BackupPath);SaveJournal(journalPath,journal with{State="Committed",After=after});return after;
    }
    private static void SaveJournal(string path,TagWriteJournal journal)
    {
        var temp=path+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(temp,JsonFormat.Serialize(journal),new System.Text.UTF8Encoding(false));File.Move(temp,path,true);
    }
}
