# Mihon-compatible extension integration (required)

User requirement: download through extensions made for Mihon/Tachiyomi and supported by Suwayomi. Reference [SW and MH](../REFERENCES.md); compare UC extension UI, MK optional Suwayomi and KK direct handoff. No specific third-party repository selected. Extension availability/compatibility varies; no promise that every APK works.

## Initial workflow

Use a separately installed Suwayomi-Server instance accessed by a narrow adapter. The operator reviews extension repository trust and manages extension installation, updates and source settings in Suwayomi. Manga Organizer keeps one authoritative download queue and import path. [Approved acquisition scope](IMPLEMENTATION-DECISIONS.md#owner-feature-selection)

1. Verify the companion version and capabilities. Search the operator's configured sources from Manga Organizer and retain source and chapter identity.
2. An owner selects a chapter and starts a manual download. Record it in the persistent queue and reconcile retries and unclear download acknowledgements against the companion and staging files.
3. Validate chapter coverage and files in staging. Present the verified import for review and preserve source and chapter provenance.

Organizer-managed companion packaging, extension lifecycle and source settings, scheduled acquisition, generating ComicInfo and rewriting downloaded covers, and Mihon backup import and progress interoperability are deferred. Catalog scans will offer read-only cover previews for association review. [Feature selection register](FEATURE-SELECTION.csv) · [Cover previews](CATALOG-ASSOCIATION.md#cover-previews-during-scans)

## Security and compatibility gates

Extensions are third-party executable code: opt-in only, isolate process/container, no Docker socket, no access to secrets or unrelated host files, restrict writable staging, validate network destinations, timeouts and resource limits, require explicit trust on signer/version changes, log provenance. Do not bypass access controls, paywalls or rate limits. Test with authorized or synthetic sources; no bundled extension list by default.

Verify the companion's actual API, authorization, extension trust and output locations for the selected version. The end-to-end fixture installs a synthetic trusted extension in Suwayomi, searches and downloads a fractional chapter through Manga Organizer, tests restart and retry, and verifies one reviewed import on Windows and Unraid. Include unsupported-version and credential-redaction checks. Apply the extension lifecycle, WebView, backup schema and distribution checks to the matching candidates when selected.

## Pass 4 compatibility evidence (2026-11-25)

See [pass 4](source-audit/PASS4.md) and [release comparison](source-audit/RELEASE-COMPARISON.csv). SW v2.4.2373 release commit and pinned branch diverge (146 ahead, 7 behind); both inspected ExtensionType files expose modern package fields. Inspected release mutations carry RequireAuth while branch adds explicit permissions and user-context changes; this does not establish the full release authorization model. Negotiate capability by authenticated schema/version, test extension store, install/update/uninstall/external upload, typed preferences, source search, queue enqueue/dequeue vs file removal and backup. Use a restricted integration account where supported. Older releases without verified granular permissions need separate isolation and explicit admin control, not silent fallback. MH v0.21.3 loader and audited pinned loader list library 1.4/1.6; release notes warn legacy support will be dropped later. Contract fixtures must distinguish actual versions and backup format; do not infer universal compatibility. Tag-source checks are not runtime tests.

## Pass 7 runtime distribution

Pinned SW source declares JVM target 21 while bundler uses Zulu JRE 25.0.5; do not equate compiler target with bundled runtime or assume stable parity. Its release dispatches a separate suwayomi-server-docker workflow: independently pin and audit that image repository and exact image digest. External separately installed companion preferred pending redistribution review; extension package and content rights separate. See [pass 7](source-audit/PASS7.md).

## Pass 20 verified Linux contract

Published Suwayomi `v2.4.2373` used the same server JAR in the tested Linux bundle and amd64 Docker image. The synthetic JAR installed, searched and downloaded a fractional chapter. The start mutation returned a GraphQL timeout on an empty queue in both artifacts, and on a populated queue in the standalone case even though the expected file was written. The adapter contract should evaluate GraphQL errors alongside queue and content state before recording success or retrying. APK/source-store flows, Mihon cross-version backup compatibility, and Windows and actual Unraid qualification are open. [Runtime evidence and fixture](source-audit/PASS20.md)

## Pass 22 companion and backup checks

The same Suwayomi release installed an external synthetic APK and downloaded its chapter on Docker. Hosted Windows passed the upstream MSI install and uninstall fixture. Published and pinned Mihon codecs decoded the generated backup in JVM tests. Store installation, additional package versions, Android device restore and actual Unraid qualification retain their own acceptance checks. [Pass 22 results](source-audit/PASS22.md)
