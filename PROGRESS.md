# Incremental collection progress

This extension uses the existing scanner, read-only TagLib reader, AudioMetadata model, SQLite wrapper, identification cache, manual resolver and reporting services. The current extension adds [runtime LLM recognition, year enrichment, review, and explicit verified tag edits](RECOGNITION.md). Source audio is never re-encoded, moved, renamed or deleted. The processing database remains authoritative for state.

## Components

- `ProgressRepository`: SQLite schema, GUID identity, metadata/status/history commits, location updates and consistent backups.
- `IncrementalProgressService`: cheap scans and bounded analysis; injected metadata reader, identity matcher and resolver enable isolated tests.
- `IProgressIdentityMatcher` / `ShaProgressIdentityMatcher`: recognizes a unique missing indexed file by SHA-256 and size. This interface can later incorporate fingerprints. Distinct existing duplicate files remain distinct entries.
- `ProgressResetService`: scoped resets and backup-before-confirmation for reset-all.
- `ProgressCommands`: command parsing, single-writer lock, statistics and identification sessions.
- `ProgressResultRecorder`: records results of existing analyze/identify/resolve and successful apply operations for files already indexed with the same hash. It never creates an implicit inventory scan. Dry-run apply does not update progress.

## Database

Default location: `<workspace>/music-organizer.db`, separate from `<workspace>/cache.db` and authoritative `manual-resolutions.json`. Use the same workspace across commands. Workspace must be outside the source; continue keeping it outside the organized target too.

See [progress-schema.sql](progress-schema.sql) for the full schema. Schema version 4 uses WAL, synchronous FULL and bound SQL parameters. Windows native SQLite is reused; no package installation is needed.

`music_files` contains Id (GUID), SourceRoot, OriginalPath, CurrentPath, FileSize, LastWriteTimeUtc, SHA256, Artist, Album, Title, TrackNumber, Year, Bitrate, Status, LastProcessedUtc, ErrorMessage, and separate original/basic and effective metadata JSON. The JSON includes album artist, disc, duration and per-field provenance. `processing_history` appends status events and timestamps. Each file result and its history entry commit together.

OriginalPath remains the discovery path. CurrentPath can change while Id stays constant. A scan can recognize an external rename when exactly one missing indexed entry matches its content. Ambiguous matches are not guessed. A state-only repository method can update CurrentPath for a future authorized relocation service; it performs no filesystem relocation. Copying to the organized target does not change the indexed source path, because the source still exists.

## Scanning and status

`scan` recursively includes all already-supported audio formats, including MP3. It reads metadata/hash for new files or files whose size or modification timestamp changed. Unchanged files retain metadata and progress and incur no TagLib/hash/API work. Initial scan reads all new metadata but does not call identification APIs.

`scan --force` re-reads metadata and SHA-256 for every discovered file. If content and tags still match, status and LastProcessedUtc are preserved. Changed content/tags become Discovered again; previous processing events and timestamps remain in history. Use --force if an external tag editor preserves both size and timestamp; normal stat-based scanning cannot detect such edits.

Missing/inaccessible entries are not deleted from the index. Scanner warnings are printed; scan exits 3 when errors occur. A file missing when selected for analysis is marked Error. Status reflects the persisted index, not a fresh filesystem inventory.

| Status | Meaning |
|---|---|
| Discovered | Indexed, awaiting analysis (also the state after a progress reset) |
| Analyzed | Reserved intermediate state, eligible for completion |
| NeedsReview | Analysis finished but metadata still needs a decision; not automatically retried |
| Ready | Analysis completed with usable required metadata |
| Processed | Existing validated apply completed for this indexed file |
| Skipped | Explicitly skipped state, reserved for future workflow use |
| Error | Failed scan/analysis; stored message; requires reset-errors or a changed file |

`status` shows Ready to apply, Pending analysis, Processed, Needs review and other counts. Completion now means Processed + Skipped divided by total; Ready is excluded because copying is unfinished. Normal `run` carries Ready through validated copying to Processed. NeedsReview and Error are excluded from completion and left alone. See [RUN.md](RUN.md). Source-folder and source-playlist counts are also reported; see [FOLDERS.md](FOLDERS.md) for schema migration and the audit of legacy Ready/Processed results.

## Bounded analysis

