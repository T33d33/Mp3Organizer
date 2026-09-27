# Preserve original playlists after organization

Use the current `artifacts-review-scope/Mp3Organizer.dll` build (also selected by the main `.cmd` and `.ps1` launchers). Your workflow stays:

```powershell
dotnet $dll scan 'E:\Mp3-Ai-test' --workspace 'E:\Mp3-Ai-test-workspace'
dotnet $dll run --online --workspace 'E:\Mp3-Ai-test-workspace'
```

These commands have not been executed against your music. See [README.md](README.md) for DLL and API configuration.

## Output and permanent spoken codes

Existing source playlists are a separate category from generated Artist, Album and Folder playlists. Each discovered playlist receives a permanent workspace ID and the PL-coded filename:

```text
E:\Mp3-Ai-test-organized\
  _Playlist-Folder\
    [PL-0001] Christmas Mix.m3u8
    [PL-0002] Old Favorites.m3u8
  _Playlists\
    Artists\...
    Albums\...
    Folders\...
  playlist-index.csv
```

`PL-0001` distinguishes imported playlist codes from numeric Artist/Album codes. The code and original display name appear in the filename. `playlist-index.csv` gains entries with Type=`ImportedPlaylist` and Code=`PL-0001`. The new output directory must be included in your My Media library indexing. Playback/voice recognition on your actual Alexa/My Media installation was not tested here.

The existing progress database owns these identities; Artist/Album IDs in `playlist-map.json` are untouched. A different source path receives a different playlist ID, even when filenames match. Rescans, restarts, editing playlist contents, deleting a generated index, and progress reset-all preserve assigned playlist IDs. IDs are never reused. A unique SHA-256 match between one missing playlist and one newly discovered playlist preserves its ID across rename/relocation. Ambiguous or edited relocations are not guessed. OriginalName and OriginalPath remain recorded; CurrentPath updates. Unchanged entries retain their source-file bindings even when the moved playlist's relative paths would no longer resolve. Keep the same workspace.

## When paths are rewritten

1. `scan` traverses the complete source tree, collecting playlist files even in directories without direct audio. It reads and persists their ordered entries, binds them to indexed source-file identities where possible, and retains valid per-entry metadata without writing to the source.
2. Normal `run` resolves pending tracks and applies the existing organization/copy pipeline. Source audio remains in place; this feature does not move, rename, retag or delete it.
3. After successful organization and persisted canonical mappings, the final playlist regeneration step rebuilds imported playlists from verified target paths. No source/staging paths or remote URLs are emitted as playable entries.
4. Every subsequent run regenerates them, including when no audio needs copying. Editing/adding/removing source playlists requires the usual scan before run. Resolving an already indexed pending entry only requires the next run.

For example, source entries `..\Album A\song.mp3`, an absolute source path, and a `file:///` URI may all map to:

```text
..\Artist\2001 - Album\03 - Song.mp3
```

Paths are relative to `_Playlist-Folder`, use Windows backslashes, and refer to the final canonical target. Output is UTF-8 M3U8 without BOM. Input order and repeated entries are preserved. Identical audio shared across playlists is still copied only once. Original source playlists remain unchanged.

## Pending and unresolvable entries

Pending, NeedsReview, Error, Skipped, missing and ambiguous references are omitted from the playable output. Each output includes `#PLAYABLE` and `#OMITTED` counts, also printed during run. Entries remain persisted with their original positions and raw references. Once a track is resolved and organized, it returns at its original position on the next run.

Paths are matched case-insensitively against the indexed current source paths, or a unique known original path from a tracked external relocation. There is **no basename-only guessing**: a stale `D:\OldMusic\song.mp3` cannot be mapped safely merely because some `song.mp3` exists elsewhere. Correct stale source references and rescan when no recorded identity establishes the connection. Playlist entries never cause an external path or remote URL to be fetched.

Detailed UTF-8 CSV snapshots are written under:

