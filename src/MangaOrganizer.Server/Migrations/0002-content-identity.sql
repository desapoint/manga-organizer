CREATE TABLE IF NOT EXISTS library_content (
    id TEXT PRIMARY KEY NOT NULL DEFAULT (lower(hex(randomblob(16)))),
    sha256 BLOB NOT NULL UNIQUE CHECK (length(sha256) = 32)
);
