# Mp3Organizer: quick start and complete command guide

This guide describes the current **artifacts-progress-state** build. Older build folders and launchers remain on disk; do not assume they contain the same commands. All examples use PowerShell and your test-library paths. Examples are instructions for you to run; writing this guide did not execute them against your music.

## 1. Recommended quick start

### 1.1 Set paths in each PowerShell session

Use `dotnet` with the explicit DLL path. You can then run commands from any working directory, including `C:\Windows\system32`, without accidentally creating a different workspace there.

```powershell
$dll = 'C:\Users\tglaz\Documents\Codex\2026-09-26\build-the-first-read-only-version\outputs\Mp3Organizer\artifacts-progress-state\Mp3Organizer.dll'
$source = 'E:\Mp3-Ai-test'
$target = 'E:\Mp3-Ai-test-organized'
$workspace = 'E:\Mp3-Ai-test-workspace'
$reports = 'E:\Mp3-Ai-test-reports'

dotnet $dll --help
```

Keep these four directories separate. Source contains your input audio. Target contains organized copies. Workspace contains progress/cache/manual decisions. Reports contains generated CSVs and saved copy plans.

The source is immutable. None of the commands intentionally writes tags, moves, renames or deletes source audio. The only audio-producing operation is an explicitly executed `apply`, which copies into the target.

### 1.2 Configure acoustic identification

