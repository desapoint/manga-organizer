CREATE TABLE release_tag (
    content_id TEXT PRIMARY KEY REFERENCES library_content(id) ON DELETE CASCADE,
    language TEXT,
    color TEXT NOT NULL CHECK (color IN ('unknown', 'black-and-white', 'full-color', 'partial')),
    censorship TEXT NOT NULL CHECK (censorship IN ('unknown', 'uncensored', 'censored', 'mixed')),
    edition_name TEXT,
    group_name TEXT,
    revision INTEGER NOT NULL CHECK (revision > 0),
    updated_at TEXT NOT NULL
);
