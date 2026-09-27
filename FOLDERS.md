# Folder playlists and metadata policy migration

Use `artifacts-review-scope/Mp3Organizer.dll`. The normal workflow remains:

```powershell
dotnet $dll scan 'E:\Mp3-Ai-test' --workspace 'E:\Mp3-Ai-test-workspace'
dotnet $dll run --online --workspace 'E:\Mp3-Ai-test-workspace'
```

These example commands were not executed against your library. See [README.md](README.md) for the full DLL path and API configuration.

## Two independent concepts

Resolved track metadata determines the physical Artist / Album / Track destination and the Artist/Album playlists. Embedded tags, filename evidence, verified online candidates and explicit manual decisions continue to feed the existing resolver. A source folder called Christmas, Party, Electro or Old Music never supplies Artist, Album, Year or TrackNumber. Neighboring tracks no longer vote an album or year into another track. Filename parsing still suggests titles and track numbers. Folder text can contribute weak supporting evidence to factual online candidates, but cannot itself provide metadata. LLM instructions explicitly enforce this distinction.

Original source-folder grouping is preserved independently in `_Playlists\Folders`. There are no folder classification questions. Files lacking sufficient metadata remain NeedsReview; the folder name is never used to manufacture a complete album identity.

## Permanent identities and occurrences

The existing workspace `music-organizer.db` is the authority for folder IDs, occurrences, order and canonical target mappings. `playlist-map.json` continues to own Artist/Album IDs; no second processing-state JSON is introduced. Explicit manual overrides remain in the existing `manual-resolutions.json`.

Every folder directly containing supported audio receives an SQLite AUTOINCREMENT identity, displayed as `FOLDER-0001`. The width is a minimum, not a limit. Rows and ID allocations are retained, including after folders disappear or progress is reset. Case-insensitive canonical root/path identity distinguishes two folders named Old Music in different locations. Scans and application restarts reuse the same rows. An externally renamed folder is treated as a new folder identity; the program does not guess that identically named folders are the same collection.

Container folders without direct audio do not receive recursive playlists. Supported audio includes MP3 and the existing FLAC/M4A/AAC/OGG/WAV/WMA formats. A source root containing direct audio is itself meaningful and receives a playlist.

Each physical source path is a persisted occurrence, separate from its content identity. A complete scan marks missing occurrences absent without deleting them. Incomplete directory scans retain unobserved memberships conservatively. New files join their direct parent's existing Folder ID. Reset-all retains folder identities and occurrences; a subsequent scan reconnects occurrences to the rebuilt file index. Preserve/backup this workspace to preserve these IDs; creating a different workspace starts a different identity registry.

## Playback ordering

A scan accepts an existing order when exactly one direct `.m3u` or `.m3u8` file is valid UTF-8, at most 1 MiB, and every playable line resolves to an indexed audio file directly in that folder. Blank lines and `#` comments are ignored. Remote, unknown, outside-folder, malformed, empty or multiple playlists cause a diagnostic and natural-order fallback. No source playlist is modified.

Repeated entries in a trustworthy playlist are retained. Unlisted direct members are appended in natural filename/path order. Without a trustworthy playlist the entire folder uses natural order: `1.mp3`, `2.mp3`, `10.mp3`. Ties are deterministic. Order positions reference occurrence IDs and may reference the same occurrence repeatedly. Two physical source files with identical content remain two distinct occurrences.

## Copying, deduplication and regeneration

The existing plan builder and guarded copy executor are reused. SHA-256 plus size prove byte identity; recording IDs, similar titles and fingerprints alone do not authorize deduplication. A later batch also compares against verified existing target audio, reusing its canonical path instead of creating another physical copy. The first verified canonical copy retains its destination and effective metadata; a different spelling on a later identical source does not silently rename it.

After successful validated apply, `canonical_mappings` records source-file ID, source hash, target root, relative target path, target hash and size. Each fulfilled Ready source becomes Processed, including verified duplicates. Older Processed copies can be mapped only when their metadata passes migration and target bytes match. No source path is ever used as a playable destination.

Folder playlists are regenerated at the end of each normal run, including runs with zero Ready files or zero audio copies. A membership/order change detected by scan therefore updates playlists on the next run. Regeneration includes only present Processed occurrences with current policy metadata and a verified target mapping. Pending, NeedsReview, Error, Skipped and missing/mismatched target entries are omitted; their memberships are retained. Once resolved and copied, an entry returns at its stored position. Target audio is read and hashed for verification; it is not re-encoded.

Each folder playlist contains `#PLAYABLE:<count>` and `#OMITTED:<count>` comments, and run prints these counts. Counts refer to playback occurrences, including repetitions. Empty playlists are retained with their IDs. Removed source occurrences are absent from both counts. Playlists use UTF-8 without BOM, relative paths and Windows backslashes. Managed replacements retain the existing backup/collision safeguards. A regeneration failure leaves completed audio copies and mappings intact; another run retries regeneration even though no audio remains Ready.

