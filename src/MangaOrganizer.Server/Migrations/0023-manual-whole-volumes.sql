ALTER TABLE library_content ADD COLUMN manual_volume TEXT CHECK (manual_volume IS NULL OR length(manual_volume) > 0);

CREATE VIEW library_file_numbered AS
    SELECT f.id, f.root_id, f.relative_path, f.extension, f.size, f.modified_at, f.content_id, f.series_key, f.series_title,
        COALESCE(c.manual_volume, f.volume_hint) AS volume_hint,
        CASE WHEN c.manual_volume IS NULL THEN f.chapter_hint END AS chapter_hint
    FROM library_file f LEFT JOIN library_content c ON c.id = f.content_id;

CREATE VIEW active_inferred_volume_chapters AS
    SELECT v.content_id, v.volume, v.chapters, v.source
    FROM inferred_volume_chapters v JOIN library_content c ON c.id = v.content_id
    WHERE c.manual_volume IS NULL;
