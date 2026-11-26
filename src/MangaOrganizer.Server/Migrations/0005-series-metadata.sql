ALTER TABLE library_file ADD COLUMN volume_hint TEXT;
ALTER TABLE library_file ADD COLUMN chapter_hint TEXT;

CREATE TABLE series_metadata (
    id TEXT PRIMARY KEY,
    series_key TEXT NOT NULL,
    title TEXT NOT NULL,
    alternate_title TEXT,
    kind TEXT NOT NULL CHECK (kind IN ('manga', 'doujin')),
    summary TEXT,
    status TEXT,
    total_chapters INTEGER CHECK (total_chapters > 0),
    total_volumes INTEGER CHECK (total_volumes > 0),
    provider_id INTEGER CHECK (provider_id > 0),
    updated_at TEXT NOT NULL
);

CREATE TABLE series_metadata_binding (
    root_id TEXT NOT NULL REFERENCES library_root(id),
    series_key TEXT NOT NULL,
    metadata_id TEXT NOT NULL REFERENCES series_metadata(id),
    PRIMARY KEY (root_id, series_key)
);

CREATE TABLE content_metadata (
    content_id TEXT NOT NULL PRIMARY KEY REFERENCES library_content(id),
    metadata_id TEXT NOT NULL REFERENCES series_metadata(id)
);

CREATE TABLE content_chapter (
    content_id TEXT NOT NULL REFERENCES library_content(id),
    volume TEXT NOT NULL,
    chapter TEXT NOT NULL,
    PRIMARY KEY (content_id, volume, chapter)
);
