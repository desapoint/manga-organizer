# Import-to-reading MVP

PRs #26–#30 landed logical chapter progress, reviewed batch organization, and duplicate review. The series page uses the existing mockup's cover, progress and volume-coverage layout. Reading keeps a separate saved physical page per content identity. [Series design reference](mockups/manga-series-v3.html) · [Current baseline and expansion](EXPANDED-BACKLOG.md#baseline-to-reuse)

1. Keep chapter completion separate from file resume. Identify logical chapters within an explicitly matched catalog work or an individual library series. Compare known chapter coverage against owner-verified edition manifests. Keep unknown coverage distinct from missing chapters.
2. Preview batch destinations and collisions, apply only the selected plans, and record each result in the recoverable file operation journal.
3. Show exact copies, physical aliases, and overlapping chapter or volume releases with side-by-side evidence. Quarantine is a reviewed action for verified exact copies. A chapter can overlap a volume without replacing the rest of the volume. Recheck the retained copy when executing the plan.
4. Open editing forms in dialogs, preserve the underlying page and return focus to the action.

The release acceptance includes upgrading an existing library, checking reading handoff on real devices, and rehearsing recovery on Windows and an actual Unraid share. Keep unattended organization behind storage qualification.

The owner-approved follow-up starts with verified pre-upgrade backups, then introduces canonical IDs and release variants, persistent duplicate decisions, managed trash, verified page ranges and cross-release reading. [Expanded backlog](EXPANDED-BACKLOG.md) · [Acceptance matrix](EXPANDED-ACCEPTANCE.md)

The Duplicates tab shows a series chapter map with indexed and manifest chapters, matching byte groups, and all local files. Compare this file opens the starting file and its candidates. Equal chapter spans and same-number volumes with unresolved chapters use a compact archive comparison. Review a chapter to mark exact standalone copies, or review a volume to see its entire coverage and same-number variants. Quarantine requires a separate, hash-identical file in the same scanned, managed series group with equal full chapter and volume assignments for every marked copy. Hardlink aliases, uncertain identity, and different releases remain protected. Quarantine keeps a verified recovery copy and a journal entry. Restore through File operation history.