```text
<workspace>\playlist-import-reports\<timestamp>-<unique-id>.csv
```

Columns: `Code`, `SourcePlaylist`, `PlaylistPath`, `Position`, `OriginalEntry`, `Result`, `TargetEntry`, `Category`. The report location is printed after regeneration. The database also retains the latest per-entry result. A read/parse error is reported and produces an empty managed playlist instead of playing a stale entry snapshot. A source playlist missing at the last complete scan likewise leaves an empty generated playlist and retains its ID. An incomplete directory scan does not mark unobserved playlists removed.

`status` reports source/migrated playlists, total/migrated occurrences, awaiting review, pending/error and missing/unresolved counts, plus read errors. Migrated means successfully generated (possibly partial); entry counts distinguish partial completion. Counts use current file states and the last successful regeneration, not a fresh target scan. Existing managed-file ownership checks and backups apply to `_Playlist-Folder`; a colliding user-owned target file is never silently replaced.

## Formats and limits

- M3U and M3U8: local paths, absolute paths and file URIs. `#EXTM3U`, valid `#EXTINF`, `#PLAYLIST`, track group/artist/album metadata and ordinary comments are preserved where valid. UTF-8 is preferred. Legacy M3U/PLS additionally accept UTF-16 with BOM and Windows-1252 fallback. For other legacy encodings, export UTF-8 first.
- PLS: numbered `FileN` entries in numeric order; duplicate entry numbers and inconsistent declared counts are rejected.
- WPL: XML media `src` entries.
- XSPF: track locations, including URI-escaped names; one location per track is required.
- ASX: XML `ref` locations.
- ZPL, B4S, FPL and CUE are discovered and reported as unsupported; export them to M3U8. Arbitrary other formats are not parsed.

Playlists are limited to 8 MiB. HLS streaming manifests, XML DTDs/external entities and malformed files are rejected. Stream URLs are reported and omitted. `#EXTINF` and associated comments stay with their occurrence and are omitted with an unresolved track, so they cannot accidentally describe the next playable entry. Unsupported/invalid directives (including old encoding declarations and player options with additional paths) are omitted and reported as metadata warnings. A single valid `#EXTM3U` header is generated. Nested playlist references are not expanded. This feature preserves playback entry sequence, not player-specific scheduling or playback commands.

## Persistence and verification

Progress schema **2/3 → 4** adds/extends `source_playlists` and `source_playlist_entries` transactionally without dropping existing data. It persists original/current playlist paths, original name, presence, content hash, header metadata, warnings, last successful generation, and ordered source-file identity bindings with entry metadata and outcome categories. Source-playlist IDs use SQLite AUTOINCREMENT. Scan updates of playlist presence and ordered entry snapshots are transactional. Folder IDs, occurrence records, metadata policy 2, manual decisions and existing progress histories remain intact.

`playlists <target> --workspace <same-workspace>` also regenerates imported lists. Without the original workspace, it can preserve existing generated lists but cannot reconstruct source playlist membership. `run` invokes regeneration automatically after organization; no extra command is needed. Lower-level plan/apply preserves already generated imported lists; use run or playlists with the workspace for the final regeneration step.

The complete test suite covers final path rewriting, all six parsed formats, Unicode and legacy encoding, repeated/deduplicated tracks, delayed resolution, no-copy updates, stable IDs and database migration, unsafe XML/remote references, no basename guessing, source immutability, and unmanaged target collisions. Tests use synthetic files and mocked services only.


See [the verified synthetic MP3 example](PLAYLIST-EXAMPLE.md) for actual source paths → persisted identities → verified target paths → rewritten M3U8 output from the test suite.




Updated destination: imported source playlists now live under _Playlists/Original Playlists. _Playlists/Folders continues to represent source-folder membership. Normal run regenerates imported playlists with the deeper relative paths, retains permanent codes, backs up/removes corresponding managed legacy _Playlist-Folder copies, and removes that directory only if empty. Unmanaged files are preserved.
