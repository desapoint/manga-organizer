CREATE TABLE IF NOT EXISTS library_series_cover (
    root_id TEXT NOT NULL REFERENCES library_root(id),
    series_key TEXT NOT NULL,
    relative_path TEXT NOT NULL,
    size INTEGER NOT NULL,
    modified_at TEXT NOT NULL,
    archive_entry TEXT,
    sha256 BLOB NOT NULL CHECK (length(sha256) = 32),
    PRIMARY KEY(root_id, series_key)
);
