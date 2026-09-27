# LLM diagnostics and counters

Use `artifacts-playlist-import/Mp3Organizer.dll`. Command syntax and identification decisions are unchanged.

Incremental `analyze --limit N` now prints:

```text
LLM: not required (no API request)
LLM: requesting decision... (OpenAI API HTTP attempt 1; model=...)
LLM: resolved candidate candidate-1
LLM: unable to decide -> NeedsReview (reason)
LLM: unavailable - Quota: ...
LLM: disabled for remainder of run - deterministic fallback enabled
```

The request message is emitted immediately before the HTTP send, after checking configuration. It means an outbound attempt, not proof that the server received or billed the request. Every retry emits another message with its attempt number. Missing credentials/model, deterministic results and run-wide fallback do not emit request messages. Keys and raw request/response bodies are not logged.

The existing user choice to continue without Codex is unchanged. Its message says **LLM disabled, deterministic fallback enabled**; saying “fallback disabled” would describe the opposite behavior.

Each incremental analysis batch prints these statistics, including a clean quota/configuration stop:

```text
Batch LLM activity:
LLM requests:        0
LLM resolved:        0
LLM needs review:    0
LLM unavailable:     0
Deterministic only:  10
```

* **LLM requests:** HTTP attempts, including retries and unsuccessful network attempts. Does not count merely entering the resolver.
* **LLM resolved:** file-resolution attempts whose model decision passes existing hard validation and yields complete metadata. Later tag/copy failures do not erase the recognition event.
* **LLM needs review:** model responses that do not yield an accepted complete resolution, including responses rejected by hard validation.
* **LLM unavailable:** file-resolution attempts that terminate with a classified LLM failure, after any retries. Includes missing configuration (which can have zero HTTP requests). The same interrupted file retried in another run can contribute another event.
* **Deterministic only:** resolutions completed without consulting the LLM, including sufficient deterministic/manual/cached metadata, explicitly disabled/offline mode and subsequent run-wide fallback. A file can still need human review in this category. The file that triggered an unavailable failure counts under unavailable, not again as deterministic-only.

These are activity counts, not mutually interchangeable totals: requests count HTTP attempts; the remaining counters count resolution outcomes. Unavailable files are distinguished from files the model actually examined and could not decide. Skipped/completed database rows not visited by the batch contribute nothing.

`status --workspace <path>` prints cumulative counters from the new additive `llm_events` table in `music-organizer.db`. Each event stores run ID, UTC time, source path, event kind and minimal diagnostic detail (model/attempt, candidate ID, validation reason or failure category). Events are committed immediately, including before a quota-stop choice, and survive application restarts, manual resolution and progress resets. Existing databases start with zero historical activity counters; old request counts cannot be reconstructed reliably. Existing per-file Codex provenance and current-state counts remain available separately.

Even `reset-all` clears processing state while retaining this diagnostic activity history; its database backup includes both. Deleting/replacing the workspace database loses the history. A forced termination between a recorded dispatch and its network send may leave an attempted-request event without a corresponding server request; no local telemetry can guarantee remote delivery.

No review-state gates, confidence thresholds, lookup ordering, caching behavior, tag writes, plan/apply behavior or retry policy were changed by this update. Full-source modes remain deterministic; the new batch counters apply to incremental `analyze --limit`.


