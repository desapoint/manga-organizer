ALTER TABLE library_folder ADD COLUMN managed INTEGER NOT NULL DEFAULT 0 CHECK (managed IN (0, 1));

CREATE TABLE file_operation (
    id TEXT PRIMARY KEY NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('planned', 'applying', 'completed', 'restoring', 'restored', 'review')),
    source_root TEXT NOT NULL REFERENCES library_root(id),
    target_root TEXT NOT NULL REFERENCES library_root(id),
    source_base TEXT NOT NULL,
    target_base TEXT NOT NULL,
    source_path TEXT NOT NULL,
    target_path TEXT NOT NULL,
    file_id TEXT NOT NULL,
    content_id TEXT NOT NULL REFERENCES library_content(id),
    series_key TEXT NOT NULL,
    series_title TEXT,
    volume_hint TEXT,
    chapter_hint TEXT,
    size INTEGER NOT NULL,
    sha256 BLOB NOT NULL CHECK (length(sha256) = 32),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    message TEXT
);

CREATE INDEX file_operation_state_source ON file_operation(state, source_root);
CREATE INDEX file_operation_state_target ON file_operation(state, target_root);
