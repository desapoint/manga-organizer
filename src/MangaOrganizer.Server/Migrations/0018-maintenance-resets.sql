CREATE TABLE maintenance_reset (
    id TEXT PRIMARY KEY,
    owner_id TEXT NOT NULL,
    level TEXT NOT NULL CHECK (level IN ('catalog', 'progress', 'configuration')),
    affected TEXT NOT NULL CHECK (json_valid(affected)),
    backup_directory TEXT NOT NULL,
    completed_at TEXT NOT NULL
);
