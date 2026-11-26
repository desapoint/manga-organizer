CREATE TABLE IF NOT EXISTS library_folder (
    root_id TEXT NOT NULL PRIMARY KEY REFERENCES library_root(id),
    path TEXT NOT NULL,
    name TEXT,
    kind TEXT NOT NULL CHECK (kind IN ('library', 'import', 'both'))
);
CREATE TABLE IF NOT EXISTS library_settings (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1)
);
