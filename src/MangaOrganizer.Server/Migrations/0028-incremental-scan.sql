ALTER TABLE library_file ADD COLUMN scan_stamp TEXT;
ALTER TABLE library_file ADD COLUMN scan_chapters TEXT;
ALTER TABLE library_file ADD COLUMN scan_chapter_source TEXT;
ALTER TABLE library_root ADD COLUMN last_scan TEXT;

CREATE TABLE library_sidecar_stamp (
    root_id TEXT NOT NULL REFERENCES library_root(id),
    relative_path TEXT NOT NULL,
    stamp TEXT NOT NULL,
    PRIMARY KEY (root_id, relative_path)
) WITHOUT ROWID;
