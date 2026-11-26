CREATE TABLE duplicate_review_decision (
    id INTEGER PRIMARY KEY,
    root_id TEXT NOT NULL REFERENCES library_root(id),
    series_key TEXT NOT NULL,
    kind TEXT NOT NULL CHECK (kind IN ('bytes', 'chapter', 'volume')),
    fingerprint TEXT NOT NULL CHECK (length(fingerprint) = 64),
    decision TEXT NOT NULL CHECK (decision IN ('keep', 'defer', 'reopen')),
    note TEXT,
    owner_id TEXT NOT NULL,
    decided_at TEXT NOT NULL
);

CREATE INDEX duplicate_review_latest ON duplicate_review_decision(root_id, series_key, kind, id DESC);
