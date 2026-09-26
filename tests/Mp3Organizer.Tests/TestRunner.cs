using System.Security.Cryptography;
using System.Text;
using Mp3Organizer;

namespace Mp3Organizer.Tests;

public static partial class TestRunner
{
    private static string root = "";
    public static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--progress-probe")
        {
            using var progress=new ProgressRepository(args[1]);return progress.All().Count==1&&progress.All()[0].Status==ProcessingStatus.Ready?0:1;
        }
        if(args.Length==3&&args[0]=="--cache-probe")
        {
            var value=new IdentificationCache(args[1],args[2]).Get("chromaprint-v1-length120:"+new string('A',64));
            if(value==null)return 1;Console.WriteLine("cache-persisted");return 0;
        }
        root = Path.Combine(args.Length == 0 ? Path.GetTempPath() : Path.GetFullPath(args[0]), "mp3tests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("MP3ORGANIZER_WORKSPACE",Path.Combine(root,"workspace"));
        var tests = new Action[]
        {
            ProgressSeparateProcess, ProgressRecorderIntegration, ProgressBackupFailureSafe, ProgressLimitAndRestart, ProgressProcessedNeverRepeated, ProgressScanPreservesAndSkipsReads, ProgressResetFileAndFolder, ProgressErrorsAndReviewReset, ProgressBackupBeforeReset, ProgressRenameIdentityAndDuplicates, ProgressEachFileCommitted, ProgressCliAndSourceSafety, NavigateBackAcrossFolders, BackspaceEditsInsteadOfNavigating, EveryPromptHasPathContext, NavigationBackAndEdit, NavigationFirstFieldReturnsToSelection, NavigationFileContextAndPersistence, ArtistAlbumTrackFilename, GenericFilenameTrackHints, GenericFilenameInteractiveDefault, CupheadTrackNumbers, TrackHintWithReliableTitle, InteractiveTrackHintAccepted, PartialOnlinePreservesFields, PartialFieldPriorityAndPlaceholders, FieldSourcesReport, IdentificationStageProgress, DiagnosticOnlineConditions, DiagnosticFpcalcLocation, DoctorChecksWorkspaceOnly, DoctorFailuresReported, PlaceholderVariants, KingpinOfflineAndReport, KingpinOnlineAndFailure, KingpinInteractiveAndManualPriority, PlaceholderOldCacheInvalidated, NormalizeWhitespace, NormalizeUnicode, PreservePunctuation, NormalizeBlank,
            DuplicateBoundary, DuplicateNoChains, DuplicateMissingTags, DuplicateDifferentArtist,
            NonIdenticalRetained, IdenticalSelected, StableDuplicateOrdering,
            SafeNames, CompilationPath, LoosePath, DiscPath,
            StableArtistIdentity, PermanentIds, CompilationPlaylists, AlbumSpokenCode, AlbumLimit,
            SourceStreamReadOnly, TagWriteStreamDenied, WriterRejectsSource, ReportsRejectSource,
            PlanRejectsOverlap, TraversalRejected, ProtectedSourceRejected, ApplyRequiresPlan,
            EndToEndSourceImmutable, DryRunNoWrites, ChangedSourceRejected, ChangedTargetRejected,
            CorruptAudioBlocksPlan, ExistingUnmanagedTargetRejected, PlanTamperRejected,
            TargetCollisionPreserved, PlaylistUtf8AndRelative, CsvEscaping, RegenerationKeepsIds,
            ReportFilesGenerated, MappingRollbackRejected, MissingMappingRejected, MultipleTrackArtists,
            AddedSourceRejected, ChangedSkippedDuplicateRejected, CopyExecutorRejectsSource,
            PlaylistWriterRejectsSource, PlanStoreRejectsSource, TagSaveDenied,
            LosslessQualityPreference, SameCodecBitratePreference, UnknownQualityRetained,
            EmptyRetiredPlaylists, SourcePathAliasesRejected, ScannerExtensions, CliWorkflow,
            IndexStableCodesAndNumericOrder, IndexRegenerationPreservesMapping, IndexAlbumReportCode,
            IndexCompilationAndUtf8, IndexUnmanagedCollision,
            ResolutionTagsPriority, ResolutionForcedPreservesTags, ResolutionLowScore, ResolutionAmbiguous,
            ResolutionReleaseSelection, ResolutionReleaseAmbiguous, ResolutionNoArbitraryCompilation,
            ResolutionNeighborAlbum, ResolutionGoodTagConflict, ResolutionDurationConflict,
            FallbackNoiseAndSoundtrack, QualityTriggers, OfflineNeverCallsHttp, FingerprintCacheReuse,
            HttpRateLimits, HttpRetryBackoff, HttpNegativeCache, HttpTransientFailureCache,
            HttpPayloadPrivacy, HttpMusicBrainzParse, HttpMalformedResponse, HttpMusicBrainzFailure,
            RecordingIdentityDuplicates, ResolutionSourceImmutable, TargetEffectiveMetadataPersists,
            IdentificationReportFields, IdentificationCacheSafety, IdentifyOfflineCli,
            ResolutionCacheReusedOffline, HttpMusicBrainzPagination, NeighborYearSelectsRelease,
            LosslessPropertyPreference, UncertainQualityKeepsBoth, ResolutionPreservesGoodFieldSpelling, FingerprintRejectsChangedSource,
            SqlitePersistenceAndSchema, SqliteNegativeExpiry, SqliteFailureRetry, SqliteSuccessfulIndefinite,
            SqliteForcedRefresh, FingerprintForcedRebuild, FingerprintChangedFileNewIdentity,
            ManualFolderPersistence, ManualFilePersistence, ManualPriority, ManualFileOverFolder,
            ManualFolderPropagationAndChanges, ManualFolderRelocation, ManualSkipFolder,
            ManualAbortTracksKeepsProgress, ManualAcceptDefaultsNoRepeat, ManualEditExisting,
            ManualDirectEditReload, ManualInvalidJson, ManualSchemaValidation, ManualAtomicBackup,
            ManualConcurrentEditRefused, ManualValidationReferences, ManualSourceImmutable,
            ManualStalePlanRejected, ManualTargetPlaylistRegeneration, ManualReportsFlags, AutomaticCacheRenameReuse,
            ManualAbortMidTrackNoRepeatedTitle, ManualRefreshCannotOverride, ManualRemovalRestoresAutomatic,
            ManualValidateCommand, ManualAcceptsUtf8Bom, AutomaticNegativeResultPersistence, SqlitePersistenceAcrossProcesses, LegacyFingerprintImport
        };
        var failed = 0;
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Method.Name + ": " + e); }
        }
        Console.WriteLine($"RESULT: {tests.Length - failed} passed, {failed} failed, {tests.Length} total. Synthetic fixtures: {root}");
        return failed == 0 ? 0 : 1;
    }
    static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static AudioMetadata Row(string path = "a.wav", string artist = "Artist", string title = "Song", double duration = 100, string hash = "") => new() { FullPath = path, FileName = Path.GetFileName(path), Artist = artist, Title = title, DurationSeconds = duration, Extension = ".wav", Sha256 = hash, Size = 10 };
    static CopyOperation Op(AudioMetadata row) => new(row, TargetPathBuilder.Build(row), "Copy");
    static DuplicateDetector Detector() => new(new Sha256EquivalenceVerifier());
    static void NormalizeWhitespace() => Equal("MONIKA BRODKA", MetadataNormalizer.Key("  Monika\t\nBrodka  "));
    static void NormalizeUnicode() { Equal(MetadataNormalizer.Key("Björk"), MetadataNormalizer.Key("Bjo\u0308rk")); Equal("ABC", MetadataNormalizer.Key("ＡＢＣ")); }
    static void PreservePunctuation() => True(MetadataNormalizer.Key("Live!") != MetadataNormalizer.Key("Live"));
    static void NormalizeBlank() { Equal("", MetadataNormalizer.Key(null)); Equal("", MetadataNormalizer.Key(" \t")); }
    static void DuplicateBoundary() => Equal(1, Detector().CandidateGroups([Row(), Row("b.wav", duration: 103)]).Count);
    static void DuplicateNoChains() { var groups = Detector().CandidateGroups([Row(), Row("b.wav", duration: 103), Row("c.wav", duration: 106)]); Equal(1, groups.Count); Equal(2, groups[0].Count); }
    static void DuplicateMissingTags() => Equal(0, Detector().CandidateGroups([Row(artist: ""), Row("b.wav", artist: ""), Row(duration: 0)]).Count);
    static void DuplicateDifferentArtist() => Equal(0, Detector().CandidateGroups([Row(), Row("b.wav", artist: "Other")]).Count);
    static void NonIdenticalRetained() => Equal(2, Detector().Select([Row(hash: new('A',64)), Row("b.wav", hash: new('B',64))]).Selected.Count);
    static void IdenticalSelected() => Equal(1, Detector().Select([Row(hash: new('A',64)), Row("b.wav", hash: new('A',64))]).Selected.Count);
    static void StableDuplicateOrdering() => Equal("a.wav", Detector().Select([Row("b.wav", hash: new('A',64)), Row(hash: new('A',64))]).Selected.Single().FullPath);
    static void SafeNames() { Equal("_CON", TargetPathBuilder.SafeName("CON")); Equal("a_b_c", TargetPathBuilder.SafeName("a/b:c")); }
    static void CompilationPath() => True(TargetPathBuilder.Build(Row() with { AlbumArtist = "Various Artists", Album = "Mix", Year = 2001 }).StartsWith("Various Artists" + Path.DirectorySeparatorChar));
    static void LoosePath() => Equal(Path.Combine("Artist", "Song.wav"), TargetPathBuilder.Build(Row()));
    static void DiscPath() => True(TargetPathBuilder.Build(Row() with { Album = "Album", Disc = 2, Track = 3 }).Contains("D02 - 03 - Song"));
    static void StableArtistIdentity()
    {
        var map = new PlaylistMap(); var builder = new PlaylistBuilder();
        builder.Build([Op(Row(artist: "Björk"))], map);
        builder.Build([Op(Row(artist: "BJO\u0308RK"))], map);
        Equal(1, map.Artists.Count); Equal("Björk", map.Artists.Values.Single().DisplayName);
    }
    static void PermanentIds()
    {
        var map = new PlaylistMap(); var builder = new PlaylistBuilder();
        builder.Build([Op(Row() with { Album = "Old" })], map);
        builder.Build([], map);
        builder.Build([Op(Row(artist: "New Artist")), Op(Row() with { Album = "New" })], map);
        Equal(1, map.Artists["ARTIST"].ArtistId); Equal(2, map.Artists["NEW ARTIST"].ArtistId);
        Equal(1, map.Artists["ARTIST"].Albums["OLD|0"].AlbumId); Equal(2, map.Artists["ARTIST"].Albums["NEW|0"].AlbumId);
    }
    static void CompilationPlaylists()
    {
        var lists = new PlaylistBuilder().Build([Op(Row(artist: "Singer") with { AlbumArtist = "Various Artists", Album = "Mix" }), Op(Row("b.wav", artist: "Various Artists", title: "Other") with { Album = "Mix" })], new());
        True(!lists.Any(x => x.RelativePath.Contains("Artists" + Path.DirectorySeparatorChar) && x.RelativePath.Contains("Various Artists")));
        True(lists.Any(x => x.RelativePath.Contains("Singer") && x.Content.Contains("Various Artists")));
        Equal(1, lists.Count(x => x.RelativePath.Contains("Albums" + Path.DirectorySeparatorChar)));
    }
    static void AlbumSpokenCode()
    {
        var map = new PlaylistMap { NextArtistId = 124, Artists = new() { ["MONIKA BRODKA"] = new() { ArtistId = 123, DisplayName = "Monika Brodka" } } };
        var lists = new PlaylistBuilder().Build([Op(Row(artist: "Monika Brodka") with { Album = "Granda" })], map);
        True(lists.Any(x => Path.GetFileName(x.RelativePath) == "Code 12301 - Monika Brodka - Granda.m3u8"));
    }
    static void AlbumLimit()
    {
        var map = new PlaylistMap { NextArtistId = 2, Artists = new() { ["ARTIST"] = new() { ArtistId = 1, DisplayName = "Artist", NextAlbumId = 100 } } };
        Throws<IOException>(() => new PlaylistBuilder().Build([Op(Row() with { Album = "New" })], map));
    }
    static void SourceStreamReadOnly()
    {
        var f = Fixture(); using var stream = new ReadOnlySource().OpenRead(f.File);
        True(!stream.CanWrite); Throws<NotSupportedException>(() => stream.WriteByte(0));
    }
    static void TagWriteStreamDenied() { var f = Fixture(); Throws<NotSupportedException>(() => { using var stream = new ReadOnlyTagFile(f.File, new ReadOnlySource()).WriteStream; }); }
    static void WriterRejectsSource() { var f = Fixture(); Throws<IOException>(() => new M3u8Writer().WriteNew(Path.Combine(f.Source,"new.m3u8"), "bad", f.Source)); }
    static void ReportsRejectSource() { var f = Fixture(); Throws<IOException>(() => new CsvReportWriter().Write(Path.Combine(f.Source,"reports"), f.Source, [], null)); }
    static void PlanRejectsOverlap() { var f = Fixture(); Throws<IOException>(() => new CopyPlanBuilder().Build(f.Source, Path.Combine(f.Source,"out"))); Throws<IOException>(() => new CopyPlanBuilder().Build(f.Source, Path.GetDirectoryName(f.Source)!)); }
    static void TraversalRejected() { var f = Fixture(); Throws<IOException>(() => PathSafetyGuard.Destination(f.Target,"../source/a.wav", f.Source)); }
    static void ProtectedSourceRejected() => Throws<IOException>(() => PathSafetyGuard.Writable(PathSafetyGuard.ProtectedSource + @"\test.txt"));
    static void ApplyRequiresPlan() { var f = Fixture(); Throws<IOException>(() => new CopyPlanStore().Latest(f.Reports, f.Source, f.Target)); }
    static void EndToEndSourceImmutable()
    {
        var f = Fixture();
        File.SetAttributes(f.File, File.GetAttributes(f.File) | FileAttributes.ReadOnly);
        var before = Snapshot(f.Source);
        var plan = new CopyPlanBuilder().Build(f.Source, f.Target); Equal(0, plan.Conflicts.Count);
        Equal("Test Album", plan.Library.Single().Album); Equal("Test Artist", plan.Library.Single().Artist); Equal(2, plan.Playlists.Count);
        var path = new CopyPlanStore().Save(plan, f.Reports);
        new CsvReportWriter().Write(Path.GetDirectoryName(path)!, f.Source, plan.Library, plan);
        new SafeCopyExecutor().Execute(new CopyPlanStore().Load(path), f.Source, f.Target, false);
        Equal(before, Snapshot(f.Source));
        True((File.GetAttributes(f.File) & FileAttributes.ReadOnly) != 0);
        var copied = new AudioFileScanner().Scan(f.Target); Equal(1,copied.Count);
        Equal(Hash(f.File), Hash(copied[0]));
        var next = new CopyPlanBuilder().Build(f.Source,f.Target); Equal("AlreadyPresent",next.Operations.Single().Action);
    }
    static void DryRunNoWrites()
    {
        var f = Fixture(); var p = new CopyPlanBuilder().Build(f.Source,f.Target); var before = Snapshot(f.Source);
        new SafeCopyExecutor().Execute(p,f.Source,f.Target,true); True(!Directory.Exists(f.Target)); Equal(before,Snapshot(f.Source));
    }
    static void ChangedSourceRejected()
    {
        var f = Fixture(); var p = new CopyPlanBuilder().Build(f.Source,f.Target);
        using(var stream = new FileStream(f.File,FileMode.Append)) stream.WriteByte(42);
        Throws<IOException>(() => new SafeCopyExecutor().Execute(p,f.Source,f.Target,false)); True(!Directory.Exists(f.Target));
    }
    static void ChangedTargetRejected()
    {
        var f = Fixture(); var p = new CopyPlanBuilder().Build(f.Source,f.Target); Directory.CreateDirectory(f.Target); File.WriteAllText(Path.Combine(f.Target,"user.txt"),"keep");
        Throws<IOException>(() => new SafeCopyExecutor().Execute(p,f.Source,f.Target,false)); Equal("keep",File.ReadAllText(Path.Combine(f.Target,"user.txt")));
    }
    static void CorruptAudioBlocksPlan()
    {
        var f = Fixture(); File.WriteAllText(Path.Combine(f.Source,"broken.mp3"),"broken"); var p = new CopyPlanBuilder().Build(f.Source,f.Target);
        True(p.Conflicts.Count > 0); Throws<IOException>(() => new SafeCopyExecutor().Execute(p,f.Source,f.Target,false));
    }
    static void ExistingUnmanagedTargetRejected() { var f = Fixture(); Directory.CreateDirectory(f.Target); File.WriteAllText(Path.Combine(f.Target,"keep"),"x"); Throws<IOException>(() => new CopyPlanBuilder().Build(f.Source,f.Target)); }
    static void PlanTamperRejected() { var f=Fixture(); var path=new CopyPlanStore().Save(new CopyPlanBuilder().Build(f.Source,f.Target),f.Reports); File.AppendAllText(path," "); Throws<IOException>(()=>new CopyPlanStore().Load(path)); }
    static void TargetCollisionPreserved()
    {
        var f = Fixture(); File.Copy(f.File,Path.Combine(f.Source,"second.wav"));
        using(var s = new FileStream(Path.Combine(f.Source,"second.wav"),FileMode.Open,FileAccess.Write)) { s.Position = s.Length-1; s.WriteByte(12); }
        var p = new CopyPlanBuilder().Build(f.Source,f.Target); Equal(2,p.Operations.Count); Equal(2,p.Operations.Select(x=>x.RelativePath).Distinct().Count()); Equal(1,p.ResolvedConflicts.Count);
        new SafeCopyExecutor().Execute(p,f.Source,f.Target,false); Equal(2,new AudioFileScanner().Scan(f.Target).Count);
    }
    static void PlaylistUtf8AndRelative()
    {
        var f=Fixture(); var p=new CopyPlanBuilder().Build(f.Source,f.Target); new SafeCopyExecutor().Execute(p,f.Source,f.Target,false);
        foreach(var list in p.Playlists)
        {
            var path=Path.Combine(f.Target,list.RelativePath); var bytes=File.ReadAllBytes(path); True(!(bytes.Length>=3 && bytes[0]==239 && bytes[1]==187 && bytes[2]==191));
            foreach(var line in File.ReadAllLines(path).Where(x=>x.Length>0 && !x.StartsWith('#'))) { True(!Path.IsPathRooted(line)); True(!line.Contains('/')); True(line.StartsWith(@"..\..\")); True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,line)))); }
        }
    }
    static void CsvEscaping() { var f=Fixture(); new CsvReportWriter().Write(f.Reports,f.Source,[Row(title:"A, \"quote\"\nline")],null); True(File.ReadAllText(Path.Combine(f.Reports,"library.csv")).Contains("\"A, \"\"quote\"\"\nline\"")); }
    static void RegenerationKeepsIds()
    {
        var f=Fixture(); var p=new CopyPlanBuilder().Build(f.Source,f.Target); new SafeCopyExecutor().Execute(p,f.Source,f.Target,false); var before=File.ReadAllText(Path.Combine(f.Target,"playlist-map.json"));
        Equal(0,Mp3Organizer.Program.Run(["playlists",f.Target])); Equal(before,File.ReadAllText(Path.Combine(f.Target,"playlist-map.json")));
    }
    static void ReportFilesGenerated()
    {
        var f=Fixture(); var p=new CopyPlanBuilder().Build(f.Source,f.Target); var path=new CopyPlanStore().Save(p,f.Reports); var folder=Path.GetDirectoryName(path)!; new CsvReportWriter().Write(folder,f.Source,p.Library,p);
        foreach(var name in new[]{"library.csv","missing-tags.csv","copy-plan.csv","duplicates.csv","conflicts.csv","albums.csv"}) True(File.Exists(Path.Combine(folder,name)));
    }
    static string Hash(string path) { using var s=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    static string Snapshot(string folder) => string.Join("\n",Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Order().Select(x=>Path.GetRelativePath(folder,x)+":"+Hash(x)+":"+File.GetLastWriteTimeUtc(x).Ticks+":"+File.GetAttributes(x)));
    static void MappingRollbackRejected()
    {
        var old=new PlaylistMap();new PlaylistBuilder().Build([Op(Row() with {Album="Album"})],old);
        Throws<IOException>(()=>PlaylistMapStore.ValidateExtension(old,new()));
        var changed=JsonFormat.Read<PlaylistMap>(JsonFormat.Serialize(old));changed.Artists["ARTIST"].Albums.Clear();
        Throws<IOException>(()=>PlaylistMapStore.ValidateExtension(old,changed));
    }
    static void MissingMappingRejected()
    {
        var f=Fixture();Directory.CreateDirectory(f.Target);File.WriteAllText(Path.Combine(f.Target,ManagedTarget.Marker),JsonFormat.Serialize(new ManagedTargetState(1,f.Source,[])));
        Throws<IOException>(()=>new PlaylistMapStore().Load(f.Target));
    }
    static void MultipleTrackArtists()
    {
        var lists=new PlaylistBuilder().Build([Op(Row() with {TrackArtists=["One","Two"],AlbumArtist="Various Artists",Album="Mix"})],new());
        Equal(2,lists.Count(x=>x.RelativePath.Contains("Artists"+Path.DirectorySeparatorChar)));Equal(1,lists.Count(x=>x.RelativePath.Contains("Albums"+Path.DirectorySeparatorChar)));
    }
    static void AddedSourceRejected()
    {
        var f=Fixture();var p=new CopyPlanBuilder().Build(f.Source,f.Target);File.Copy(f.File,Path.Combine(f.Source,"new.wav"));
        Throws<IOException>(()=>new SafeCopyExecutor().Execute(p,f.Source,f.Target,true));
    }
    static void ChangedSkippedDuplicateRejected()
    {
        var f=Fixture();var duplicate=Path.Combine(f.Source,"z.wav");File.Copy(f.File,duplicate);var p=new CopyPlanBuilder().Build(f.Source,f.Target);Equal(1,p.Operations.Count);
        using(var s=new FileStream(duplicate,FileMode.Append))s.WriteByte(1);
        Throws<IOException>(()=>new SafeCopyExecutor().Execute(p,f.Source,f.Target,false));True(!Directory.Exists(f.Target));
    }
    static void CopyExecutorRejectsSource()
    {
        var f=Fixture();var before=Snapshot(f.Source);var p=new CopyPlan {Source=f.Source,Target=f.Source};
        Throws<IOException>(()=>new SafeCopyExecutor().Execute(p,f.Source,f.Source,false));Equal(before,Snapshot(f.Source));
    }
    static void PlaylistWriterRejectsSource()
    {
        var f=Fixture();var before=Snapshot(f.Source);Throws<IOException>(()=>new ManagedPlaylistWriter().Write(f.Source,f.Source,[],new()));Equal(before,Snapshot(f.Source));
    }
    static void PlanStoreRejectsSource()
    {
        var f=Fixture();var before=Snapshot(f.Source);Throws<IOException>(()=>new CopyPlanStore().Save(new CopyPlan{Source=f.Source,Target=f.Target},f.Source));Equal(before,Snapshot(f.Source));
    }
    static void TagSaveDenied()
    {
        var f=Fixture();var before=Snapshot(f.Source);using(var file=TagLib.File.Create(new ReadOnlyTagFile(f.File,new ReadOnlySource()))) {file.Tag.Title="Forbidden";Throws<NotSupportedException>(()=>file.Save());}Equal(before,Snapshot(f.Source));
    }
    static void LosslessQualityPreference() => Equal<int?>(1,new AudioQualityComparer().Compare(Row() with {Lossless=true},Row() with {Lossless=false}));
    static void SameCodecBitratePreference() => Equal<int?>(1,new AudioQualityComparer().Compare(Row() with {Lossless=false,CodecFamily="MP3",Bitrate=320},Row() with {Lossless=false,CodecFamily="MP3",Bitrate=128}));
    static void UnknownQualityRetained() => Equal<int?>(null,new AudioQualityComparer().Compare(Row(),Row()));
    static void EmptyRetiredPlaylists()
    {
        var lists=new List<PlaylistDefinition>();PlaylistBuilder.IncludeRetired(lists,new ManagedTargetState(1,"source",[Path.Combine("_Playlists","Artists","Code 1 - Old.m3u8")]));Equal("#EXTM3U\n",lists.Single().Content);
    }
    static void SourcePathAliasesRejected()
    {
        var f=Fixture();Throws<IOException>(()=>PathSafetyGuard.Writable(f.Source+". ",f.Source));Throws<IOException>(()=>PathSafetyGuard.Writable(f.File+":stream",f.Source));Throws<IOException>(()=>PathSafetyGuard.Writable(@"\\?\UNC\192.168.0.124\Public\mp3\x"));
    }
    static void ScannerExtensions()
    {
        var f=Fixture();foreach(var ext in new[]{".MP3",".flac",".m4a",".aac",".ogg",".wma",".txt"})File.WriteAllText(Path.Combine(f.Source,"test"+ext),"");Equal(7,new AudioFileScanner().Scan(f.Source).Count);
    }
    static void CliWorkflow()
    {
        var f=Fixture();var before=Snapshot(f.Source);
        Equal(0,Mp3Organizer.Program.Run(["analyze",f.Source,"--reports",f.Reports]));
        Equal(0,Mp3Organizer.Program.Run(["plan",f.Source,f.Target,"--reports",f.Reports]));True(!Directory.Exists(f.Target));
        Equal(0,Mp3Organizer.Program.Run(["apply",f.Source,f.Target,"--reports",f.Reports,"--dry-run"]));True(!Directory.Exists(f.Target));
        Equal(0,Mp3Organizer.Program.Run(["apply",f.Source,f.Target,"--reports",f.Reports]));Equal(before,Snapshot(f.Source));
    }
    static void IndexStableCodesAndNumericOrder()
    {
        var map = new PlaylistMap { NextArtistId = 124, Artists = new()
        {
            ["MONIKA BRODKA"] = new() { ArtistId = 123, DisplayName = "Monika Brodka", NextAlbumId = 12, Albums = new()
                { ["LATER|0"] = new() { AlbumId = 11, DisplayName = "Later" }, ["GRANDA|0"] = new() { AlbumId = 1, DisplayName = "Granda" } } },
            ["EARLY"] = new() { ArtistId = 2, DisplayName = "Early", NextAlbumId = 2, Albums = new() { ["FIRST|0"] = new() { AlbumId = 1, DisplayName = "First" } } }
        }};
        var before = JsonFormat.Serialize(map);
        var playlists = map.Artists.Values.SelectMany(a => new[] { new PlaylistDefinition(PlaylistNames.ArtistPath(a), "#EXTM3U\n") }.Concat(a.Albums.Values.Select(b => new PlaylistDefinition(PlaylistNames.AlbumPath(a,b), "#EXTM3U\n")))).Reverse().ToList();
        var rows = new PlaylistIndexBuilder().Build(map, playlists);
        Equal("2,123,201,12301,12311", string.Join(",", rows.Select(x => x.Code)));
        Equal("Granda", rows.Single(x => x.Code == "12301").Album);
        Equal(@"_Playlists\Albums\Code 12301 - Monika Brodka - Granda.m3u8", rows.Single(x => x.Code == "12301").PlaylistPath);
        Equal(before, JsonFormat.Serialize(map));
    }
    static void IndexRegenerationPreservesMapping()
    {
        var f = Fixture(); var before = Snapshot(f.Source);
        var p = new CopyPlanBuilder().Build(f.Source, f.Target);
        new SafeCopyExecutor().Execute(p, f.Source, f.Target, false);
        var path = Path.Combine(f.Target, PlaylistIndexBuilder.FileName);
        var original = File.ReadAllText(path); var map = File.ReadAllText(Path.Combine(f.Target,"playlist-map.json"));
        File.Move(path, Path.Combine(f.Target,"saved-index.csv"));
        Equal(0, Mp3Organizer.Program.Run(["playlists", f.Target]));
        Equal(original, File.ReadAllText(path)); Equal(map, File.ReadAllText(Path.Combine(f.Target,"playlist-map.json")));
        File.WriteAllText(path,"stale index");
        Equal(0, Mp3Organizer.Program.Run(["playlists", f.Target]));
        Equal(original, File.ReadAllText(path)); Equal(map, File.ReadAllText(Path.Combine(f.Target,"playlist-map.json")));
        Equal(before, Snapshot(f.Source));
    }
    static void IndexAlbumReportCode()
    {
        var f = Fixture(); var p = new CopyPlanBuilder().Build(f.Source, f.Target);
        new CsvReportWriter().Write(f.Reports, f.Source, p.Library, p);
        var album = new PlaylistIndexBuilder().Build(p.PlaylistMap,p.Playlists).Single(x => x.Type == "Album");
        var lines = File.ReadAllLines(Path.Combine(f.Reports,"albums.csv"));
        True(lines[0].EndsWith("\"Code\"")); True(lines[1].EndsWith("\"" + album.Code + "\""));
        True(File.ReadAllText(Path.Combine(f.Reports,PlaylistIndexBuilder.FileName)).Contains("\"" + album.Code + "\""));
        var analysis = Path.Combine(f.Reports,"analysis"); new CsvReportWriter().Write(analysis,f.Source,p.Library,null);
        True(File.ReadAllLines(Path.Combine(analysis,"albums.csv"))[1].EndsWith(",\"\""));
    }
    static void IndexCompilationAndUtf8()
    {
        var map = new PlaylistMap();
        var lists = new PlaylistBuilder().Build([Op(Row(artist: "Björk, \"live\"") with { AlbumArtist = "Various Artists", Album = "Mix" })],map);
        var builder = new PlaylistIndexBuilder(); var rows = builder.Build(map,lists);
        True(!rows.Any(x => x.Type == "Artist" && x.Artist == "Various Artists"));
        True(rows.Any(x => x.Type == "Album" && x.Artist == "Various Artists"));
        var f = Fixture(); var path = Path.Combine(f.Reports,PlaylistIndexBuilder.FileName);
        new M3u8Writer().WriteNew(path,builder.Csv(map,lists),f.Source);
        var bytes = File.ReadAllBytes(path); True(!bytes.Take(3).SequenceEqual(new byte[]{239,187,191}));
        True(new UTF8Encoding(false,true).GetString(bytes).Contains("\"Björk, \"\"live\"\"\""));
    }
    static void IndexUnmanagedCollision()
    {
        var f = Fixture(); var p = new CopyPlanBuilder().Build(f.Source,f.Target); new SafeCopyExecutor().Execute(p,f.Source,f.Target,false);
        // Simulate an older managed target containing a user-owned index file.
        File.WriteAllText(Path.Combine(f.Target,ManagedTarget.Marker), JsonFormat.Serialize(new ManagedTargetState(1,f.Source,p.Playlists.Select(x => x.RelativePath).ToList())));
        var path = Path.Combine(f.Target,PlaylistIndexBuilder.FileName); File.WriteAllText(path,"user owned");
        Throws<IOException>(() => Mp3Organizer.Program.Run(["playlists",f.Target])); Equal("user owned",File.ReadAllText(path));
    }
    sealed record FixturePaths(string Source,string Target,string Reports,string File);
    static FixturePaths Fixture()
    {
        var folder=Path.Combine(root,Guid.NewGuid().ToString("N")); var source=Path.Combine(folder,"source"); Directory.CreateDirectory(source); var file=Path.Combine(source,"test.wav");
        using(var stream=File.Create(file))
        using(var writer=new BinaryWriter(stream,Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(0); writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            Chunk(writer,"fmt ",Build(w=>{w.Write((short)1);w.Write((short)1);w.Write(8000);w.Write(16000);w.Write((short)2);w.Write((short)16);}));
            Chunk(writer,"LIST",Build(w=>{w.Write(Encoding.ASCII.GetBytes("INFO"));Chunk(w,"IART",Encoding.ASCII.GetBytes("Test Artist\0"));Chunk(w,"ISTR",Encoding.ASCII.GetBytes("Test Artist\0"));Chunk(w,"INAM",Encoding.ASCII.GetBytes("Test Song\0"));Chunk(w,"DIRC",Encoding.ASCII.GetBytes("Test Album\0"));Chunk(w,"ICRD",Encoding.ASCII.GetBytes("2020\0"));}));
            Chunk(writer,"data",new byte[16000]); writer.Seek(4,SeekOrigin.Begin); writer.Write((int)stream.Length-8);
        }
        return new(source,Path.Combine(folder,"target"),Path.Combine(folder,"reports"),file);
    }
    static byte[] Build(Action<BinaryWriter> action) { using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream); action(writer); writer.Flush(); return stream.ToArray(); }
    static void Chunk(BinaryWriter writer,string name,byte[] data) { writer.Write(Encoding.ASCII.GetBytes(name)); writer.Write(data.Length); writer.Write(data); if(data.Length%2!=0)writer.Write((byte)0); }
}
