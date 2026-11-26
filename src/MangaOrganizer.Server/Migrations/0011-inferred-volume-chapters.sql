CREATE TABLE inferred_volume_chapters (
    content_id TEXT PRIMARY KEY NOT NULL REFERENCES library_content(id),
    volume TEXT NOT NULL,
    chapters TEXT NOT NULL CHECK (json_valid(chapters)),
    source TEXT NOT NULL CHECK (source IN ('archive', 'filename'))
) WITHOUT ROWID;
