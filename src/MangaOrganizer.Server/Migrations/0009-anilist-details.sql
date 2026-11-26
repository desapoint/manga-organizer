ALTER TABLE series_metadata ADD COLUMN provider_details TEXT CHECK (provider_details IS NULL OR json_valid(provider_details));
