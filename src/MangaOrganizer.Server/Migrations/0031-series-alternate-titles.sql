ALTER TABLE series_metadata ADD COLUMN alternate_titles TEXT NOT NULL DEFAULT '[]'
    CHECK (json_valid(alternate_titles) AND json_type(alternate_titles) = 'array');

UPDATE series_metadata SET alternate_titles = json_array(alternate_title)
WHERE alternate_title IS NOT NULL AND trim(alternate_title) <> '';
