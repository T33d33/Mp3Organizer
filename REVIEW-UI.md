Current review interaction (supersedes previous guided/auto-finish instructions):

- Every file starts in selection mode. [1-N] chooses the displayed candidate. Enter chooses the highest-scoring candidate (a recommendation, not verification), or current metadata when there are no candidates.
- M alone opens manual editing. P plays, D leaves NeedsReview and continues, /q stops.
- Manual fields are labeled track/album metadata. Enter preserves, text replaces, /clear empties, /back revisits the previous field, /cancel returns without saving, /q stops. Unknown slash commands show help. Candidate numbers have no selection meaning inside explicitly entered manual editing.
- After candidate selection or manual editing, a full summary requires YES. No browsing/cancelled changes persist. Incomplete confirmed metadata stays NeedsReview.
- YES saves this track only. ALBUM explicitly also propagates the year to missing-year tracks matching the source folder, artist/album identity. Track-specific Artist/Title/Track never propagate. Candidate/manual album names and album artist edits remain per track. Source-tag writing, when enabled, is stated on the confirmation screen.
- Automatic per-folder entry into manual mode and F auto-finish are not used by the new review command; they conflicted with explicit candidate selection and confirmation.
- Explicit numeric clears are persisted as zero in manual JSON; older null values still mean no override.
- Console input/output uses UTF-8. Filenames already damaged on disk cannot be repaired by changing console encoding and are left unchanged.

Updated confirmation: Enter saves and continues (YES remains accepted). /cancel returns to selection with the session draft retained, and M resumes those values. Explicit --write-tags still requires YES before touching source tags. A confirmed empty album is persisted as a loose-track decision; a track number is optional in that case. Meaningful Artist and Title are still required. Cancelled drafts are not persisted to disk.

Single-action acceptance: Enter at the action menu saves current metadata (or the recommended candidate when candidates exist) and advances directly. A candidate number also saves directly. M enters labeled field editing, which ends with an Enter-to-save summary. Explicit source-tag writing retains separate authorization. The prompt explicitly says Action [Enter = save and next; M = edit fields].

Action menu always displays M (manual), P (play), D (defer), /back (previous file), /q (stop) alongside Enter (save and next). /back revisits the preceding file in the current session, preserving saved metadata and drafts; at the first file it reports that there is no predecessor. In manual mode /back still means previous field. Reviewing a file twice does not double-count it.

Album-first workflow (current review command):
1. ALBUM METADATA appears before the track loop if shared Album, Album Artist or Year is unreliable. Suggestions use unanimous reliable metadata; folder names never supply metadata. Enter keeps the value, a replacement edits it, S leaves unknown, /back revisits and /q stops. The summary states propagation scope and requires Enter/YES confirmation. Nothing is persisted before this.
2. The decision is stored in the existing manual-resolutions.json folder entry, with setup-confirmed and provisional-album-artist flags. It fills missing/unreliable shared fields for NeedsReview, Ready and Processed tracks in that exact source folder. Existing reliable conflicting values remain unchanged. No Title or Track Number is propagated. Track Artist remains unchanged.
3. TRACK candidate selection retains numbered choices and Enter save-and-next. M explicitly edits track Artist, Title and Track Number only. The established Album, Album Artist and Year remain displayed.
4. If a confirmed track Artist differs from an Album Artist derived from the common track Artist, review proposes Various Artists and requires YES. Album, Year and individual track Artists remain unchanged. Explicitly typed Album Artist decisions are preserved.
5. Later review invocations reuse the persisted folder setup, including decisions to leave fields unknown. New source files can inherit the explicit folder decision through normal manual resolution.
6. Normal run repairs mapped Processed targets when a confirmed missing Album/Album Artist/Year is filled, using the existing guarded migration journal and playlist regeneration. Existing nonempty album metadata is not broadly replaced. Source audio remains unchanged.

Assisted numbering: album-first review uses natural source path order. Existing nonzero resolved track numbers are preserved. For missing numbers, suggest the next unused positive number following the closest preceding confirmed album track, starting at 1. Confirmed Ready/Processed tracks and explicit manual number decisions reserve their numbers. Scope is source root + exact source folder + album identity/owner. Enter accepts the displayed fallback. Only saved decisions advance the sequence; cancelled/deferred prompts do not. Restarting reads persisted numbers. /back edits cause subsequent unsaved sequential suggestions to be recalculated; already saved or explicitly edited numbers are retained. Candidate-provided numbers still take priority. Automatic suggestions stop above 9999 rather than wrap or collide.

Explicit review scopes (current behavior):
- Before confirmation the UI states the scope root and exact currently indexed count, INCLUDING nested folders. Confirmation snapshots stable progress file IDs into manual-resolutions.json's folder decision (reviewFileIds).
- Only those IDs inherit that confirmed Album/Album Artist/Year context. Source path alone does not grant membership; new files need explicit confirmation. Unconfirmed folder names never become metadata.
- Entering nested folders or restarting review reuses membership. Track candidates cannot override the confirmed shared context. Artist/Title/Track remain independent. Number suggestions share the explicit scope across directories.
- Legacy album decisions without membership require one scope confirmation, displaying the actual current recursive count and existing values. Historical counts cannot be reconstructed safely; no silent recursive migration occurs.
- Album context is resolved by progress ID rather than SHA, preventing identical content outside a scope from inheriting its album decision. Track decisions continue using the existing manual override mechanism.
- Normal run reconciles mapped target metadata/paths against the confirmed scope; source audio remains immutable. Conflicts stop instead of overwriting.
