-- =========================================================================
-- MIGRATION: Transition DocumentId to globally unique BatchDetail.ID
-- =========================================================================

DO $$
DECLARE
    r RECORD;
BEGIN
    -- 1. Update BatchDetail.DocumentID to match the ID of the first page in each group
    -- This makes DocumentID globally unique while preserving grouping.
    UPDATE BatchDetail bd
    SET DocumentId = (
        SELECT MIN(ID) 
        FROM BatchDetail 
        WHERE BatchID = bd.BatchID AND DocumentId = bd.DocumentId
    )
    WHERE DocumentId IS NOT NULL;

    -- 2. Update OCRResults to use the new IDs
    -- This is an approximate fix for legacy data
    UPDATE OCRResults ocr
    SET DocId = bd.DocumentId
    FROM BatchDetail bd
    WHERE ocr.DocId = bd.DocumentId;

    -- 3. Update DocIntelResults to use the new IDs
    UPDATE DocIntelResults dir
    SET DocId = bd.DocumentId
    FROM BatchDetail bd
    WHERE dir.DocId = bd.DocumentId;

    -- 4. Update dynamic tables (doctable_X)
    FOR r IN (SELECT table_name FROM information_schema.tables WHERE table_name LIKE 'doctable_%') LOOP
        EXECUTE format('
            UPDATE %I dt
            SET docid = bd.DocumentId
            FROM BatchDetail bd
            WHERE dt.docid = bd.DocumentId
        ', r.table_name);
    END LOOP;

    -- 5. Update ReleaseLog
    UPDATE ReleaseLog rl
    SET DocId = bd.DocumentId
    FROM BatchDetail bd
    WHERE rl.DocId = bd.DocumentId;

-- 6. DocStatus update removed (table merged into BatchDetail)
END $$;
