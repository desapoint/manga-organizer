CREATE TABLE library_intake_target (
    source_root TEXT PRIMARY KEY REFERENCES library_folder(root_id) ON UPDATE CASCADE ON DELETE CASCADE,
    target_root TEXT NOT NULL REFERENCES library_folder(root_id) ON UPDATE CASCADE ON DELETE CASCADE
);

ALTER TABLE file_operation ADD COLUMN intake_transfer INTEGER NOT NULL DEFAULT 0 CHECK (intake_transfer IN (0, 1));
