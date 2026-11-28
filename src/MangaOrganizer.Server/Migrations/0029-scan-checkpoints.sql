CREATE TABLE library_scan_checkpoint (
    root_id TEXT PRIMARY KEY REFERENCES library_root(id),
    snapshot TEXT NOT NULL,
    skipped_links INTEGER,
    file_plan TEXT,
    digests TEXT NOT NULL DEFAULT '[]'
) WITHOUT ROWID;

CREATE TABLE library_scan_metadata (
    root_id TEXT NOT NULL REFERENCES library_root(id),
    series_key TEXT NOT NULL,
    PRIMARY KEY (root_id, series_key)
) WITHOUT ROWID;