`playlist-index.csv` includes Type=Folder with Code=FOLDER-0001. Artist/Album columns are empty for these rows; the original folder display name is in PlaylistPath. Folder rows follow Artist/Album rows, sorted by numeric folder ID. Deleting the index or playlist never allocates replacement IDs. `playlists <target> --workspace <same-workspace>` also rebuilds folder playlists from the database; without that workspace, existing managed folder playlists are preserved, but membership cannot be reconstructed from organized target paths alone.

## Database and metadata migration

Progress schema **1 → 2** adds tables without dropping existing data. New table creation and the schema-version update commit atomically. See [progress-schema.sql](progress-schema.sql).

| Table | Purpose |
|---|---|
| `source_folders` | Permanent integer ID, source root, original/current folder paths, display name |
| `source_occurrences` | Stable occurrence ID, folder/file IDs, original/current source paths, present flag |
| `folder_order` | Ordered positions pointing to occurrences; repeated references allowed |
| `canonical_mappings` | Verified source-file → canonical organized target mapping |
| `metadata_policy_audit` | Previous complete indexed record and migration decision per file/policy |

Metadata policy version **2** is stored with identification evidence. Before a normal run analyzes or applies files, migration examines legacy Ready and Processed records. Progress commands perform the audit on their first invocation; merely opening the database upgrades its additive schema but does not analyze audio. Folder identity bootstrap uses existing indexed paths, without reading source audio. The next scan discovers current memberships and source playlist order.

Before metadata transitions, migration loads and validates manual overrides and creates a consistent SQLite backup under `workspace\backups`. If that fails, metadata transitions do not begin. The backup includes the additive schema and bootstrapped folder identities. Each previous record is retained in `metadata_policy_audit`; status/history updates use the existing per-file transaction. Interrupted audits can be rerun: completed policy-2 records are skipped and unaudited legacy records remain eligible.

| Legacy record | Migration result |
|---|---|
| Ready with independently valid embedded/filename metadata | Remains Ready; provenance and policy version recorded |
| Ready with explicit file/folder manual overrides | Overrides are reapplied and preserved; remains Ready if all required fields are valid |
| Ready with verified fingerprint/MusicBrainz evidence independent of folder inference | Remains Ready when persisted evidence supports its fields |
| Ready using folder-derived fields, same-folder inference or insufficient provenance | Discovered, with `MetadataPolicyReassessment`; normal run analyzes again before copying |
| Processed with unsupported legacy metadata | NeedsReview; existing target audio and timestamps retained, omitted from Folder playlists |
| Existing NeedsReview, Error, Skipped or pending records | Status retained; normal status rules apply |

The audit checks per-field provenance, original tags, independent filename hints and persisted recording/release evidence. It does not simply trust a nonempty result or a Ready label. Unknown provenance is reassessed when the value cannot be independently justified. Known automatically written contaminated tags are suppressed as resolver inputs using persisted `SuppressedAutomaticFields`; migration never erases or rewrites the actual source tags. Explicit manual values still take precedence. Folder-derived track/release/year evidence is not made trustworthy merely by a high recording score.

Old derived-resolution cache keys are invalidated by the new policy namespace; reusable fingerprints and factual HTTP caches remain. Old saved plans lacking metadata policy 2 are rejected. Create a new plan or use normal run. Folder IDs, source occurrences, progress history and explicit manual JSON are preserved. This migration never modifies or deletes source audio and does not rewrite or relocate already organized audio.

## Example target tree

The synthetic duplicate test produces this shape: a root track, two Christmas files and one Party file contain identical bytes. Christmas's explicit order repeats one entry, so its playlist has three playable occurrences pointing to the same WAV. With MP3 input the organized file keeps `.mp3` instead.

```text
target\
  Test Artist\
    2020 - Test Album\
      01 - Test Song.wav
  _Playlists\
    Artists\Code 1 - Test Artist.m3u8
    Albums\Code 101 - Test Artist - Test Album.m3u8
    Folders\
      [FOLDER-0001] source.m3u8
      [FOLDER-0002] Christmas.m3u8
      [FOLDER-0003] Party.m3u8
  playlist-map.json
  playlist-index.csv
  .mp3organizer.json
  .mp3organizer-metadata.json
```

All three Christmas entries and the Party entry contain:

```text
..\..\Test Artist\2020 - Test Album\01 - Test Song.wav
```

The full test suite covers all ten requested scenarios, plus unsafe order fallback, ID preservation through reset/restart, missing playlist recovery, legacy plan rejection, suppression of old automatically written tags and preservation of explicit folder overrides. Tests use synthetic audio and mocked services only. Build and test output is saved alongside this documentation.



