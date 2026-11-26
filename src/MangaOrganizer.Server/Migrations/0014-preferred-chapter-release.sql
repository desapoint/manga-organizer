CREATE TABLE preferred_chapter_release (
    owner_id TEXT NOT NULL,
    work_key TEXT NOT NULL,
    volume TEXT NOT NULL,
    chapter TEXT NOT NULL,
    content_id TEXT NOT NULL REFERENCES library_content(id),
    PRIMARY KEY (owner_id, work_key, volume, chapter)
) WITHOUT ROWID;
