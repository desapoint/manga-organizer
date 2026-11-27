CREATE TABLE import_file_split (
    file_id TEXT PRIMARY KEY NOT NULL REFERENCES library_file(id) ON DELETE CASCADE,
    content_id TEXT NOT NULL REFERENCES library_content(id) ON DELETE CASCADE
);
