# Normal workflow: scan, then run

Use the current **artifacts-review-scope** build. `scan` finds files; `run` performs pending analysis and then organized copying automatically. API configuration remains documented in [RECOGNITION.md](RECOGNITION.md). The lower-level commands remain available for diagnostics.

```powershell
$dll = 'C:\Users\tglaz\Documents\Codex\2026-09-26\build-the-first-read-only-version\outputs\Mp3Organizer\artifacts-review-scope\Mp3Organizer.dll'
$workspace = 'E:\Mp3-Ai-test-workspace'

dotnet $dll scan 'E:\Mp3-Ai-test' --workspace $workspace
dotnet $dll run --online --workspace $workspace
```

These example commands were not run against your music during development. No old progress reset is required. When you add folders, repeat the same two commands.

## Target and reports

By default each indexed source uses its sibling `<source>-organized` directory: `E:\Mp3-Ai-test` maps to **`E:\Mp3-Ai-test-organized`**. A nonempty existing target must already be managed by this organizer for the same source; unmanaged directories are never adopted or overwritten. The default reports directory is `<workspace>\reports`.

To choose a different target on the first run:

```powershell
dotnet $dll run --online --workspace $workspace --target 'E:\MP3-organized' --reports 'E:\Mp3-Ai-test-reports'
```

The target choice is persisted in the same SQLite database and reused on later runs without `--target`. Reports overrides apply to the current invocation; otherwise workspace/reports is used. Target changes are refused once that source has Processed entries; use a separate workspace for a different target. Explicit `--target` requires one indexed source root. Multiple roots otherwise receive separate sibling targets, checked for overlaps with every source, workspace and other target.

## What run does

1. Load the existing progress index, migrate legacy metadata under the folder-independent policy, and validate configured targets. Independently valid old Ready results remain Ready; unsupported folder-derived results return to Discovered for reassessment. See [FOLDERS.md](FOLDERS.md) for backup/audit behavior. Run `scan` first after adding/changing files.
2. Analyze `Discovered` and retryable `Analyzed` entries in case-insensitive full input-path order. Each file first enters retryable `Analyzed`; the result becomes `Ready`, `NeedsReview`, or `Error`. Existing deterministic recognition, LLM hard validation, caching, manual overrides and quota choices are reused.
3. Continue to the downstream stage for **all eligible Ready entries**, including files already Ready before this invocation. Analysis is not repeated for them. Pending analysis is performed first, then the Ready stage; each stage is ordered deterministically by source/path, not discovery time. There is no new-file priority.
4. Validate Ready source content, build a selected-inventory copy plan with the existing duplicate/path/playlist services, save its JSON/checksum and CSV reports, reload it, and execute it through the existing guarded validator/copy executor.
5. After successful verified copying and playlist generation, persist `Processed` for the fulfilled Ready entries. Byte-identical duplicates selected out of the copy set are fulfilled by their verified equivalent target copy and also become Processed. Non-identical candidates remain separate under the existing conservative rules.
6. Regenerate Folder playlists using the source-to-target mappings persisted after successful copying. This stage also runs with zero Ready files, preserving repetitions and updating scanned membership/order changes. Pending members are omitted and counted. A failure during this final regeneration is retryable on the next run; already verified copies remain Processed.
7. During that final regeneration, also rebuild discovered source playlists under `_Playlist-Folder\[PL-0001] <name>.m3u8`, using verified canonical targets. Order/repetitions are retained, unresolved entries are reported, and permanent playlist codes are included in the index. See [PLAYLIST-IMPORT.md](PLAYLIST-IMPORT.md).

`Ready` is an intermediate state meaning **Ready to apply**, not completion. `run` carries valid persisted Ready entries through the downstream stage. Legacy Ready metadata must first pass the migration audit; unsupported results are reassessed instead of blindly copied.

## Status and exclusions

```text
Processed:       100
Ready to apply:   10
Needs review:      3
Pending analysis: 20
```

`Pending analysis` counts Discovered + Analyzed. The completion percentage counts Processed + Skipped, excluding Ready. NeedsReview and Error remain excluded.

| Current state | Normal run action |
|---|---|
| Discovered / Analyzed | Analyze, persist result; eligible results continue to copying |
| Ready | Copy/organize automatically; then Processed |
| NeedsReview | Leave untouched; no source tags, target copy or completion |
| Error | Leave untouched; explicit reset or changed-file scan is needed |
| Processed | Skip source analysis and new copy planning |
| Skipped | Skip |

The existing target is read to preserve playlists, permanent IDs and managed metadata. This does not reprocess completed source files. Unresolved files already copied by an older build are not automatically removed from the target.

`review --workspace ...` later handles NeedsReview. Confirmation sets Ready; the next `run` applies it. The existing review `S` action remains permanent Skipped, not defer-until-next-review. The lower-level plan/apply paths now also filter/block pending, review, error and skipped indexed entries, and the progress recorder will not mark them Processed after apply. A stale plan is rejected if its indexed operation is no longer eligible. Existing unindexed standalone planning remains available.

## Scanning and restart behavior

Every `scan <source>` recursively enumerates the whole input tree from the beginning. Unchanged size/time observations preserve existing progress and avoid re-reading audio metadata/hash. Newly encountered nested folders/files are added as Discovered. `scan --force` re-reads all file content; identical content retains its state. Changed content returns to Discovered. Missing/unreadable files are reported; nothing is deleted.

Progress is persisted per analysis and per successful completion record. A quota stop preserves the current retryable Analyzed file and ends `run` before copying. Rerun the same command later; already completed records are skipped. Choosing “continue without Codex” lets deterministic processing and eligible copying continue, with unresolved files left NeedsReview.

A Ready file that changed/disappeared before copying is marked Error without preventing other valid Ready files from proceeding. A plan/target validation or copy/playlist failure stops the downstream stage without marking that plan completed. Ready entries remain available for retry; the next run creates a fresh plan and recognizes verified existing copies as AlreadyPresent. Previously completed records remain Processed. Partial target files are retained for inspection according to the existing copy executor's recovery rules.

## Options and safety

```text
run [--online|--offline] [--workspace PATH] [--target PATH] [--reports PATH]
    [--codex|--no-codex] [--write-tags] [--limit N]
```

No limit means all pending analysis. `--limit N` bounds the analysis phase only; the subsequent stage handles all eligible Ready files. `--offline` does not prevent copying—it disables online identification. `--no-codex` explicitly disables the LLM while allowing structured online lookup with `--online`.

Source tags remain read-only by default. `--write-tags` retains the existing explicit, verified tag-write policy during analysis; it does not retroactively retag already Ready/Processed entries. Audio is never re-encoded; source originals are never moved, renamed or deleted. Organized filenames exist only in target copies. Reports/workspace must remain outside source and target.

Return codes: 0 = run completed (review items may remain); 1 = configuration/plan/global operation failure; 3 = newly encountered per-file analysis/preflight errors; 4 = clean Codex stop with progress saved. Existing Error rows alone do not fail an otherwise successful run. LLM batch and cumulative statistics are unchanged.

To inspect/simulate copying instead of executing it, retain the lower-level `analyze`, `plan`, and `apply --dry-run` workflow. Normal `run` performs the saved, validated plan automatically and does not ask for a separate apply confirmation.