`analyze --limit N` takes only Discovered/Analyzed entries in case-insensitive full input-path order. The limit bounds attempted files, including failures. It does not implicitly rescan the collection. A second invocation continues with the next pending entries. Each result is committed before proceeding. If the process stops during a file, only that uncommitted file may need repeating. One file failure is recorded and does not abort the batch; storage failure stops safely because durability cannot be guaranteed. Batch exit code is 3 if it encountered file errors.

Analysis defaults to offline and reuses manual decisions and identification cache. Add --online with the existing AcoustID/fpcalc/MusicBrainz configuration to permit network identification. --reports optionally exports identification reports from indexed effective metadata. Existing `analyze "<source>"` remains the original full-source analysis command; use `analyze --limit N` for incremental work. Other full-source commands retain their existing behavior.

Existing resolve/identify/analyze commands update matching indexed entries after resolution. After editing manual JSON directly, reset the relevant file/folder (or reset-review) and run incremental analysis to update its indexed result. Changes to manual JSON do not silently reset every Ready file.

Use one progress command at a time per workspace. A file lock prevents concurrent scan/analyze/reset operations. The lock file may remain after a process ends; the OS releases its lock. Do not delete it to bypass a running command.

## Resets

- `reset-file "<current-path>"`: only that indexed path.
- `reset-folder "<folder>"`: current paths inside the folder, recursively; similarly named sibling folders are excluded.
- `reset-errors`: only Error entries.
- `reset-review`: only NeedsReview entries.
- `reset-progress`: every indexed entry becomes Discovered. Paths, identities, basic/effective metadata and history remain; current error and last-processed markers clear.
- `reset-all`: creates a consistent SQLite backup using VACUUM INTO, then asks for exactly `RESET`. Only that exact answer clears indexed rows and processing history. Cancellation retains both progress and the backup. Backup failure prevents confirmation/reset. Database/schema remain available for a fresh scan. Permanent source-folder IDs and occurrence records are retained and reconnected during that scan.

Backups are named `backups/music-organizer-<UTC timestamp>-<unique suffix>.db` under the workspace. Reset-all applies **only to the progress index**. It does not erase manual decisions, identification cache, saved plans, target playlists/IDs or audio. Individual scoped resets are committed per file and can be safely repeated if interrupted.

## PowerShell examples — not executed against your library

```powershell
$dll = 'C:\Users\tglaz\Documents\Codex\2026-09-26\build-the-first-read-only-version\outputs\Mp3Organizer\artifacts-review-scope\Mp3Organizer.dll'
$workspace = 'E:\Mp3-Ai-test-workspace'

dotnet $dll scan 'E:\Mp3-Ai-test' --workspace $workspace
dotnet $dll status --workspace $workspace
dotnet $dll analyze --limit 200 --workspace $workspace --online --reports 'E:\Mp3-Ai-test-reports'
# Repeat later: resumes with pending entries.
dotnet $dll analyze --limit 200 --workspace $workspace --online

dotnet $dll reset-file 'E:\Mp3-Ai-test\album\Track02.mp3' --workspace $workspace
dotnet $dll reset-folder 'E:\Mp3-Ai-test\album' --workspace $workspace
dotnet $dll reset-errors --workspace $workspace
dotnet $dll reset-review --workspace $workspace
dotnet $dll reset-progress --workspace $workspace
dotnet $dll scan 'E:\Mp3-Ai-test' --force --workspace $workspace
dotnet $dll reset-all --workspace $workspace
```

The reset-file/folder examples use placeholder paths; substitute actual indexed paths. Reset commands are optional, not steps to run routinely. Do not run reset-progress/reset-all between batches if you want to retain completion.

## Verification

The complete offline .NET 10 application and test suite compile with warnings treated as errors. 222 tests pass, including unchanged-scan read avoidance, limits, processed-file exclusion, persistence across a separate process, interruption after commit, scoped resets, forced rescan, SHA relocation/duplicates, backup-before-confirmation, backup failure, existing-workflow integration and source immutability. Tests use temporary SQLite databases, synthetic WAV fixtures and mocked identification. No real music library was scanned.

Standard NuGet/MSBuild restore remains unverified due to the existing environment restriction. Rebuild this output with `./build-offline.ps1 -OutputDirectory artifacts-review-scope`; run `dotnet artifacts-review-scope/Mp3Organizer.Tests.dll <scratch-directory>`.






