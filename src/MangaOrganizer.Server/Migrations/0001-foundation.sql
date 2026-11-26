CREATE TABLE IF NOT EXISTS server_identity (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    instance_id TEXT NOT NULL,
    owner_id TEXT NOT NULL,
    password_hash TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS server_setup (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    library_path TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS library_root (
    id TEXT PRIMARY KEY,
    path_key TEXT NOT NULL UNIQUE,
    state TEXT NOT NULL DEFAULT 'idle',
    file_count INTEGER NOT NULL DEFAULT 0,
    last_attempt_at TEXT,
    last_scanned_at TEXT,
    skipped_links INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS library_file (
    id TEXT PRIMARY KEY DEFAULT (lower(hex(randomblob(16)))),
    root_id TEXT NOT NULL REFERENCES library_root(id),
    relative_path TEXT NOT NULL,
    extension TEXT NOT NULL,
    size INTEGER NOT NULL,
    modified_at TEXT NOT NULL,
    UNIQUE(root_id, relative_path)
);
