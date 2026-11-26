CREATE TABLE series_collection (
    id TEXT PRIMARY KEY NOT NULL,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    filters TEXT CHECK (filters IS NULL OR json_valid(filters))
);

CREATE TABLE series_collection_item (
    collection_id TEXT NOT NULL REFERENCES series_collection(id) ON DELETE CASCADE,
    root_id TEXT NOT NULL,
    series_key TEXT NOT NULL,
    PRIMARY KEY (collection_id, root_id, series_key)
) WITHOUT ROWID;
