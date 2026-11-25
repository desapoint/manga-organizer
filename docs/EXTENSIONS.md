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

## Pass 4 compatibility evidence (2026-11-25)

See [pass 4](source-audit/PASS4.md) and [release comparison](source-audit/RELEASE-COMPARISON.csv). SW v2.4.2373 release commit and pinned branch diverge (146 ahead, 7 behind); both inspected ExtensionType files expose modern package fields. Inspected release mutations carry RequireAuth while branch adds explicit permissions and user-context changes; this does not establish the full release authorization model. Negotiate capability by authenticated schema/version, test extension store, install/update/uninstall/external upload, typed preferences, source search, queue enqueue/dequeue vs file removal and backup. Use a restricted integration account where supported. Older releases without verified granular permissions need separate isolation and explicit admin control, not silent fallback. MH v0.21.3 loader and audited pinned loader list library 1.4/1.6; release notes warn legacy support will be dropped later. Contract fixtures must distinguish actual versions and backup format; do not infer universal compatibility. Tag-source checks are not runtime tests.

## Pass 7 runtime distribution

Pinned SW source declares JVM target 21 while bundler uses Zulu JRE 25.0.5; do not equate compiler target with bundled runtime or assume stable parity. Its release dispatches a separate suwayomi-server-docker workflow: independently pin and audit that image repository and exact image digest. External separately installed companion preferred pending redistribution review; extension package and content rights separate. See [pass 7](source-audit/PASS7.md).

## Pass 20 verified Linux contract

Published Suwayomi `v2.4.2373` used the same server JAR in the tested Linux bundle and amd64 Docker image. The synthetic JAR installed, searched and downloaded a fractional chapter. The start mutation returned a GraphQL timeout on an empty queue in both artifacts, and on a populated queue in the standalone case even though the expected file was written. The adapter contract should evaluate GraphQL errors alongside queue and content state before recording success or retrying. APK/source-store flows, Mihon cross-version backup compatibility, and Windows and actual Unraid qualification are open. [Runtime evidence and fixture](source-audit/PASS20.md)

## Pass 22 companion and backup checks

The same Suwayomi release installed an external synthetic APK and downloaded its chapter on Docker. Hosted Windows passed the upstream MSI install and uninstall fixture. Published and pinned Mihon codecs decoded the generated backup in JVM tests. Store installation, additional package versions, Android device restore and actual Unraid qualification retain their own acceptance checks. [Pass 22 results](source-audit/PASS22.md)
