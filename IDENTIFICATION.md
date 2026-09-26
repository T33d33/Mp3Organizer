# Optional acoustic identification

Source audio remains immutable. Neither embedded tags nor copied target audio tags are rewritten. The only data sent to AcoustID is the compressed fingerprint, whole-file duration, application key, and lookup parameters. MusicBrainz receives recording IDs and metadata-query parameters. Audio bytes, local filenames, paths, and embedded tags are never uploaded.

## Commands and configuration

`identify` produces reports without copying audio. Online identification is explicitly opt-in:

```powershell
$env:ACOUSTID_API_KEY = "YOUR_APPLICATION_KEY"
$env:CHROMAPRINT_FPCALC = "C:\Tools\Chromaprint\fpcalc.exe"
$env:MP3ORGANIZER_CONTACT = "your-email@example.com"

.\Mp3Organizer.cmd identify "C:\Music-test" --reports "C:\Music-test-reports" --online
```

Use a small folder you populate with test copies. The command above has **not** been executed against your files. Get an **application** key by registering an application through the [AcoustID web service documentation](https://acoustid.org/webservice). No user submission key is needed. Environment values are read at runtime and are not stored in reports, plans, or cache keys.

Install/extract the official Windows [Chromaprint fpcalc package](https://acoustid.org/chromaprint), which provides `fpcalc.exe` with audio decoding support. Set `CHROMAPRINT_FPCALC` to its executable or put it on PATH. No extra NuGet package is required. The application invokes `fpcalc -json -algorithm 2 -length 120 -- <absolute-source-path>` without a shell and holds a read-only source handle while the process runs. Fingerprinting happens locally. The first 120 seconds produce the fingerprint; the reported whole-file duration is sent for lookup.

`MP3ORGANIZER_CONTACT` supplies a real contact email or URL for `Mp3Organizer/1.1 (<contact>)`, the MusicBrainz User-Agent. Both contact and application key are required with `--online`.

Options for `identify`, `analyze`, and `plan`:

| Option | Behavior |
|---|---|
| omitted `--online` | No network calls; use tags, cached identification, then folder fallback. |
| `--online` | Permit lookup for files needing resolution. |
| `--offline` | Explicitly state the default no-network mode. |
| `--identify-all` | Request fingerprint identification even for well-tagged files; cached fingerprints can be reused. Good tags are still preserved. Can run local fingerprinting without network access. |
| `--cache <directory>` | Override the cache location; it must be outside source and target. |

For an exhaustive small-folder test, append `--identify-all` to the online command. Ordinary planning does not call APIs unless you also pass `--online`:

```powershell
.\Mp3Organizer.cmd plan "C:\Music-test" "E:\MP3-organized-test" --reports "C:\Music-test-reports"
```

Use the same `--workspace` across identify, resolve, plan, apply and playlists. Default cache: `<workspace>/cache.db`; workspace defaults to `workspace` under the current directory or `MP3ORGANIZER_WORKSPACE`. See [WORKFLOW.md](WORKFLOW.md) for manual precedence and refresh options. No network or fingerprinting occurs during `apply`; it executes the metadata decisions saved in the plan.

## Architecture and resolution policy

| Component | Responsibility |
|---|---|
| `IAudioFingerprintService` / `ChromaprintFingerprintService` | Local executable invocation; fingerprint cache keyed by source SHA-256. |
| `IAcoustIdClient` / `AcoustIdClient` | Fingerprint-only lookup; parse recording candidates. |
| `IMusicBrainzClient` / `MusicBrainzClient` | Recording and paginated release/track lookup. |
| `IMetadataResolver` / `MetadataResolver` | Select effective fields, preserve originals, report evidence and uncertainty. |
| `MetadataQualityEvaluator` | Missing/generic/malformed fields, filename-like titles, strong folder-album disagreement. |
| `CachedMetadataResolver` | Reuse confident resolutions, including results of prior `--identify-all`; invalidate on file/tag/folder-consensus changes. |
| `IdentificationHttp`, `RequestRateLimiter`, `IdentificationCache` | Throttling, retry/backoff, local response and failure caching. |
| `FolderMetadataFallback` | Low-confidence last fallback; remove uploader/bitrate noise. |
| `TargetMetadataStore` | Preserve effective metadata across future playlist regeneration without rewriting audio. |

Complete plausible embedded artist/title/album tags normally skip fingerprinting and HTTP. Missing year alone does not trigger online lookup. Known generic values, embedded URLs/uploader text, a title ending in an audio extension, and generic numbered titles trigger review/identification. Strong album disagreement requires at least three neighboring tags and an 80% majority. Evaluation uses per-folder summaries to avoid rescanning every neighboring tag for every well-tagged track.

The current conservative policy requires an AcoustID score of at least 0.90 and no other plausible recording (score at least 0.85) within 0.05 of the best score. These scores are matching evidence, **not calibrated probabilities**. Recording duration discrepancies above three seconds trigger review. Good artist/title fields that disagree with the candidate are retained and flagged instead of replaced.

Album releases need agreement with existing album tags or a substantial neighboring album consensus. Year, track, disc, and album artist provide additional evidence. Ties and weak matches leave the effective album, album artist, track/disc/year unresolved; artist/title may still be confidently identified. The resolver never selects the first listed compilation/reissue merely because a recording appears there. Release browsing follows actual page lengths because MusicBrainz may return fewer releases than requested. More than 500 releases, incomplete pagination, or unavailable release data leaves the album unresolved.

If identification is unavailable or unreliable, valid embedded fields are retained and missing/bad fields may use cleaned folder/filename hints. `Cypress Hill - Kingpin OST-uppedByTeedee` can suggest Cypress Hill / Kingpin OST. `Cuphead OST` only suggests an album, never a performer by itself. Such results remain `Review`; the fallback does not represent an online match.

`IdentificationEvidence` keeps original artist, album artist, album, title, track, disc, and year; source (`Tags`, `AcoustIdMusicBrainz`, `FolderFallback`); scores/IDs; confidence, status, review reason, and evidence. Effective fields feed target paths, grouping, playlists, duplicate candidates, and reports.

Plan JSON contains effective target metadata. `apply` persists it in `.mp3organizer-metadata.json`, an application-managed, backed-up sidecar. Future planning/playlist generation uses those fields only while the copied audio's SHA-256 and size still match. A missing managed sidecar stops regeneration instead of silently reverting IDs/names. Original audio tags are untouched.

## Duplicates

Files confidently linked to the same MusicBrainz recording receive a `Recording-<id>` candidate group even if tag titles differ. `duplicates.csv` marks non-identical files `KeepBoth/Review` and can recommend lossless, stronger measured lossless sample properties, or higher bitrate within comparable lossy codecs.

A shared recording ID or first-two-minute fingerprint cannot prove that two masters, remixes, edits, or full recordings are identical. Consequently, acoustic candidates are retained for review; automatic skipping still requires identical SHA-256 and size. Quality suggestions do not authorize an automatic collapse.

## Rates, retries, and caches

The shared in-process AcoustID limiter spaces starts by at least 350 ms; the MusicBrainz limiter uses at least 1,100 ms. Every retry passes through the same limiter. HTTP 408/429/5xx, transport failures, and malformed JSON are retried up to four attempts with exponential delays (1, 2, 4 seconds) and Retry-After support. Other HTTP errors stop retries. Error messages exclude request bodies and keys.

SQLite stores successes and fingerprints without expiry. No-match/ambiguous results expire after 30 days by default (`--identification-retry-days`); transient failures after five minutes. Expired negatives are not reused offline. `--rebuild-fingerprints` recomputes locally; `--refresh-identification --online` refreshes automatic results. Authoritative file and folder manual overrides take precedence over automatic fields. Keep the cache outside both libraries. Run only one identification process at a time per public IP, and account for other clients sharing it.

Service policy references: [AcoustID lookup and limits](https://acoustid.org/webservice), [MusicBrainz rate limiting](https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting), [MusicBrainz browse/paging API](https://musicbrainz.org/doc/MusicBrainz_API).

## Reports and verification

`identification.csv` is generated by `identify`, analysis, and planning. It contains original and resolved fields (including disc/year), metadata source, AcoustID score/ID, MusicBrainz recording/release IDs, status, review reason, and evidence. Other inventory reports use effective metadata. Identification review statuses are advisory and remain visible in a plan; metadata read failures still block apply. Inspect the reports before applying a plan with fallback/unresolved results.

Build and run the dependency-free unit/integration/mock HTTP tests:

```powershell
.\build-offline.ps1
dotnet .\artifacts\Mp3Organizer.Tests.dll "C:\Music-test-scratch"
```

Tests use only synthetic audio and mocked HTTP. The real source library has not been accessed, and no live AcoustID/MusicBrainz identification requests have been made. `fpcalc` was not found on PATH in the development environment, so its real decoder/output compatibility remains a local integration check after installation. The executable adapter, caching, and resolution flow compile and are covered by isolated service tests; service HTTP behavior is covered with mocked responses.
