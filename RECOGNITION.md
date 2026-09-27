# Runtime recognition, original years and reviewed MP3 tag edits

For normal use, start with **[scan + run](RUN.md)**. The analyze examples below remain useful for inspecting recognition without automatically applying copies. `run` supports the same online/LLM/tag options and adds downstream processing of Ready files.

See [LLM observability](OBSERVABILITY.md) for actual HTTP-attempt messages, per-batch counters and persisted cumulative statistics.

This extends the existing application, caches and `music-organizer.db`. Use the **artifacts-playlist-import** build. No real music library or live recognition service was accessed during implementation.

## Recommended quick start

Run in PowerShell, replacing credentials and the model name with values for your accounts:

```powershell
$dll = 'C:\Users\tglaz\Documents\Codex\2026-09-26\build-the-first-read-only-version\outputs\Mp3Organizer\artifacts-playlist-import\Mp3Organizer.dll'
$source = 'E:\Mp3-Ai-test'
$workspace = 'E:\Mp3-Ai-test-workspace'
$reports = 'E:\Mp3-Ai-test-reports'
$target = 'E:\Mp3-Ai-test-organized'

$env:ACOUSTID_API_KEY = 'YOUR_ACOUSTID_APPLICATION_KEY'
$env:CHROMAPRINT_FPCALC = 'C:\Tools\Chromaprint\fpcalc.exe'
$env:MP3ORGANIZER_CONTACT = 'your-real-email@example.com'
$env:OPENAI_API_KEY = 'YOUR_OPENAI_API_KEY'
$env:MP3ORGANIZER_CODEX_MODEL = 'YOUR_ACCESSIBLE_RESPONSES_MODEL_WITH_STRUCTURED_OUTPUTS'

dotnet $dll doctor --workspace $workspace
dotnet $dll scan $source --workspace $workspace
dotnet $dll analyze --limit 10 --online --workspace $workspace --reports $reports
dotnet $dll status --workspace $workspace
dotnet $dll review --workspace $workspace
```

This first analysis writes database/reports, not MP3 tags. To enable actual tag updates for subsequent batches:

```powershell
dotnet $dll analyze --limit 10 --online --write-tags --workspace $workspace --reports $reports
dotnet $dll review --write-tags --workspace $workspace
```

Completed entries are skipped. If you first analyzed without `--write-tags`, reset only the particular file/folder you want to analyze again with tag writing; simply adding the flag does not revisit completed entries:

```powershell
dotnet $dll reset-file 'E:\Mp3-Ai-test\small-folder\one.mp3' --workspace $workspace
dotnet $dll analyze --limit 10 --online --write-tags --workspace $workspace
```

Reset does not erase lookup caches or confirmed manual overrides. Prefer this small-folder test before enabling writes on a larger collection. None of these example commands has been executed against your music.

## Configuration and service boundaries

