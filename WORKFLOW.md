# Persistent identification and manual resolution

## Architecture

The source is read-only. Scanning computes whole-file SHA-256; size and modification time are observations, never substitutes for content identity. `ManualMetadataResolver` wraps `CachedMetadataResolver` and the existing automatic resolver. Per-field precedence is file override, folder override, valid embedded tag, confident online result, then fallback. Original tags and pre-manual metadata remain available. No audio tags are saved.

`IdentificationCache` uses Windows native `winsqlite3.dll`, bound parameters, WAL and full synchronization. No additional package is needed. `IFingerprintGenerator` separates fpcalc from caching. `IResolutionConsole` enables scripted tests. `ManualResolutionStore` owns authoritative JSON; SQLite contains only recomputable automatic results. Planning records the manual file revision; changing it requires replanning. Playlist regeneration reconciles manual decisions with the target metadata sidecar.

## Workspace and database

Default workspace is `workspace` under the current directory; override with `MP3ORGANIZER_WORKSPACE` or `--workspace`. Use the same absolute workspace throughout. It must be outside source and target.

- `cache.db`: SQLite cache; `--cache` accepts a directory or `.db` path.
- `manual-resolutions.json`: authoritative human decisions.
- `manual-resolutions.csv`: derived overview, regenerated on save/resolve/validation.
- `manual-resolutions.json.bak-*`: backups from application edits.

Schema version is SQLite `user_version=1`; see [cache-schema.sql](cache-schema.sql).

| Table | Contents |
|---|---|
| source_files | SHA identity, path, size, modification time, duration and timestamps |
| fingerprints | SHA, fingerprint, duration, algorithm, generator version and timestamps |
| cache_entries | Hashed semantic keys, raw payload, status, timestamps and retry time |
| identifications | Automatic results, score, AcoustID/MusicBrainz IDs, metadata and evidence |

Successful results and fingerprints have no expiry. No-match/ambiguous results expire after 30 days by default; transient failures after five minutes. Expired negatives are not reused offline. Refresh bypasses entries once per key per invocation. Automatic keys include SHA, embedded metadata and folder consensus; path fallback is recomputed after renaming. SHA is checked even when size/time match. Legacy unexpired hash-named JSON entries can be imported lazily by pointing `--cache` at the old directory; old files remain untouched.

## Manual JSON

See [manual-resolutions.schema.json](manual-resolutions.schema.json). Minimal example:

```json
{
  "schemaVersion": 1,
  "folders": {
    "folder:my-album": {
      "lastKnownPath": "C:\\Music-test\\album",
      "artist": "Monika Brodka",
      "album": "Granda",
      "year": 2010
    }
  },
  "files": {}
}
```

Folder keys are stable `folder:<id>` identifiers, normally UUIDs, independent of folder contents. File keys are `sha256:<64 hex digits>`. Each entry requires an absolute `lastKnownPath`. Optional metadata: artist, albumArtist, album, title, trackNumber, discNumber, year. Optional `anchorSha256` records hashes for conservative folder relocation. Null/omitted fields fall through; empty strings are explicit values and can remain unresolved. Numeric fields accept null or 1–9999; text accepts null or at most 500 characters without controls.

Folder decisions survive content changes. Relocation requires a unique sufficiently overlapping anchor match; ambiguous matches are not guessed. Changed audio gets a new file identity. Missing references are reported without deleting decisions.

Every run loads JSON; resolution reloads when its size/time changes. Malformed JSON, unknown fields, duplicates and unsupported versions fail with diagnostics. Direct edits are authoritative. Detected concurrent edits prevent overwrite. Application saves use UTF-8 without BOM, temporary sibling files, atomic replacement and backups. Every confirmed field is saved immediately. CSV is never an input.

## Commands

```text
identify "<source>" [options]
analyze "<source>" [options]
resolve "<source>" [--only-unresolved] [--edit-manual] [options]
manual validate [--workspace "<directory>"]
plan "<source>" "<target>" [options]
apply "<source>" "<target>" [--workspace "<directory>"] [--reports "<directory>"] [--dry-run]
playlists "<target>" [--workspace "<directory>"]
```

Identification options: `--workspace PATH`, `--cache PATH`, `--reports PATH`, `--offline` (default), `--online`, `--identify-all`, `--rebuild-fingerprints`, `--refresh-identification`, and `--identification-retry-days N` (1–3650, default 30). Refresh identification requires online mode; manual values still win. Rebuild fingerprints recomputes locally for scanned tracks. Online setup is in [IDENTIFICATION.md](IDENTIFICATION.md).

Resolve defaults to unresolved folders. `--edit-manual` includes resolved folders; combining it with `--only-unresolved` restricts editing to unresolved folders. Enter accepts displayed defaults. Escape at selection skips a folder; Escape during editing stops that folder, retaining confirmed fields. Title/track are requested only where needed or when editing existing file decisions. Redirected input is refused; edit JSON for noninteractive workflows. Explicit blank decisions can be corrected with `--edit-manual`.

