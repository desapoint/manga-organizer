CREATE TABLE series_cover_preference (
    series_id TEXT NOT NULL PRIMARY KEY REFERENCES series_metadata(id) ON DELETE CASCADE,
    label TEXT NOT NULL,
    data BLOB,
    provider_url TEXT,
    CHECK ((data IS NULL) <> (provider_url IS NULL))
) WITHOUT ROWID;
