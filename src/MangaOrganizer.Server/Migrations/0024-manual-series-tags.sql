ALTER TABLE series_metadata ADD COLUMN manual_tags TEXT CHECK (manual_tags IS NULL OR json_type(manual_tags) = 'array');
