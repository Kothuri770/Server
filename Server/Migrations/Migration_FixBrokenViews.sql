-- ======================================================
-- REGISTRATION & REPAIR SCRIPT 
-- For both PostgreSQL and SQL Server
-- ======================================================

-- 1. OCR Table Readiness (Common Step)
-- Delete any legacy orphaned records that might cause unique constraint violations
-- (Optional: Only if you want to start fresh with OCR for existing batches)
-- DELETE FROM DocIntelResults WHERE DocId NOT IN (SELECT ID FROM BatchDetail);
-- DELETE FROM OCRResults WHERE DocId NOT IN (SELECT ID FROM BatchDetail);


-- ======================================================
-- POSTGRESQL SECTION
-- ======================================================

-- Repair BatchDetailWithDocType View for PG
CREATE OR REPLACE VIEW BatchDetailWithDocType AS
SELECT ID, BatchID, PageNo, FileName, Format, 
       DocPage, Status, doctypeid AS DocTypeId, DocName, pageName, originalfilename
FROM BatchDetail;

-- Repair jobmonitorreportquery View for PG (Restores Monitor UI)
CREATE OR REPLACE VIEW jobmonitorreportquery AS
 SELECT batch.id,
    batch.batchname,
    batchlog.batchtype,
        CASE
            WHEN steps.stepname LIKE '% Separation%' THEN 'Classification'
            ELSE steps.stepname
        END AS task,
        CASE
            WHEN batch.batchstatus = 'A' THEN 'Active'
            WHEN batch.batchstatus = 'I' THEN 'InActive'
            WHEN batch.batchstatus = 'P' THEN 'Running'
            WHEN batch.batchstatus = 'H' THEN 'Hold'
            ELSE NULL
        END AS batchstatus,
    batch.createdon,
    COUNT(DISTINCT batchdetail.DocName) AS documentcount,
    COUNT(DISTINCT batchdetail.pageno) AS pagecount,
    batchactions.username
   FROM batch
     JOIN steps ON batch.stepId = steps.Id
     JOIN batchlog ON batch.Id = batchlog.batchId
     JOIN batchactions ON batch.Id = batchactions.batchId
     JOIN batchdetail ON batchactions.BatchID = batchdetail.BatchID
   WHERE batchactions.actionname = 'CREATE' AND batchdetail.status = 'A'
   GROUP BY batch.id, batch.batchname, batch.createdon, steps.stepname, batch.batchstatus, batch.stepid, batchlog.batchtype, batchactions.username;


-- ======================================================
-- SQL SERVER SECTION (Uncomment if using SQL Server)
-- ======================================================

/*
-- Repair BatchDetailWithDocType View for SQL Server
CREATE OR ALTER VIEW BatchDetailWithDocType AS
SELECT ID, BatchID, PageNo, FileName, Format, 
       DocPage, Status, doctypeid AS DocTypeId, DocName, pageName, originalfilename
FROM BatchDetail;
GO

-- Repair jobmonitorreportquery View for SQL Server (Restores Monitor UI)
CREATE OR ALTER VIEW jobmonitorreportquery AS
 SELECT batch.id,
    batch.batchname,
    batchlog.batchtype,
        CASE
            WHEN steps.stepname LIKE '% Separation%' THEN 'Classification'
            WHEN steps.stepname NOT LIKE '% Separation%' THEN steps.stepname
            ELSE NULL
        END AS task,
        CASE
            WHEN batch.batchstatus = 'A' THEN 'Active'
            WHEN batch.batchstatus = 'I' THEN 'InActive'
            WHEN batch.batchstatus = 'P' THEN 'Running'
            WHEN batch.batchstatus = 'H' THEN 'Hold'
            ELSE NULL
        END AS batchstatus,
    batch.createdon,
    COUNT(DISTINCT batchdetail.DocName) AS documentcount,
    COUNT(DISTINCT batchdetail.pageno) AS pagecount,
    batchactions.username
   FROM batch
     JOIN steps ON batch.stepid = steps.id
     JOIN batchlog ON batch.id = batchlog.batchid
     JOIN batchactions ON batch.id = batchactions.batchid
     JOIN batchdetail ON batchactions.batchid = batchdetail.batchid
   WHERE batchactions.actionname = 'CREATE' AND batchdetail.status = 'A'
   GROUP BY batch.id, batch.batchname, batch.createdon, steps.stepname, batch.batchstatus, batch.stepid, batchlog.batchtype, batchactions.username;
GO
*/