1. Obtain an AcoustID **application/client API key**, not a user submission key. Register an application through the [official AcoustID web-service page](https://acoustid.org/webservice).
2. Download/extract the Windows `fpcalc` executable from the [official Chromaprint page](https://acoustid.org/chromaprint). Set its actual executable path below; the example location is a placeholder.
3. Supply your real contact email or URL for the MusicBrainz User-Agent. This application uses public metadata lookup, not account-authenticated editing. See the [MusicBrainz API documentation](https://musicbrainz.org/doc/MusicBrainz_API).

```powershell
$env:ACOUSTID_API_KEY = 'YOUR_ACOUSTID_APPLICATION_KEY'
$env:CHROMAPRINT_FPCALC = 'C:\Tools\Chromaprint\fpcalc.exe'
$env:MP3ORGANIZER_CONTACT = 'your-email@example.com'

dotnet $dll doctor --workspace $workspace
```

These assignments affect the current PowerShell process and programs launched from it. Repeat them in a new terminal unless you configure persistent user environment variables; instructions are in section 3. Do not paste a real key into reports, screenshots, shared scripts or this guide.

`doctor` checks executable/version, key presence, MusicBrainz contact/connectivity, cache writability and manual JSON validity. It does **not** prove that an AcoustID key is accepted by the service. It may create the cache. `Manual resolutions: not created yet (valid empty state)` is normal before the first manual edit.

For a local-only check, run `dotnet $dll doctor --workspace $workspace --offline`. Missing online prerequisites can still be reported as failures; offline identification itself does not require them.

### 1.3 Index once, analyze in batches

```powershell
dotnet $dll scan $source --workspace $workspace
dotnet $dll status --workspace $workspace
dotnet $dll analyze --limit 200 --workspace $workspace --online --reports $reports
dotnet $dll status --workspace $workspace
```

Repeat **only the analyze/status commands** to work through the pending files over successive sessions. Completed results are committed per file. Run `scan` again when files have been added or changed; unchanged files are inexpensive to check.

Online access always requires `--online`. Omitting `--offline` does not enable it. For local tags, cached results and filename/folder hints only, substitute `--offline` or omit both flags.

### 1.4 Review uncertain metadata

```powershell
dotnet $dll resolve $source --workspace $workspace --reports $reports --only-unresolved
```

This is a full-source review command, not a bounded database batch. It scans and reads the source but reuses cached identification and manual decisions. It defaults to offline. You can pass a particular album folder to review a smaller scope.

Controls:

- **Enter:** accept the displayed default.
- **Typing and Enter:** replace the current value.
- **Backspace:** delete typed characters; at empty input, do nothing.
- **Alt+Left:** go back one question. At a folder selection, return to the last question of the preceding visited folder in this session.
- **Esc:** skip/abort the current folder, retaining already confirmed answers.

Two blank lines and the full folder/file path appear immediately above each input prompt. Going backwards retains confirmed answers as defaults. Unconfirmed text is not saved. Navigation history is session-local.

To revisit already completed answers, use **`--edit-manual` instead of `--only-unresolved`**. Edit mode intentionally asks you to reconfirm existing file decisions. Enter accepts a displayed track number; you do not need to retype it.

### 1.5 Plan, inspect, simulate, then copy

```powershell
dotnet $dll plan $source $target --workspace $workspace --reports $reports
dotnet $dll apply $source $target --workspace $workspace --reports $reports --dry-run
```

Inspect the printed plan/report location, especially conflicts, duplicate decisions, missing metadata and filename suggestions. A successful dry run validates the plan but copies nothing. When you are satisfied, explicitly run:

```powershell
dotnet $dll apply $source $target --workspace $workspace --reports $reports
```

First-time targets must be empty or absent; subsequent targets must be managed by Mp3Organizer and match the source. Planning and apply cover the supplied source, not only the latest 200-file batch. A pending or NeedsReview status is not a blanket copy prohibition: inspect the plan's actual contents before applying.

Do not change source, target, manual JSON or playlist mapping between plan and apply. If validation reports a change, create a fresh plan.

### 1.6 Refresh playlists after changes

```powershell
dotnet $dll playlists $target --workspace $workspace
```

This scans the managed target, regenerates playlists and `playlist-index.csv`, preserves permanent codes, and backs up managed replacements. M3U8 entries use relative Windows `\` paths and UTF-8 without BOM. Keep playlists in their target directory hierarchy so the relative paths continue to work.

## 2. Command selection at a glance

| Task | Command |
|---|---|
| See available syntax | `--help` |
| Check setup | `doctor` |
| Index new/changed files | `scan SOURCE` |
| Force metadata/hash reread | `scan SOURCE --force` |
| View persisted counts | `status` |
| Process a bounded set of pending files | `analyze --limit N` |
| Full-source analysis and inventory reports | `analyze SOURCE` |
| Full-source identification/report run | `identify SOURCE` |
| Answer unresolved questions | `resolve SOURCE --only-unresolved` |
| Correct previous answers | `resolve SOURCE --edit-manual` |
| Validate edited manual JSON | `manual validate` |
| Calculate copy paths/decisions/playlists | `plan SOURCE TARGET` |
| Validate without copying | `apply SOURCE TARGET --dry-run` |
| Execute saved plan | `apply SOURCE TARGET` |
| Regenerate managed playlists | `playlists TARGET` |
| Retry selected work | A scoped `reset-*` command |

Use the same explicit `--workspace $workspace` everywhere. Use `--reports $reports` on full-source report/plan/apply commands. The two analyze forms are different: **`analyze --limit N` has no source argument**; its sources come from the index.

## 3. Configuration reference

### Environment variables

| Variable | Meaning | Required when |
|---|---|---|
| `ACOUSTID_API_KEY` | AcoustID application/client key | `--online` |
| `CHROMAPRINT_FPCALC` | Executable path or executable name | Needed if fpcalc is not discoverable on PATH |
| `MP3ORGANIZER_CONTACT` | Contact email or URL embedded in MusicBrainz User-Agent | `--online` |
| `MP3ORGANIZER_WORKSPACE` | Default workspace directory | Optional; explicit `--workspace` wins |

There is no credentials JSON file, `.env` auto-loader or alternate configuration key. A PowerShell variable such as `$ACOUSTID_API_KEY` is not enough; use `$env:ACOUSTID_API_KEY` so the child process receives it.

Without `--workspace` or its environment variable, workspace is `workspace` relative to the shell's current working directory. Full-source reports default to `reports` under that directory. This is why absolute paths are recommended.

### Optional persistent user settings

After assigning the environment variables in section 1, you can persist those exact values for future processes:

```powershell
[Environment]::SetEnvironmentVariable('ACOUSTID_API_KEY', $env:ACOUSTID_API_KEY, 'User')
[Environment]::SetEnvironmentVariable('CHROMAPRINT_FPCALC', $env:CHROMAPRINT_FPCALC, 'User')
[Environment]::SetEnvironmentVariable('MP3ORGANIZER_CONTACT', $env:MP3ORGANIZER_CONTACT, 'User')
[Environment]::SetEnvironmentVariable('MP3ORGANIZER_WORKSPACE', $workspace, 'User')
```

Do this only after replacing example values. User environment values are not an encrypted secret vault. A newly launched terminal normally inherits the settings; an already-running terminal retains its old environment. If launched by a long-running host, restart that host or set the `$env:` values directly in the current session.

To remove the persisted API key:

```powershell
[Environment]::SetEnvironmentVariable('ACOUSTID_API_KEY', $null, 'User')
Remove-Item Env:ACOUSTID_API_KEY -ErrorAction SilentlyContinue
```

### fpcalc discovery and diagnostics

An explicit `CHROMAPRINT_FPCALC` path is used when present. If it names a missing path, the program reports that problem rather than silently using another installation. A configured executable name is searched on PATH; without the variable, the names `fpcalc.exe`/`fpcalc` are searched on PATH. Executable discovery alone does not prove that it runs or can decode your files.

```powershell
Test-Path -LiteralPath $env:CHROMAPRINT_FPCALC
& $env:CHROMAPRINT_FPCALC -version
dotnet $dll doctor --workspace $workspace
```

The direct `Test-Path` example assumes the variable contains a full path. `doctor` applies the actual discovery rules.

### Online-mode conditions

The CLI enables online mode only with `--online`, a nonblank application key, a configured contact without control characters, and a discoverable fpcalc executable. Conflicting `--online --offline` is rejected. Missing prerequisites produce a specific reason and stop an explicitly online request before scanning.

`Online lookup enabled` means prerequisites were found, not that a request succeeded. Per-file reports distinguish unavailable services, no matches, ambiguous releases and successful identifications. fpcalc works locally; AcoustID receives fingerprints/duration and the application key. MusicBrainz receives metadata lookup requests. Audio files are not uploaded. No fingerprint-submission command is implemented.

The current clients space AcoustID requests by at least 350 ms and MusicBrainz requests by at least 1,100 ms within the process. Retries include delays. Run one online organizer process at a time; independent clients do not share its limiter. See [IDENTIFICATION.md](IDENTIFICATION.md) for the matching policy and service references.

## 4. Complete command reference

In the syntax below, angle brackets denote required arguments, square brackets denote optional arguments; do not type those brackets. Paths containing spaces must be quoted. PowerShell variables from section 1 are already single arguments and need no extra quoting.

### `--help` / `-h`

```powershell
dotnet $dll --help
dotnet $dll -h
```

Prints the compact command list. No library scan. There is currently no `--version` command and no unified release version in assembly metadata; use the explicit build path in this guide. Do not interpret the `Mp3Organizer/1.1` API User-Agent as an official package version.

### `doctor`

Syntax: `doctor [--workspace PATH] [--cache PATH] [--offline]`.

```powershell
dotnet $dll doctor --workspace $workspace
dotnet $dll doctor --workspace $workspace --offline
```

Checks fpcalc version with a 15-second timeout, key presence without printing its value, contact configuration, one MusicBrainz sample query with a 15-second timeout, SQLite cache writability using a rolled-back insert, and manual JSON syntax/schema. Connectivity is skipped with `--offline`. It may initialize the workspace/cache. It does not scan audio or validate paths inside manual entries. A missing manual file is accepted. Individual failures are listed and later checks continue; any failed check gives exit code 1. This checks `cache.db`, not the complete health of `music-organizer.db`.

### `scan`

Syntax: `scan SOURCE [--force] [--workspace PATH]`.

```powershell
dotnet $dll scan $source --workspace $workspace
dotnet $dll scan $source --force --workspace $workspace
```

Recursively discovers all supported audio: MP3, FLAC, M4A, AAC, OGG, WAV and WMA. New files receive permanent GUID identities and original/current paths. Normal scanning compares file size and modification time. Unchanged entries are not rehashed or reread through TagLib, and retain status. Changed/new files receive metadata and SHA-256; no online identification occurs.

`--force` rereads every discovered file. Matching hash/tags retain processing state; changed content/tags return to Discovered. History remains. Use it when an external tag editor may preserve size/time. A unique missing indexed file can be recognized by SHA/size after an external rename; ambiguous matching never merges entries automatically. Scanner errors are reported; indexed records are not silently removed when files disappear. Exit code 3 indicates scan errors.

### `status`

Syntax: `status [--workspace PATH]`.

```powershell
dotnet $dll status --workspace $workspace
```

Reads the persistent index, without rescanning audio. Shows total, Processed, Ready, Skipped, NeedsReview, pending and Error counts; missing/unreliable Artist/Album; missing Track/Year; and completion percentage. An empty workspace initializes an empty progress database. Statistics cover all roots indexed in that workspace.

| State | Meaning / next action |
|---|---|
| Discovered | Awaiting analysis; selected by the next batch |
| Analyzed | Reserved intermediate state; eligible for a batch |
| NeedsReview | Analyzed but uncertain/incomplete; resolve manually or explicitly reset-review |
| Ready | Required metadata usable; analysis complete, not necessarily copied |
| Processed | Matching indexed file was included in successful apply |
| Skipped | Reserved explicit skipped state; not automatically retried |
| Error | Stored failure; fix its cause and reset-errors |

Completion is `(Ready + Processed + Skipped) / Total`. NeedsReview/Error do not count as complete. A batch can process zero entries while reviews remain, because reviews are not endlessly retried. Missing Year does not necessarily prevent readiness. Status is a database view, not a current filesystem audit.

### `analyze --limit N` — incremental

Syntax: `analyze --limit N [--online|--offline] [--workspace PATH] [--reports PATH]`.

```powershell
dotnet $dll analyze --limit 200 --workspace $workspace --online --reports $reports
dotnet $dll analyze --limit 50 --workspace $workspace --offline
```

Requires a positive integer and no source positional argument. Selects pending entries in discovery order across the workspace index. The limit bounds attempted files, including failures. It does not implicitly scan for new files. Each result/error commits separately; a stopped run may repeat the in-flight uncommitted file, not the completed batch. File errors do not abort remaining attempts; database durability errors stop the run. Batch exit code 3 indicates file errors.

When `--reports` is supplied, writes identification reports from the indexed effective metadata, grouped by source root. These reports can include entries outside the latest batch. This command uses the default workspace cache and default retry policy. It does **not** accept `--cache`, `--identify-all`, `--rebuild-fingerprints`, `--refresh-identification` or `--identification-retry-days`; use full-source commands when you need those controls.

### `analyze SOURCE` — full source

Syntax: `analyze SOURCE [identification options] [--workspace PATH] [--reports PATH]`.

```powershell
dotnet $dll analyze $source --workspace $workspace --reports $reports --offline
```

Scans/reads the supplied source and generates inventory/quality reports, with optional online identification. It is not the incremental `--limit` command. Existing cache/manual decisions are reused; matching entries in an existing progress index are updated. It does not implicitly create a collection index or copy audio. Use it when you want a fresh full-source report; use batches for routine incremental processing.

### `identify SOURCE`

Syntax: `identify SOURCE [identification options] [--workspace PATH] [--reports PATH]`.

```powershell
dotnet $dll identify $source --workspace $workspace --reports $reports --online
dotnet $dll identify $source --workspace $workspace --reports $reports --offline
```

Full-source identification and reports without audio copying/tag changes. It uses the same scanning/resolution pipeline as full-source analyze, with an `identification-...` report directory. Reliable tags normally avoid unnecessary lookup. `--identify-all` gathers fingerprint evidence even for well-tagged files. Manual values still win. The command is offline unless `--online` is present.

### Identification options for full-source commands

These apply to `identify SOURCE`, `analyze SOURCE`, `resolve SOURCE`, and `plan SOURCE TARGET`.

| Option | Effect |
|---|---|
| `--online` | Permit external lookup after configuration validation |
| `--offline` | Explicit no-network mode; also the default |
| `--identify-all` | Include well-tagged files in identification; may compute fingerprints locally offline |
| `--cache PATH` | Override cache directory or `.db` file; outside both libraries |
| `--rebuild-fingerprints` | Force local fingerprint recomputation for scanned tracks |
| `--refresh-identification` | Refresh automatic/API results; requires --online; does not overwrite manual choices |
| `--identification-retry-days N` | Negative/ambiguous-result lifetime, 1–3650 days; default 30 |

Examples for deliberate maintenance, not every session:

```powershell
dotnet $dll identify $source --workspace $workspace --reports $reports --online --refresh-identification
dotnet $dll identify $source --workspace $workspace --reports $reports --offline --rebuild-fingerprints
dotnet $dll identify $source --workspace $workspace --reports $reports --online --identify-all --identification-retry-days 7
```

Successful automatic results and fingerprints do not expire automatically. Negative/ambiguous results default to 30 days; transient failures to five minutes. Expired failures/negatives are not reused offline. Refresh bypasses automatic caches but cannot guarantee a remote service will return a match. Changing the negative lifetime does not retroactively rewrite every existing entry's expiry. Old-version automatic decisions may be recomputed after application updates while raw fingerprints/API results remain reusable.

### `resolve SOURCE`

Syntax: `resolve SOURCE [--only-unresolved] [--edit-manual] [identification options] [--workspace PATH] [--reports PATH]`.

```powershell
dotnet $dll resolve $source --workspace $workspace --reports $reports --only-unresolved
dotnet $dll resolve 'E:\Mp3-Ai-test\Cuphead OST' --workspace $workspace --reports $reports --edit-manual
```

Offers folder suggestions, then applicable Artist/Album/Year and file Title/Track questions. Defaults to unresolved folders. `--edit-manual` also includes completed folders and revisits existing file decisions. Combining it with `--only-unresolved` restricts the edit pass to unresolved folders.

Each confirmed field is saved immediately to manual JSON with a backup. Esc and restarting retain those answers. Revisited questions show the current saved value as the default. File context includes current metadata; the full path sits immediately above input. Backspace edits text; Alt+Left navigates. If the terminal consumes Alt+Left, the program cannot receive that key combination. A real interactive terminal is required; redirected standard input is refused. For noninteractive work, edit JSON and validate it.

An unresolved album or review flag can still cause a folder to appear even when some displayed values look plausible. The current workflow can offer title questions from a retained review reason; a suggested title is not necessarily a confirmed online result. Edit mode deliberately asks for completed values. Manual saved values are never silently replaced by a newly improved parser.

Recognized filename hints include `Track02.mp3`, `AudioTrack02.mp3`, `01 - Title.mp3`, `Artist - Album - 01 - Title.mp3` and matching repeated numbers such as `07 7. Botanic Panic.mp3`. Conflicting repeated numbers are not guessed. A generic track filename supplies a number but not a meaningful title. A number belonging to a title, such as `08 - 3 lil'putos.mp3`, is retained.

### `manual validate`

Syntax: `manual validate [--workspace PATH]`.

```powershell
dotnet $dll manual validate --workspace $workspace
```

Loads schema version 1 JSON, rejects malformed/unknown/duplicate fields, checks folder references and file SHA/path references, and regenerates `manual-resolutions.csv`. Unlike doctor, it can read referenced files to check hashes. Missing references are reported without deleting decisions. Invalid JSON stops with an error; valid JSON with unresolved-reference warnings can still exit 0. A missing JSON file represents an empty manual set.

Manual field precedence is file override > folder override > reliable embedded tag > usable online result > usable fallback, evaluated separately. Missing automatic fields cannot clear useful lower-priority values. Omitted/null manual fields fall through; an explicit empty manual string remains an intentional override. See [manual-resolutions.schema.json](manual-resolutions.schema.json) and [WORKFLOW.md](WORKFLOW.md).

### `plan SOURCE TARGET`

Syntax: `plan SOURCE TARGET [identification options] [--workspace PATH] [--reports PATH]`.

```powershell
dotnet $dll plan $source $target --workspace $workspace --reports $reports
```

Scans and resolves the supplied source, checks the managed target, calculates copy destinations, duplicate decisions, collisions, playlist codes and playlists, then saves a checksummed JSON plan and reports. It does not copy audio into the target. It is a full-source operation, not `--limit` aware. By default it uses tags/manual/cache/fallback without networking.

Paths are generally `Artist\YYYY - Album\NN - Title.ext`, with loose tracks under `Artist\Title.ext`; disc 2+ adds a disc marker. Various Artists compilation albums stay together. Missing year uses Unknown Year. Name collisions receive planned deterministic suffixes rather than silent overwrite. Inspect `copy-plan.csv`, `duplicates.csv`, `conflicts.csv`, `albums.csv`, `library.csv`, and identification reports.

Only byte-identical files verified with SHA-256/size can be automatically represented by one selected copy. Non-identical recording candidates remain for review; neither bitrate nor fingerprint similarity alone deletes/drops source audio. No source file is deleted by duplicate handling.

### `apply SOURCE TARGET`

Syntax: `apply SOURCE TARGET [--plan PATH] [--dry-run] [--workspace PATH] [--reports PATH]`.

```powershell
dotnet $dll apply $source $target --workspace $workspace --reports $reports --dry-run
dotnet $dll apply $source $target --workspace $workspace --reports $reports
```

Executes a previously saved plan. Without `--plan`, finds the latest saved plan for the exact source/target pair under reports. With `--plan`, supply the actual JSON path printed by plan; do not pass a CSV:

```powershell
$plan = 'E:\Mp3-Ai-test-reports\REPLACE_WITH_ACTUAL_PLAN_DIRECTORY\copy-plan.json'
dotnet $dll apply $source $target --workspace $workspace --plan $plan --dry-run
```

Validation checks source inventory/content, target snapshot, paths, manual revision, mapping and playlist references. `--dry-run` writes nothing. Actual apply copies to temporary target files, verifies hashes, and finalizes without overwriting different audio. Managed control files/playlists can be replaced with backups. It does not call identification APIs. Use the same workspace as planning. If anything relevant changed, replan instead of editing plan JSON. After interruption, replan; completed matching copies can be AlreadyPresent. Do not reset the progress database as a substitute for fixing a stale plan.

### `playlists TARGET`

Syntax: `playlists TARGET [--workspace PATH]`.

```powershell
dotnet $dll playlists $target --workspace $workspace
```

Requires a target created/managed by apply. Reads its copied audio and effective-metadata sidecar, reconciles manual decisions, and writes playlists, the code index, persistent mapping and managed metadata. It does not run online identification. Replacements are backed up. A missing/corrupt mapping fails rather than inventing replacement IDs.

Artist playlists use `_Playlists\Artists\Code <artistId> - <artist>.m3u8`; album playlists use a spoken concatenated code such as `Code 12301`, with artistId 123 and albumId 1 stored separately. IDs are permanent and never recycled. There is no global Various Artists artist playlist; compilation tracks contribute to actual performers' artist playlists. Each artist supports up to 99 permanent album IDs in this version.

`playlist-index.csv` is regenerated from the map/playlists. Deleting only the index does not assign new codes. The playlist uses UTF-8 without BOM and relative Windows separators; a path such as `..\..\Cypress Hill\...` is resolved from the playlist's containing folder, not from the shell's current directory. A player importing a playlist into a different location may resolve paths differently.

### Scoped reset commands

All accept `--workspace PATH`, modify organizer state only, and do not require confirmation. They preserve indexed identities/paths/basic metadata and append reset history. Current errors and last-processed markers clear; selected entries become Discovered.

| Command | Scope |
|---|---|
| `reset-file PATH` | One exact indexed current path |
| `reset-folder PATH` | Indexed current paths recursively inside that folder |
| `reset-errors` | Error entries only |
| `reset-review` | NeedsReview entries only |
| `reset-progress` | Every indexed entry |

```powershell
dotnet $dll reset-file 'E:\Mp3-Ai-test\Cuphead OST\REPLACE_WITH_ACTUAL_FILENAME.mp3' --workspace $workspace
dotnet $dll reset-folder 'E:\Mp3-Ai-test\Cuphead OST' --workspace $workspace
dotnet $dll reset-errors --workspace $workspace
dotnet $dll reset-review --workspace $workspace
dotnet $dll reset-progress --workspace $workspace
```

These are alternatives, not a routine sequence. A reset matching no entries prints zero. `reset-folder album` does not match sibling `album-extra`. Reset-review deliberately requeues reviews, potentially using the same cached evidence again. Reset-progress does not clear caches or manual decisions, so reanalysis can still reuse them. Do not run it between ordinary batches.

### `reset-all`

Syntax: `reset-all [--workspace PATH]`.

```powershell
dotnet $dll reset-all --workspace $workspace
```

Creates a consistent backup under `<workspace>\backups\music-organizer-<UTC timestamp>-<unique suffix>.db`, then prompts for exactly `RESET`. Lowercase, extra spaces or any other answer cancels. Backup creation occurs before confirmation, so cancellation still leaves a backup. Backup failure prevents reset.

On confirmation it clears indexed file rows and processing history, retaining the database/schema. This is a fresh **progress-index** start, not deletion of every organizer artifact: manual JSON, identification cache, reports/plans, target files, playlists and permanent IDs remain. Run scan again to populate the empty index. Cancellation exits 2. This reset is not needed for ordinary restarts or API-configuration changes.

## 5. Option compatibility summary

| Command family | Supported useful options |
|---|---|
| scan | workspace, force |
| status and reset commands | workspace |
| analyze --limit | limit, workspace, reports, online/offline |
| analyze SOURCE / identify SOURCE | workspace, reports, all full-source identification options |
| resolve | Same as full-source identification, plus only-unresolved/edit-manual |
| plan | workspace, reports, full-source identification options |
| apply | workspace, reports, plan, dry-run |
| playlists | workspace |
| doctor | workspace, cache, offline |
| manual validate | workspace |

Use only the options listed for a command. Some shared-parser options may be accepted but have no useful effect on a particular command; acceptance is not evidence that a setting was applied. There is no automatic background scheduling or shell loop in this guide.

## 6. Persistent files, reports and backups

| Location | Purpose |
|---|---|
| workspace/music-organizer.db | Progress inventory, statuses, observations and history |
| workspace/music-organizer.lock | Single-writer coordination; file can remain after normal exit |
| workspace/cache.db | Fingerprints, raw API responses and automatic resolutions |
| workspace/manual-resolutions.json | Authoritative human decisions |
| workspace/manual-resolutions.csv | Readable overview; never an input |
| workspace/manual-resolutions.json.bak-* | Previous JSON versions from confirmed edits |
| workspace/backups/*.db | Consistent progress reset-all backups |
| reports/analysis-* or identification-* | Full-source report runs |
| reports/progress-* | Incremental indexed identification exports |
| reports/plan directories | Saved JSON copy plans and supporting CSVs |
| target/playlist-map.json | Permanent artist/album identities and codes |
| target/playlist-index.csv | Human-readable code-to-playlist index |
| target/.mp3organizer.json | Managed-target marker |
| target/.mp3organizer-metadata.json | Effective copied-file metadata without tag rewriting |
| target/.mp3organizer-backups | Previous managed control/playlist files |

CSV report availability varies by command. Full-source reports include inventory/missing-tag/duplicate information; plan adds planned copy/conflict/album/playlist information. Incremental --reports exports identification data, not a new copy plan. Inspect column headers for original/effective metadata, ReviewReason, field provenance, cache hits and manual override flags.

Keep the workspace when moving to another terminal/session. Copying only the executable does not transfer progress. Avoid copying a live SQLite main file alone while WAL writes are active; use a consistent backup or stop all organizer processes before copying the workspace. No automated database restore command is implemented. Restoring old progress state does not roll back target audio or playlist mappings.

## 7. Common workflows and troubleshooting

### Resume tomorrow

Set the same DLL/workspace variables and API environment, then run status and another `analyze --limit 200 --online`. Do not run reset-progress. A normal scan is optional if the collection changed; it is not a reason to lose completion.

### Correct one album

Run `resolve '<album folder>' --edit-manual` with the same workspace. Confirm the full path above each question. After changing metadata, create a new copy plan; if only managed playlist metadata needs refreshing on an existing target, run playlists. Neither command rewrites audio tags. Direct JSON edits become effective on reload; reset the indexed scope and analyze again if you need its persisted progress result refreshed.

### Online lookup disabled

- `--online not specified`: add --online if network identification is desired.
- `AcoustID API key not configured`: set `$env:ACOUSTID_API_KEY` in the executing terminal.
- `MusicBrainz contact not configured`: set `$env:MP3ORGANIZER_CONTACT`.
- `fpcalc executable not found`: set the executable path and run doctor.

An enabled message followed by per-file errors is a service/decoder issue, not a missing opt-in. Check identification reports and doctor. Repeated NeedsReview can mean ambiguous release evidence rather than connectivity failure.

### A command appears busy

Full-source scanning, metadata hashing, fingerprinting, APIs and resolution are separate stages. Reading a large/network file can take time. Current full-source scan/read and identification flows show elapsed activity and waiting messages. Incremental scan prints changed/new files and a final count; it does not provide a heartbeat during every individual scan read. Incremental analysis includes waiting updates. A message naming AcoustID/MusicBrainz can mean cache access, not necessarily a new request.

### Why is it asking me for a known number/title?

In --edit-manual mode this is intentional: you requested editing completed decisions. Enter accepts the default. With unresolved-only mode, a retained review reason may still prompt for a plausible suggestion. A filename hint is not proof of recording identity. Existing manual values, including mistakes, are preserved until explicitly corrected.

### Why does status show zero after restarting?

Check the workspace path first. A different working directory or unset environment variable can create a different empty workspace. Reuse the absolute path from the initial scan. `status` does not discover source files by itself.

### Analyze --limit processes zero, but work remains

Check the breakdown: NeedsReview and Error entries are deliberately excluded from automatic retries. Resolve reviews, or fix errors and reset-errors. Use reset-review only when you intentionally want automatic analysis retried. New files require scan.

### Playlist says file not found

Open it from its original target location and verify each relative entry from that folder. Do not move only the playlist. Regenerate with the current build for Windows backslashes. If the files exist, the player's import/path handling may be the remaining issue; a different separator is not proof that all players will succeed.

### Plan validation fails

Use the same source/target/workspace/reports as plan. Check whether manual JSON, source inventory/content, target files or playlist mapping changed. Generate a fresh plan. Do not bypass checks by editing checksums or manually modifying control files.

### A lock or executable is in use

Let the existing process finish. Do not delete the workspace lock to defeat an active process. Do not overwrite a running DLL or edit an active batch launcher; earlier in-place batch editing produced command-fragment errors in a synthetic reproduction. The exact prior localized Windows filename-syntax error was not conclusively established. Direct `dotnet $dll` avoids that launcher-resume path without hiding errors.

### Exit codes

| Code | Typical meaning |
|---|---|
| 0 | Command completed; review warnings may still exist |
| 1 | Configuration, validation, I/O, database or other handled error; doctor failure |
| 2 | Missing command, or cancelled reset-all |
| 3 | Incomplete scan/full analysis, blocking plan conflicts, or incremental file-analysis errors |

After any command inspect `$LASTEXITCODE`. Exit 0 is not a promise that all metadata was confidently identified. In particular, reference warnings from manual validate and NeedsReview results require reading the report/status output.

## 8. Build, tests and implementation references

From the solution directory:

```powershell
.\build-offline.ps1 -OutputDirectory artifacts-progress-state
dotnet .\artifacts-progress-state\Mp3Organizer.Tests.dll 'C:\Users\tglaz\Documents\Codex\Mp3Organizer-test-scratch'
```

The offline script uses installed .NET 10 SDK/reference assemblies and an installed TagLibSharp 2.3.0 DLL. `-TagLibDll PATH` overrides that dependency location. Do not rebuild into an output currently in use; choose another output directory and update `$dll` for the next run.

Normal SDK commands, for an environment with working NuGet configuration:

```powershell
dotnet restore Mp3Organizer.slnx --configfile NuGet.Config
dotnet build Mp3Organizer.slnx --no-restore -c Release
dotnet run --project tests/Mp3Organizer.Tests/Tests.csproj -c Release --no-build
```

Tests use a dependency-free executable runner; `dotnet test` is not its entry point. The latest application build passed 169 tests with synthetic files and mocked identification. Standard NuGet/MSBuild restore remains unverified in the restricted development environment. Native SQLite makes this implementation Windows-specific. This documentation change does not install tools, configure your environment or run the real-library commands.

Further detail: [progress architecture/schema](PROGRESS.md), [progress SQL](progress-schema.sql), [identification internals](IDENTIFICATION.md), [manual/cache workflow](WORKFLOW.md), [manual JSON schema](manual-resolutions.schema.json), and [cache SQL](cache-schema.sql). Older workflow notes describe the evolution of the app; this guide's build path and keyboard controls take precedence for the current documented build.
