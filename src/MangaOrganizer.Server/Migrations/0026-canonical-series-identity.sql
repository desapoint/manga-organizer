CREATE TABLE series_identity_alias (
    previous_id TEXT NOT NULL PRIMARY KEY,
    series_id TEXT NOT NULL REFERENCES series_metadata(id) ON DELETE CASCADE
) WITHOUT ROWID;

-- Retain the same preferred series key the grouped Library used before the upgrade.
INSERT INTO series_identity_alias(previous_id, series_id)
SELECT id, canonical_id FROM (
    SELECT id, FIRST_VALUE(id) OVER (PARTITION BY provider_id ORDER BY series_key, id) AS canonical_id
    FROM series_metadata WHERE provider_id IS NOT NULL
)
WHERE id <> canonical_id;

INSERT INTO series_metadata_binding(root_id, series_key, metadata_id)
SELECT f.root_id, f.series_key, MIN(alias.series_id)
FROM library_file f
JOIN content_metadata link ON link.content_id = f.content_id
JOIN series_metadata old ON old.id = link.metadata_id AND old.series_key = f.series_key
JOIN series_identity_alias alias ON alias.previous_id = old.id
GROUP BY f.root_id, f.series_key
HAVING COUNT(DISTINCT alias.series_id) = 1
ON CONFLICT(root_id, series_key) DO NOTHING;

UPDATE series_metadata_binding AS binding SET metadata_id = alias.series_id
FROM series_identity_alias alias WHERE binding.metadata_id = alias.previous_id;

UPDATE content_metadata AS link SET metadata_id = alias.series_id
FROM series_identity_alias alias WHERE link.metadata_id = alias.previous_id;

DELETE FROM series_metadata WHERE id IN (SELECT previous_id FROM series_identity_alias);

CREATE UNIQUE INDEX series_metadata_anilist_id ON series_metadata(provider_id) WHERE provider_id IS NOT NULL;
CREATE INDEX series_identity_alias_series ON series_identity_alias(series_id);
CREATE INDEX content_metadata_series ON content_metadata(metadata_id);
