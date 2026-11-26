CREATE TABLE metadata_suggestion (
    series_key TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    revision TEXT NOT NULL,
    queued INTEGER NOT NULL CHECK (queued IN (0, 1)),
    requested_at INTEGER NOT NULL,
    retry_at INTEGER,
    candidates TEXT,
    message TEXT
);

CREATE INDEX metadata_suggestion_pending ON metadata_suggestion (requested_at, series_key) WHERE queued = 1;
CREATE INDEX library_file_series_lookup ON library_file (series_key, root_id);
