# Mp3Organizer — quick start

**[Complete command guide: configuration, every command, examples and troubleshooting](COMMANDS.md)**

Use the latest build directly with `dotnet`, rather than an older `.cmd` launcher. Start each PowerShell session with:

```powershell
$dll = 'C:\Users\tglaz\Documents\Codex\2026-09-26\build-the-first-read-only-version\outputs\Mp3Organizer\artifacts-progress-state\Mp3Organizer.dll'
$source = 'E:\Mp3-Ai-test'
$target = 'E:\Mp3-Ai-test-organized'
$workspace = 'E:\Mp3-Ai-test-workspace'
$reports = 'E:\Mp3-Ai-test-reports'
```

Configure online identification using your AcoustID **application key**, actual fpcalc executable path and real MusicBrainz contact:

```powershell
$env:ACOUSTID_API_KEY = 'YOUR_ACOUSTID_APPLICATION_KEY'
$env:CHROMAPRINT_FPCALC = 'C:\Tools\Chromaprint\fpcalc.exe'
$env:MP3ORGANIZER_CONTACT = 'your-email@example.com'

dotnet $dll doctor --workspace $workspace
```

Obtain the application key through the [official AcoustID documentation](https://acoustid.org/webservice); obtain fpcalc from [Chromaprint](https://acoustid.org/chromaprint). The [complete guide](COMMANDS.md) explains persistent configuration, MusicBrainz setup, diagnostics and offline use. These example values must be replaced before online use.

Recommended incremental workflow:

```powershell
dotnet $dll scan $source --workspace $workspace
dotnet $dll analyze --limit 200 --workspace $workspace --online --reports $reports
dotnet $dll status --workspace $workspace
```

Repeat `analyze --limit 200` next session with the same workspace to continue. Do not reset progress between batches. Rescan when the collection changes. Online access requires explicit `--online`; use `--offline` for local/cached analysis.

Review uncertain entries, then plan and simulate:

```powershell
dotnet $dll resolve $source --workspace $workspace --reports $reports --only-unresolved
dotnet $dll plan $source $target --workspace $workspace --reports $reports
dotnet $dll apply $source $target --workspace $workspace --reports $reports --dry-run
```

After inspecting the plan, explicitly copy selected files with:

```powershell
dotnet $dll apply $source $target --workspace $workspace --reports $reports
```

Full-source resolve/plan are not limited to the last analysis batch. The source is immutable; apply writes only organized target copies/control files. Use `--edit-manual` to revisit completed decisions. Controls are **Enter = accept, Backspace = delete text, Alt+Left = previous, Esc = skip/abort folder**.

These examples have not been executed against your library while writing this documentation. Keep workspace and reports outside both source and target.

Further references: [all commands](COMMANDS.md), [incremental progress/schema](PROGRESS.md), [identification details](IDENTIFICATION.md), [manual/cache internals](WORKFLOW.md). Historical build paths in older notes should not replace the current DLL path above.

## Build and test

Normal SDK build:

```powershell
dotnet restore Mp3Organizer.slnx --configfile NuGet.Config
dotnet build Mp3Organizer.slnx --no-restore -c Release
dotnet run --project tests/Mp3Organizer.Tests/Tests.csproj -c Release --no-build
```

The tests use a dependency-free executable test runner with assertions and nonzero exit status on failure; `dotnet test` is not the runner for this solution. Tests create synthetic WAV fixtures in an isolated directory; an optional argument selects their parent directory. They never access your real library. Fixtures are retained for inspection.

This environment denied access to the user-level NuGet configuration and rejected an escalation request. Both projects were therefore compiled with the installed .NET 10 Roslyn compiler and reference assemblies, using the already installed TagLibSharp package, without accessing that configuration or downloading dependencies:

```powershell
.\build-offline.ps1 -OutputDirectory artifacts-progress-state
dotnet artifacts-progress-state/Mp3Organizer.Tests.dll "path/to/test-scratch"
```

The offline script accepts `-TagLibDll` for a different installed TagLibSharp 2.3.0 DLL path. It treats warnings as errors. The current prebuilt application and tests are in `artifacts-progress-state`. Standard NuGet/MSBuild restore remains unverified in this restricted environment.

## Safety and execution

- Source file handles use `FileMode.Open`, `FileAccess.Read`, and read-only sharing. The TagLib abstraction rejects write streams, including attempted `Save()` calls. Services do not expose source rename, delete, move, or tag-save operations.
- The configured source and the supplied real-library path are protected from output writes. Overlapping roots, directory/file links and junctions, device paths, alternate data streams, path traversal, and ambiguous Windows names are rejected.
- First use requires an empty or absent target. Later runs require its managed marker and matching source. `playlists` only operates on a managed target.
- Planning writes reports and a checksummed JSON plan outside both libraries. It makes no target changes. Apply validates every source audio hash, the source inventory, the complete target snapshot, destination paths, mapping continuity, and playlist references before copying. `--dry-run` performs these checks and writes nothing.
- Audio copies use uniquely named target partial files, SHA-256 verification, and a final move with overwrite disabled. Existing matching destinations are `AlreadyPresent`; collisions get a deterministic suffix in the plan. Existing different audio is never overwritten.
- Only managed mapping/playlist/control files are deliberately replaced. Previous versions are backed up under `.mp3organizer-backups`. Unmanaged playlist collisions block the operation. Old managed playlists become empty if their tracks disappear; they are not deleted.
- After an interruption, run `plan` again: verified completed copies become `AlreadyPresent`. Partial target files remain for inspection. A missing/corrupt mapping stops processing; restore it from backup or recover its contents from the saved plan before proceeding. There is no automatic destructive cleanup.
- No filesystem application can guarantee immutability against another process changing directory mappings concurrently or against externally configured aliases to the same share. Use a source account with read-only share/filesystem permissions for an OS-enforced boundary. The tests prove the application service paths reject source mutations; they do not assert control over other processes or server configuration. Run one organizer process per target at a time.

## Metadata, duplicate policy, and paths

Supported extensions (case-insensitive): `.mp3`, `.flac`, `.m4a`, `.aac`, `.ogg`, `.wav`, `.wma`. Recursive scanning reports inaccessible entries and skips links. Plan/apply fail closed if source or target metadata cannot be read completely.

Metadata includes path, filename, artist(s), album artist, album, title, track, disc, year/date, duration, extension, bitrate, codec, size, and SHA-256. Full ID3v2 TDRC and Vorbis DATE values are retained when available; other formats fall back to year. Raw tags remain unchanged. Normalization uses Unicode NFKC, invariant uppercase, trimming, and whitespace collapsing; it preserves punctuation and accents.

Candidates have matching normalized Artist + Title and durations within three seconds of every member of their group. Unknown/zero durations and missing identities are excluded. Non-identical files are retained for review regardless of apparent quality. Only matching SHA-256 plus size causes an automatic skip; the lexically first source path is selected deterministically. `IDuplicateEquivalenceVerifier` is the extension point for future acoustic evidence. Decisions and their evidence already belong to the saved plan model. `AudioQualityComparer` supplies lossless/bitrate policy for verified equivalents, but quality never establishes equivalence and does not eliminate different encodings in version 1. Lossless bitrate is not used as a fidelity ranking.

Paths:

```text
Artist/YYYY - Album/NN - Title.ext
Artist/Title.ext
Various Artists/YYYY - Album/NN - Title.ext
```

Album folders use AlbumArtist, falling back to Artist. Missing values become `Unknown Artist`, `Unknown Year`, the original filename stem, and track `00`. Disc 2 onward uses `D02 - NN - Title.ext`. Invalid characters and reserved names are sanitized; application-owned top-level names are escaped. Long paths block planning rather than truncate silently. Same album title with different years is treated as separate albums. Separate releases with identical owner/title/year are not distinguished in version 1.

## Persistent identities and playlists

`playlist-map.json` stores normalized lookup keys separately from original display names. Both counters are monotonic. Entries remain even when artists/albums disappear. Renumbering, removing existing identities, lowering counters, or reusing IDs is rejected. A changed normalized artist name or album title/year is treated as a new identity; automatic artist merging is intentionally absent.

```json
{
  "schemaVersion": 1,
  "nextArtistId": 124,
  "artists": {
    "MONIKA BRODKA": {
      "artistId": 123,
      "displayName": "Monika Brodka",
      "nextAlbumId": 2,
      "albums": {
        "GRANDA|2010": { "albumId": 1, "displayName": "Granda" }
      }
    }
  }
}
```

Artist IDs start at 1. Album IDs start at 1 per artist and are permanently limited to 99 so concatenation is unambiguous. Exceeding that limit fails explicitly. Example filenames:

```text
_Playlists/Artists/Code 123 - Monika Brodka.m3u8
_Playlists/Albums/Code 12301 - Monika Brodka - Granda.m3u8
```

M3U8 uses UTF-8 without BOM and paths relative to the playlist directory. Albums sort by disc, track, then path. Each separately tagged performer receives an artist playlist, including compilation tracks. No global Various Artists artist playlist is generated; compilation album playlists are generated normally. Missing track artists use Unknown Artist.

### Human-readable code index

Every `apply` and `playlists` run generates `playlist-index.csv` in the target root, encoded as UTF-8 without BOM. Columns are `Code,Type,Artist,Album,PlaylistPath`. Artist rows come first, sorted numerically by artist ID; album rows follow, sorted numerically by artist ID and album ID. Paths are relative to the target root and use Windows backslashes. Quoting handles commas and quotes in display names.

The index is a read-only projection of `playlist-map.json` and the generated playlist set: deleting or losing the index never assigns new IDs. Regeneration restores it from the same mapping. It includes retained empty playlists for retired identities, but no global Various Artists artist entry. An existing user-owned file with that name is not overwritten; managed replacements receive the same backups as playlists.

Plan report directories also contain a preview `playlist-index.csv`. Their `albums.csv` includes a `Code` column with the same spoken album code. Source-only analysis leaves that column blank because it has no target identity mapping; it does not allocate IDs to populate the report.

## Reports

Each analysis/plan has its own report directory. Planning produces `library.csv`, `missing-tags.csv`, `duplicates.csv`, `conflicts.csv`, `albums.csv`, `copy-plan.csv`, `copy-plan.json`, and its SHA-256 checksum. Analysis produces the applicable inventory reports without a copy plan. CSV uses UTF-8, quoted fields, invariant numeric formatting, and spreadsheet formula neutralization.

Summary album counts use normalized album owner/title/year. Loose tracks mean missing album tags. Unreadable files are counted in total files and errors but not misclassified as confirmed missing tags. `conflicts.csv` distinguishes blocking issues from resolved filename collisions. Exact destination paths appear in `copy-plan.csv`.
