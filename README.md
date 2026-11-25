# Manga Organizer

A documentation-first project for a unified, self-hosted manga library, acquisition manager and reader on Windows and Unraid/Docker.

**Status** — the owner has approved the initial implementation baseline, and the audit remains open.

## Start here

- [Reference repositories (all seven)](REFERENCES.md)
- [Per-project documentation audit and comparison](docs/UPSTREAM-AUDIT.md)
- [Pinned source-code audit and observations](docs/source-audit/README.md)
- [Granular candidate features](docs/source-audit/ATOMIC-FEATURES.csv)
- [Feature selection matrix](docs/FEATURE-MATRIX.md)
- [Editable parent selection CSV](docs/FEATURE-SELECTION.csv)
- [Mihon/Suwayomi extension integration](docs/EXTENSIONS.md)
- [File organization and duplicate management (required)](docs/FILE-ORGANIZATION.md)
- [Remote reading and device sync](docs/REMOTE-READING.md)
- [Proposed architecture and deployment](docs/ARCHITECTURE.md)
- [Approved implementation baseline](docs/IMPLEMENTATION-DECISIONS.md)
- [Roadmap and acceptance gates](docs/ROADMAP.md)
- [Contributing and audit protocol](CONTRIBUTING.md)

The project targets a main library, one queue and a shared WebUI. One Windows or Linux server hosts the main library and validates reading progress for Android, Windows and Linux clients. The project also requires Mihon-compatible extension downloads, physical file organization and duplicate handling. Clients may optionally upload local files or subscribe to additional server libraries. [Audit completion ledger](docs/source-audit/COMPLETION-LEDGER.md)