`identification.csv` includes FingerprintCacheHit, IdentificationCacheHit, ManualOverrideHit and ResolutionStatus. All other reports and playlists use effective metadata. Playlist IDs stay permanent; a changed normalized identity receives a new ID while historical IDs remain reserved.

## Small-folder workflow — not executed

From the solution directory in PowerShell, after populating `C:\Music-test` with test copies:

```powershell
$workspace = 'C:\Users\tglaz\Documents\Codex\MP3-test-workspace'
$reports = Join-Path $workspace 'reports'
.\Mp3Organizer.cmd identify 'C:\Music-test' --workspace $workspace --reports $reports --offline
.\Mp3Organizer.cmd resolve 'C:\Music-test' --workspace $workspace --reports $reports --only-unresolved
.\Mp3Organizer.cmd manual validate --workspace $workspace
.\Mp3Organizer.cmd plan 'C:\Music-test' 'E:\MP3-organized-test' --workspace $workspace --reports $reports
.\Mp3Organizer.cmd apply 'C:\Music-test' 'E:\MP3-organized-test' --workspace $workspace --reports $reports --dry-run
```

After reviewing the plan, removing `--dry-run` copies into the target. Playlists requires an existing managed target. Use one process per workspace/target; API rate limiting is in-process.

## Verification and limitations

Offline .NET 10 compilation passed with warnings treated as errors. The dependency-free runner passed 145 tests, including SQLite persistence across processes, expiry/refresh, manual precedence, direct edits, validation/backups, relocation, scripted interaction, stale plans, playlist reconciliation and source immutability. Synthetic files, fake fingerprint generators and mocked HTTP only were used. No real library scan, live API request or real interactive session was run. fpcalc was absent from PATH, so live decoding remains unverified. SQLite is Windows-specific. Standard NuGet/MSBuild restore remains unverified because this environment denies user NuGet configuration access; the complete offline build succeeded.

## Placeholder metadata correction

The shared placeholder detector normalizes Unicode/case and ignores spacing and punctuation when matching whole generic labels and numeric suffixes. Labels such as no artist, no title, and AudioTrack 02 are unreliable even when nonempty. Meaningful names containing these words are retained. Reports expose PlaceholderArtist, PlaceholderAlbum and GenericTrackTitle in ReviewReason, including after online replacement. Offline/failed lookup can propose Cypress Hill / Kingpin OST from the cleaned folder, while generic titles remain in review and reach interactive resolution. Automatic resolution cache version 3 invalidates earlier decisions; fingerprints and raw API caches remain reusable. Five regression tests cover variants, real-example reports, mocked online/failure paths, interactive/manual priority and stale cache invalidation.

## Online diagnostics and doctor

Online lookup requires explicit `--online`, no conflicting `--offline`, nonblank `ACOUSTID_API_KEY`, nonblank valid `MP3ORGANIZER_CONTACT`, and a discoverable fpcalc executable. There is no JSON configuration key for credentials. Online mode is not inferred from the absence of `--offline`. The program prints its reason before scanning. Missing prerequisites with `--online` return exit code 1 before scanning; they do not silently downgrade the request. “Online lookup enabled” means prerequisites are present, not that credentials or remote connectivity have been proven.

Executable discovery: `CHROMAPRINT_FPCALC` can specify an executable path or name; otherwise search PATH for fpcalc.exe/fpcalc. An explicitly configured missing path is an error rather than falling back to another installation. Runtime decode errors remain visible in identification reports.

```powershell
.\Mp3Organizer.cmd doctor --workspace 'E:\Mp3-Ai-test-workspace'
.\Mp3Organizer.cmd doctor --workspace 'E:\Mp3-Ai-test-workspace' --offline
```

Doctor runs fpcalc with `-version` and a 15-second timeout, hides the AcoustID key (presence only, not credential verification), checks MusicBrainz contact configuration and makes one public sample metadata query with a 15-second timeout. `--offline` explicitly skips connectivity. It opens/initializes the cache schema and rolls back a test insert, and validates manual JSON syntax/schema without checking referenced source paths. It may create the workspace/cache but does not scan music or modify manual decisions. Failures are reported separately, later checks still run, and any failed check returns exit code 1. A missing manual file is a valid empty state. Use the workspace outside source and target. Tests use mocked network/version probes; no live service or actual decoder was tested.
## Console progress
The launcher now uses artifacts-progress, built separately because the previous executable was in use. Identify/analyze/resolve show elapsed time, scan counts, metadata/hash reads, per-file identification counts, result/cache status and report writing. Identification tasks emit a waiting message every ten seconds while asynchronous fingerprint/API work runs. Planning also reports source reads and identification. Existing running processes do not gain these updates. Build this active output with: .\build-offline.ps1 -OutputDirectory artifacts-progress.


