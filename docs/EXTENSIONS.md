# Mihon-compatible extension integration (required)

User requirement: download through extensions made for Mihon/Tachiyomi and supported by Suwayomi. Reference [SW and MH](../REFERENCES.md); compare UC extension UI, MK optional Suwayomi and KK direct handoff. No specific third-party repository selected. Extension availability/compatibility varies; no promise that every APK works.

## Recommended design

Use Suwayomi-Server as a separate, version-pinned engine, accessed by a narrow adapter. On Unraid support both an existing external instance and optional managed companion container. On standalone Windows evaluate a managed local Java process/bundle or existing remote engine; don't require Android emulation or Docker on Windows. Remote desktop talks to Manga Organizer server, never runs a duplicate scheduler. Keep extension UI in our WebUI. Suwayomi runs extensions; Manga Organizer owns monitoring policy, canonical identity, unified queue, import and progress. Disable redundant engine automation where possible; otherwise reconcile ownership explicitly.

## Candidate workflow

1. Admin adds a trusted extension repository URL, explicitly reviews trust, package identity, source languages, version and permissions.
2. Browse/install/update/remove approved extensions; enable individual sources. Expose extension settings and supported authentication without leaking secrets.
3. Search/browse sources; return per-source status and errors; map source-specific series/chapter IDs to canonical work/edition/chapter with confidence and manual review.
4. Select a chapter/version, queue authorized download, poll or subscribe to progress, cancel/retry with idempotency.
5. Validate pages and chapter coverage; stage import, preserve source/group/extension version and hashes; publish CBZ + ComicInfo atomically. Reconcile already-owned chapters and failures, clean only managed temporary artifacts.
6. Expose source health, package/API compatibility, version mismatch, storage and auth diagnostics. Import Mihon backups and map categories, source IDs and reading progress with reviewed matches; evaluate Mihon server connector and explicit progress synchronization.

## Security and compatibility gates

Extensions are third-party executable code: opt-in only, isolate process/container, no Docker socket, no access to secrets or unrelated host files, restrict writable staging, validate network destinations, timeouts and resource limits, require explicit trust on signer/version changes, log provenance. Do not bypass access controls, paywalls or rate limits. Test with authorized or synthetic sources; no bundled extension list by default.

Verify actual SW API/schema, auth, extension lifecycle, download output locations, supported versions, WebView dependencies and backup schema before coding. Build a contract test for each adapter. Required end-to-end tests: add trusted fixture repo, install extension, search, resolve fractional chapter, download, pause/restart/retry, import valid CBZ, verify no duplicate, uninstall, invalid package and unsupported-version failure, Windows local and Unraid remote execution, no credentials in logs. Exact engine bundling/licensing and distribution remain open.