* AcoustID needs an **application** key from its [web service documentation](https://acoustid.org/webservice). `ACOUSTID_API_KEY` is unrelated to the OpenAI key.
* Install [Chromaprint/fpcalc](https://acoustid.org/chromaprint); `CHROMAPRINT_FPCALC` supplies its executable path, otherwise the existing PATH search applies. Fingerprinting runs locally.
* MusicBrainz public lookup needs no API key. `MP3ORGANIZER_CONTACT` supplies a real contact in the User-Agent. Existing request limiting and persistent cache remain in use.
* Runtime Codex/LLM reasoning uses the **OpenAI Responses API**, with `OPENAI_API_KEY` and an explicitly chosen `MP3ORGANIZER_CODEX_MODEL`. No model is assumed. Choose an API-accessible model supporting [structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs). It does not control this Codex desktop chat or read its account usage limits. API credentials, usage and billing are separate from the desktop session.
* `doctor` still checks fpcalc, AcoustID configuration, MusicBrainz, cache writability and manual JSON. It does not send an OpenAI inference request or validate model access. Missing OpenAI settings are reported clearly when the first ambiguous file needs reasoning; that file stays pending.
* `--online` on **incremental** `analyze --limit N` enables deterministic online lookup plus runtime Codex. `--no-codex` explicitly uses deterministic recognition only. `MP3ORGANIZER_CODEX_ENABLED=false` disables the LLM by default; `--codex` overrides that setting. `--offline` cannot be combined with `--codex`.
* `--codex` without `--online` permits LLM reasoning over locally cached factual candidates, while AcoustID/MusicBrainz stay offline. The LLM still uses the internet. Use `--offline` to prevent both kinds of online work.
* Full-source `identify`, `analyze <source>`, `resolve` and `plan` retain their deterministic workflow. Runtime LLM and its resumable failure handling are provided by `scan` → `analyze --limit`. These modes say so explicitly. `plan` reuses accepted, unchanged Codex results from the same workspace and reapplies current manual overrides.

Keep keys out of source files, reports and shared logs. The app never logs the OpenAI key. It sends filenames, full folder paths, tags, duration, bitrate, up to 30 nearby tracks, fingerprint lookup evidence, and factual candidate metadata to OpenAI. **It sends no MP3/audio bytes.** The request uses `store:false`; this is not a claim about every provider retention policy. No model tools, shell access, web browsing or file access are enabled in these requests.

## How automatic decisions work

1. Existing quality detection rejects placeholders; manual overrides and reliable tags retain priority.
2. Chromaprint/AcoustID and MusicBrainz provide factual recordings/releases. Candidate evidence is persisted, including rejected/ambiguous alternatives.
3. Deterministic scoring uses fingerprint (.55), duration (.15), artist (.08), title (.08), album (.06), track (.03), folder as weak supporting context (.025), and no neighboring-track vote (0). These are evidence weights, not calibrated probabilities. A unique recording/release with strong fingerprint (at least .95), matching duration and enough context can be accepted without confirmation.
4. Missing years use exact Artist/Album release-group searches. Album queries are normalized and cached in memory and SQLite. A unique complete result with a valid original first-release year can enrich the file. Truncated searches, conflicting years, live/remix/version doubts and incomplete album identities stay in review. A later edition date alone is not an original-year source.
5. Remaining ambiguity goes to the LLM. Its JSON includes selected candidate ID, release, year, confidence, short reason, human-review flag and conflicts. The response cannot invent new metadata: accepted values are taken from the selected factual candidate.
6. Hard validation requires a known candidate, supported release/year, preserved existing year, strong fingerprint and duration evidence, sufficient context score and at least .05 factual score separation from materially different candidates. LLM confidence alone never establishes correctness. Version conflicts and contradictions with reliable embedded artist/title prevent automatic acceptance. Year-only ambiguity lacking independent evidence remains human review even if the LLM sounds confident.

Example automatic case: AcoustID .99, matching 100-second duration, album/title/track context consistent, a unique original release group from 1991. The app accepts the metadata and fills missing Year=1991. With `--write-tags`, that year is written after validation.

Example review case: two similarly supported studio/live recordings or two plausible release groups with different original years. The LLM evaluates them, but without independent evidence distinguishing them the app keeps `NeedsReview` and does not write tags.

## Quota, errors and resuming

OpenAI failures are distinguished as quota, rate limit, temporary service/network, authentication/configuration, or invalid request/model. See the [official error categories](https://developers.openai.com/api/docs/guides/error-codes). Quota errors are not retried. Rate limits and temporary failures have at most three attempts, bounded waits, and a 90-second request timeout; the batch heartbeat continues every three seconds.

Before asking what to do, the current deterministic result/candidates are committed to SQLite as `Analyzed`, with the Codex failure recorded. `Analyzed` is a retryable pending state, not a completed or failed result.

```text
Progress saved. Codex Quota: OpenAI API usage/quota limit reached.
[1] Continue without Codex
[2] Stop now and resume later
Select 1 or 2:
```

Choice 1 disables LLM calls for the rest of this process and sends remaining ambiguous cases to `NeedsReview`, with `DeterministicWithoutCodexFallback` and `CodexUnavailable` provenance. It does not ask again for each file. Deterministically complete results can still finish normally.

Choice 2 exits with code **4** before processing another file. Rerun the same command later: the waiting file is retried; completed files are skipped. No reset is needed. The run-wide disable flag is not persisted, so the next run attempts Codex again. Redirected input stops safely instead of assuming consent to continue. Authentication/configuration and invalid model/request failures stop immediately with a corrective message and preserve pending state; they do not repeatedly retry or masquerade as quota failures.

## Review command

```powershell
dotnet $dll review --workspace $workspace
dotnet $dll review --workspace $workspace --write-tags
```

Only `NeedsReview` rows are presented. The screen shows path, original/current metadata, duration, bitrate, candidates with evidence scores, and reasons. Enter selects candidate 1; 1–9 selects another; M enters metadata; P opens the current file in the configured system player; S marks it Skipped. EOF stops cleanly. Selection/manual changes require typing `YES` before saving. File content is rechecked before acceptance. Missing candidate values do not erase useful current fields. Meaningful artist, album, title and track number are required; Year may remain empty by manual decision.

Confirmed metadata remains in the existing SHA-keyed `manual-resolutions.json` override mechanism. Processing status, evidence, candidates and review reasons remain in `music-organizer.db`; no second review-state JSON file is introduced. Normal confirmed decisions are committed immediately and are not shown again. If interrupted between saving a confirmed JSON override and the SQLite status commit, the JSON decision survives; the entry may need reconciliation/review again. With tag writes, the write journal recovers a file replacement that completed before its DB commit.

## Tag-write boundary and recovery

`--write-tags` is supported only by incremental `analyze --limit` and `review`. Without it, the source is read-only. Scanning, planning, applying copies and the legacy resolver never write source tags.

Automatic writes require a resolved high-confidence match or verified year enrichment. Review/ambiguous results cannot write. Useful existing artist/album/title/track/disc values are retained automatically; missing/junk fields may be filled. A nonzero existing Year is always retained automatically. Manual review may explicitly change it after `YES` confirmation. Only four-digit year values are written, never full dates. No encoder is invoked.

For each write the app checks content identity, saves a complete backup under `workspace/tag-writes`, stages an MP3 beside the original, writes tags on that staging copy using TagLibSharp, and verifies the exact MPEG audio payload SHA-256 is unchanged. Unsupported MP3 layouts fail closed. It then rechecks the source SHA and atomically replaces the file. The original full-file backup is retained. Temporary staging files are cleaned up; a crash-left staging file is excluded from scans and reported for inspection. Read-only filesystem permissions or replacement failures leave the original untouched.

The Prepared/Committed journal reconciles interrupted writes with SQLite on the next progress command. Confirmed manual SHA identities are extended to the new tagged file hash; old identities remain for unchanged duplicate copies. Do not delete backups/journals during a pending operation. Retagging changes whole-file hashes and invalidates old copy plans: run `plan` again before `apply`.

## Database and reports

`music_files.effective_json` persists all per-field provenance plus `RecognitionMethod` (`Deterministic`, `Codex`, `Manual`, or `DeterministicWithoutCodexFallback`), model, structured decision/confidence/reason, validation result, failure kind and unavailable flag. `recognition_evidence` stores confidence, candidates, review reasons and year provenance in the same transaction as file status/history. The additive table preserves existing indexes and rows.

`identification.csv` adds these diagnostic fields. `status` adds Auto recognized, Year enriched, No match, Codex examined, Codex unavailable, Waiting for Codex and grouped review reasons. Completed files remain excluded until changed or explicitly reset. New scans add new files without resetting old progress.

After resolving, use the existing `plan`, `apply --dry-run`, `apply`, and `playlists` commands. Keep the same workspace throughout so plans can reuse accepted AI decisions and manual overrides.

## Verification

Build with `./build-offline.ps1 -OutputDirectory artifacts-playlist-import`. Run the dependency-free test executable using `dotnet artifacts-playlist-import/Mp3Organizer.Tests.dll <scratch-folder>`. Tests mock OpenAI, AcoustID and MusicBrainz and generate synthetic WAV/MP3 data. Standard NuGet/MSBuild restore and a real provider/model inference remain unverified in this environment.