## Partial online result correction

The previous resolver explicitly assigned empty album/album artist and zero year/track/disc when no release was selected. The new MetadataFieldMerger selects each field separately: file manual > folder manual > reliable tags > usable online value > usable fallback. Empty/placeholder automatic values cannot erase useful lower-priority values. Explicit manual empty strings retain their existing intentional-override semantics. Uncertain release selection still reports Review; a folder album is a suggestion, not a confirmed release.

Identification reports append ArtistSource, AlbumArtistSource, AlbumSource, TitleSource, TrackNumberSource, DiscNumberSource and YearSource. Automatic resolution cache keys now use version 4, so old destructive results are not reused. Fingerprints and raw HTTP caches remain reusable. Existing saved plans should be regenerated; historical reports are not rewritten.

The corrected build is in artifacts-fieldmerge. Use Mp3Organizer-fieldmerge.cmd or invoke its DLL directly. The existing launcher was left untouched to avoid editing a running batch file. Synthetic reproduction of a batch file edited while its child ran produced a trailing command-fragment error: cmd resumes reading at the old byte offset. This is a likely explanation for the user's trailing Windows error because the prior update changed that active launcher; the exact reported localized filename-syntax message was not reproduced, so its cause is not conclusively established. Direct dotnet invocation bypasses this shell path without suppressing application errors.

Progress shows scanning, metadata/SHA reads, fingerprint/cache work, AcoustID, MusicBrainz and field resolution. Scan/read and identification heartbeats occur every three seconds while work is pending. Stage events indicate an operation started, not a completed network request. All 145 tests pass using synthetic data and mocked services. No real library was accessed.
## Filename track hints
Build artifacts-trackhint recognizes leading track numbers and tokens following a spaced dash, including Cuphead - OST - 07 7. Botanic Panic.mp3. Repeated numbers must agree. Four-digit year prefixes and conflicting numbers are rejected. Missing track numbers can be filled independently of a reliable title; interactive resolution offers the parsed number as the Enter default. Existing tags and manual choices retain priority. Automatic cache version is now 5. Build passed; 148 tests passed. The prior launchers remain unchanged; invoke artifacts-trackhint/Mp3Organizer.dll directly.


## Interactive navigation update
The artifacts-navigation build uses explicit ResolutionStep/ResolutionNavigator state. Backspace always goes to the preceding prompt; it does not delete typed characters. At the first field it returns to folder selection. Confirmed values remain saved and become defaults on revisiting. Enter accepts the current default; new input replaces it. Esc retains existing folder skip/abort behavior. Pending unconfirmed input is not saved when going back. File prompts (including retries and revisits) display filename, full source path and current artist/album/title/track/disc values immediately before the question. Folder prompts stay compact. No metadata, cache or source handling rules changed. 151 tests pass. Invoke artifacts-navigation/Mp3Organizer.dll directly; old running launchers remain untouched.


## Generic track filename hints
Build artifacts-tracklabels also recognizes Track02, Track 02, TRACK_02 and AudioTrack02 (case-insensitive), suggesting track 2 without inventing a title. Existing titles, track tags and manual decisions remain authoritative. Cached automatic resolutions now use version 6. Navigation and file context improvements are included. All 153 tests pass; only synthetic fixtures were used. Invoke artifacts-tracklabels/Mp3Organizer.dll after the current session finishes.


## Artist/album filename parsing
Build artifacts-filename verifies Artist - Album - NN - Title filenames, including Unicode dashes. Full filename-derived title tags now use the parsed song title. A leading number inside the actual title (08 - 3 lil'putos) is preserved instead of being stripped a second time. Automatic resolution cache version 7; 154 tests pass. Existing manual choices remain authoritative. Invoke artifacts-filename/Mp3Organizer.dll directly.


## Corrected editing controls (artifacts-ux)
Backspace now deletes typed text and does nothing on empty input. Alt+Left navigates to the previous question. Every question displays its file or folder path immediately before the prompt, including retries/revisits. Enter accepts the default and Esc skips/aborts the folder. Existing saved answers are retained. This supersedes the prior Backspace navigation instructions. All 156 tests pass; no library access. Invoke artifacts-ux/Mp3Organizer.dll.


## Cross-folder navigation
Build artifacts-crossfolder retains each folder's question history for the interactive session. Alt+Left at a folder selection returns to the previous visited folder's last question with the saved answer and source path. Enter proceeds forward again; repeated backwards navigation preserves answers. At the first session folder a clear boundary message is shown. History is session-local; after completion/restart use --edit-manual to revisit saved answers. Backspace still edits text. All 157 tests pass; only synthetic files were used. Invoke artifacts-crossfolder/Mp3Organizer.dll.

