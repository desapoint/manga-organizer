# Proposed extension acquisition contract (Pass 10)

Status: proposal, not implementation or approved technology choice. Required outcome F12 extension downloading and F69/F70 organization/duplicates; F68 unified queue is a proposed architecture capability, not newly approved. See [Pass 10](PASS10.md) and [Pass 9](PASS9.md).

## Source-declared API comparison

Mihon checked-in Suwayomi schema at f8e30a86086020aaee1e89a9d015373b1a4ff026 includes the selected fields below; server Kotlin declarations at 4081b05f02456ec099c85127dfa5253e968d48a7 corroborate names. This is not runtime introspection or full schema diff. Permission statements are selected server source annotations/checks, not tested.

| API surface | Selected source contract | Manga Organizer adapter behavior |
|---|---|---|
| addExtensionStore / removeExtensionStore | indexUrl; MANAGE_EXTENSION_STORES | Admin approval; URL validation; origin and signer change review |
| updateExtension / updateExtensions | package ID(s), patch install/update/uninstall | Explicit action and version; test update-only permission separately |
| installExternalExtension | uploaded file; INSTALL_EXTERNAL_EXTENSIONS | Disabled by default; verify artifact, isolated staging and admin consent |
| downloadStatus | authenticated snapshot | Reconcile at startup, reconnect and uncertain request outcome |
| downloadStatusChanged | input; update flow; maxUpdates obsolete | Subscribe only for acceleration; tolerate omissions and duplicates |
| downloadChanged | deprecated full status | Version-gated fallback only |
| enqueueChapterDownload(s) / dequeueChapterDownload(s) | ID(s), DOWNLOAD_CHAPTERS | Bind engine instance and requester; reconcile before retry |
| startDownloader / stopDownloader / clearDownloader | DOWNLOAD_CHAPTERS, engine-wide control | Separate privileged action from job-local pause/cancel |
| reorderChapterDownload(s) | source declarations, single reorder in client snapshot | Feature-probe bulk reorder, keep central order independent |
| deleteDownloadedChapter(s) | auth, delegates user ID | No automatic deletion; verify permission and retained references |
| updateChapters | Mihon marks read in combined request with delete | Split progress from storage actions; no delete-on-read by default |

## Required data and state boundaries

Persist job ID, engine instance/version, source ID and source chapter identity, canonical work/edition/language/chapter coverage (unknown allowed), requesters, desired state, attempt counter, queue ordering, timestamps, correlation and receipt, artifact path/size/strong digest, import journal ID and failure diagnostics. Keep secrets in scoped credential storage; redact logs. Engine IDs are local to instance and may change on migration. Correlation ID alone does not enforce exactly-once.

| Central state | Transition requirement |
|---|---|
| requested | Durable record and requester link before remote side effect |
| submitted / queued | Engine acknowledgement or snapshot reconciliation |
| downloading | Progress events advisory, timestamped |
| paused / blocked / unknown | Distinguish local intent, permission denial, offline and unresolved remote result |
| engine_finished | Snapshot or terminal event; not library success |
| verifying | Stable readable artifact, safe archive entries, expected coverage, ordered page/hash evidence |
| duplicate_review | Cross-adapter physical/content/edition comparison before publication |
| importing | Pass 9 journal, exclusive staging and no-clobber |
| imported | Verified durable receipt and DB references |
| failed / cancelled | Persist cause and requester intent; no implicit purge |

Retry transient errors using bounded policy, preserve diagnostics, do not retry authorization/unsupported format blindly. Reconcile remote state before resubmitting; when timeout leaves ambiguity block or query rather than create duplicate. Avoid direct writes to engine DB and do not rename files behind it. Support independent engine and central backups; do not restore unreviewed signer keys or secrets. Only authorized content sources and user-authorized downloads.

## Qualification fixtures (all NOT RUN)

| ID | Fixture | Acceptance |
|---|---|---|
| EXT-01 | Legacy APK and native JAR on pinned runtime | Qualified compatibility range and source list; unsupported rejected |
| EXT-02 | Same package higher version from different store/signer | No silent trust transfer |
| EXT-03 | Invalid package, library version, malformed manifest, tampered artifact | Reject prior to class load |
| EXT-04 | Store outage / redirect / changed signing key | Preserve trusted inventory; block unexpected origin |
| EXT-05 | Role matrix incl. update-only, external install, global queue, delete | Denied operations have no side effects |
| EXT-06 | HTTP/WS auth expiry and reconnect | No unauthorized data; snapshot resync |
| ACQ-01 | Two users request same source chapter; one cancels | One shared acquisition remains for other user |
| ACQ-02 | Remote enqueue accepted but response lost; restart | Reconcile without duplicate jobs |
| ACQ-03 | Queue order, retry state, worker stops, engine restarts | Durable central intent and bounded recovery |
| ACQ-04 | Engine finishes with partial/malformed archive or wrong pages | No imported state, original retained |
| ACQ-05 | Same chapter from extension, torrent and direct adapter | Exact/repack/alternate classification with no lost unique coverage |
| ACQ-06 | Progress sync including Mihon mark-read-and-delete | Progress does not automatically remove centralized file |
| ACQ-07 | Crash after publication before import acknowledgement | Receipt verified; no duplicate publication |
| MIG-01 | Current/legacy backup with missing source/store and changed signer | Preview unresolved data; no automatic code execution |
| DEP-01 | Windows companion and actual Unraid runtime restart/upgrade | Preserve state, scoped mount and credentials; version mismatch reported |

Live schema, exact engine artifacts, platform test results and authorized extension fixtures remain blockers. No optional feature or license choice made.
