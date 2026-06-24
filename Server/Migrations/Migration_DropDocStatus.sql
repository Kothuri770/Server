-- =========================================================================
-- MIGRATION: Remove redundant DocStatus table
-- Document status is now handled by the BatchDetail table
-- =========================================================================

-- For PostgreSQL
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'docstatus') THEN
        DROP TABLE docstatus;
    END IF;
END $$;

-- For SQL Server
IF OBJECT_ID('DocStatus', 'U') IS NOT NULL
BEGIN
    DROP TABLE DocStatus;
END
GO
