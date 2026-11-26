CREATE TABLE chapter_completion (
    owner_id TEXT NOT NULL,
    work_key TEXT NOT NULL,
    volume TEXT NOT NULL,
    chapter TEXT NOT NULL,
    completed INTEGER NOT NULL CHECK (completed IN (0, 1)),
    PRIMARY KEY (owner_id, work_key, volume, chapter)
) WITHOUT ROWID;

CREATE TABLE edition_volume_manifest (
    id TEXT PRIMARY KEY NOT NULL,
    work_key TEXT NOT NULL,
    edition TEXT NOT NULL,
    volume TEXT NOT NULL,
    chapters TEXT NOT NULL CHECK (json_valid(chapters)),
    UNIQUE (work_key, edition, volume)
);

CREATE TABLE edition_volume_release (
    manifest_id TEXT NOT NULL REFERENCES edition_volume_manifest(id) ON DELETE CASCADE,
    content_id TEXT NOT NULL REFERENCES library_content(id),
    PRIMARY KEY (manifest_id, content_id)
) WITHOUT ROWID;
