ALTER TABLE content_metadata ADD COLUMN reviewed_locations_only INTEGER NOT NULL DEFAULT 0;

CREATE TABLE file_association_review (
    file_id TEXT NOT NULL,
    content_id TEXT NOT NULL REFERENCES library_content(id),
    PRIMARY KEY (file_id, content_id)
) WITHOUT ROWID;
