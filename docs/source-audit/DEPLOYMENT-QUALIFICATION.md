# Windows and Unraid deployment qualification (proposed)

Required outcomes from owner: standalone Windows app and Unraid Docker WebUI; remote Windows client optional mode in existing requirements; no stack selection yet.

| Reference | Windows | Docker |
|---|---|---|
| Mangarr | Python source, no verified native installer | Python 3.12 / Node 22 build; 6996; /config and /media |
| Maki | .NET 10 win-x64 tray/API, portable and Inno/Velopack, unsigned | .NET 10 / Node 24 build; 8990; /config; UID/GID mapping |
| Uchiyomi | Electron x64 NSIS, same app, unsigned; keep appdata | Node 24, embedded/external PostgreSQL, 8080:3000 default, owned volumes |
| Manga-arr | No verified native installer | Python 3.14; /config and /data; 8000; nonroot; amd64/arm64 CI |
| Kavita | .NET server reference, installer not verified here | Ubuntu noble architecture tar; 5000; active PUID/PGID logic absent |
| Suwayomi | Bundled MSI/JRE/launcher; JVM target 21, bundle JRE 25.0.5 | Separate image repository workflow dispatch; audit separate source/digest |
| Mihon | Android only in inspected packaging; interoperability | No native server image established |

## Proposed target contract

- Windows x64 standalone: same backend/API and web frontend as container; per-user installation, portable option only if selected, appdata outside binaries, loopback bind by default, remote mode authenticated TLS, child process graceful stop, no Docker required. Select tray/browser/WebView packaging only after architecture approval. Do not bypass SmartScreen; sign installer, updater and manifests, verify signatures and artifact hashes. Preserve data on uninstall unless explicit confirmation.
- Unraid amd64 first; optional arm64 only after native tests. Proposed port 8787 (not approved), /config local pool/appdata, /data common media/download mount if user wants hardlinks, optional separate read-only roots and quarantine/staging. Example UID/GID 99:100 only when host ownership matches. Prefer explicit numeric user with writable appdata preflight, no privileged mode, no Docker socket, no blanket chown of existing library. Never default disable security mitigations for memory.
- Use local appdata for database/operation journal; network media mount only after per-filesystem qualification. Hardlink requires same underlying filesystem and permissions, not just same container prefix. Mount disappearance must suspend mutation and reconciliation; restore only after validated identity. Data snapshot/backup consistent and verified.
- Suwayomi companion: external server or managed distinct process/container with pinned version, image digest and verified runtime, scoped credentials and isolated storage; never mount entire host or expose engine publicly by default. Runtime schemas and extension compatibility test; companion health does not equal organizer readiness. Separate permissions, extension repositories opt-in, no untrusted packages bundled.
- Release: locked dependencies (no fallback to unlocked install), digest-pinned bases, reproducible version/commit, per-architecture SBOM + licenses + vulnerability scan, signed artifacts/checksums, staged update with backup and rollback or forward recovery. No auto-update during active file mutation. Use authenticated API readiness and non-sensitive liveness; logs redact secrets.

## Target qualification matrix — all target-host runs pending

| ID | Target | Test | Pass condition |
|---|---|---|---|
| W01 | Windows 10/11 x64 candidate | Fresh per-user install without Docker; launch and stop | Same backend/WebUI, no admin by default |
| W02 | Windows | Upgrade/uninstall/reinstall, portable if selected | State preserved, verified backup, signed package |
| W03 | Windows NTFS/UNC | Case-only rename, reserved names, long paths, locked files, cross-volume move | No overwrite or loss; correct recovery |
| W04 | Windows | Remote connection, offline engine startup and shutdown | No unintended local database or competing queue |
| D01 | Actual Unraid amd64 | Install template, local pool appdata, numeric owner | Nonroot, no media chown, stable paths |
| D02 | Unraid /mnt/user and direct pool/disk paths | Hardlink identity, same/different fs, missing mount and remount | Verified actual behavior, no false reclamation |
| D03 | Unraid + SMB/NFS | Root unavailable, permissions, disconnect and crash during mutation | Fence, rollback/recover, no destructive reconciliation |
| D04 | Docker | Fresh install, stop during stage/publish, restart, upgrade, restore | Consistent journal and DB, no duplicate jobs |
| D05 | Docker | Image health, auth, secrets, rootless and resource limits | No privileged mode; no sensitive probe/log |
| S01 | Windows and Unraid | Pinned Suwayomi startup, auth, schema, extension fixtures | Compatible authorized download and recovery |
| S02 | Windows and Unraid | Engine restart, stale jobs, version change, disable/uninstall | Queue authority and retained user data |
| R01 | Release CI | Locked dependencies, SBOM, notices, hashes, signing, scanner | Artifact-specific evidence and approved exceptions |
| R02 | Native arch | Actual amd64 and optional arm64 runner | Test results bound to commit/image digest |

Pass 20 records Linux and rootless Docker test results for selected upstream behavior. Pass 21 adds the portable Suwayomi ZIP and basic NTFS checks on hosted Windows, and explicit numeric ownership and separate volumes in Docker. The full matrix requires the target operating systems, storage configurations and deployable Manga Organizer artifacts for sign-off. [Pass 20 results](PASS20.md) · [Pass 21 qualification](PASS21.md)
