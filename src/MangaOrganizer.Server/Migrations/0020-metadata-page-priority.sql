ALTER TABLE metadata_suggestion ADD COLUMN priority INTEGER NOT NULL DEFAULT 0 CHECK (priority IN (0, 1));
DROP INDEX metadata_suggestion_pending;
CREATE INDEX metadata_suggestion_pending
    ON metadata_suggestion (priority DESC, requested_at, series_key) WHERE queued = 1;
