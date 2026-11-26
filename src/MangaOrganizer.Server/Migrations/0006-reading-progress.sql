CREATE TABLE reading_progress (
    owner_id TEXT NOT NULL,
    content_id TEXT NOT NULL REFERENCES library_content(id),
    page_index INTEGER NOT NULL CHECK (page_index >= 0),
    page_count INTEGER NOT NULL CHECK (page_count > 0),
    revision INTEGER NOT NULL CHECK (revision > 0),
    completed INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0, 1)),
    updated_at TEXT NOT NULL,
    PRIMARY KEY (owner_id, content_id),
    CHECK (page_index < page_count)
);
CREATE INDEX reading_progress_latest ON reading_progress(owner_id, completed, updated_at DESC);

CREATE TABLE reading_progress_event (
    owner_id TEXT NOT NULL,
    content_id TEXT NOT NULL REFERENCES library_content(id),
    update_id TEXT NOT NULL,
    revision INTEGER NOT NULL,
    PRIMARY KEY (owner_id, content_id, update_id)
);
