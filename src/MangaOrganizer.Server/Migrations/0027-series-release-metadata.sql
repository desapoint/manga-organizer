CREATE TABLE series_release_metadata (
    series_id TEXT NOT NULL REFERENCES series_metadata(id) ON DELETE CASCADE,
    volume TEXT NOT NULL,
    chapter TEXT NOT NULL,
    title TEXT,
    summary TEXT,
    PRIMARY KEY(series_id, volume, chapter),
    CHECK (volume <> '' OR chapter <> ''),
    CHECK (title IS NOT NULL OR summary IS NOT NULL)
) WITHOUT ROWID;
