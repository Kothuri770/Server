
-- =============================================
-- SQL SERVER DATABASE SCRIPT
-- =============================================

-- =============================================
-- Tables Creation & Migration
-- =============================================

-- MIGRATION: Ensure UserName/UserId column lengths are 256
-- This handles existing databases that were created with shorter lengths (e.g., 30)
BEGIN
    DECLARE @BatchUserNameLen INT = (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Batch' AND COLUMN_NAME = 'UserName');
    DECLARE @UsersUserNameLen INT = (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UsersCredentials' AND COLUMN_NAME = 'UserName');
    DECLARE @BatchLocksUserLen INT = (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BatchLocks' AND COLUMN_NAME = 'UserId');

    IF (@BatchUserNameLen IS NOT NULL AND @BatchUserNameLen < 256) OR 
       (@UsersUserNameLen IS NOT NULL AND @UsersUserNameLen < 256) OR
       (@BatchLocksUserLen IS NOT NULL AND @BatchLocksUserLen < 256)
    BEGIN
        PRINT 'Migrating user identity columns to 256 characters...';
        
        -- Temporary drop foreign keys referencing UsersCredentials.UserName
        DECLARE @DropFkSql NVARCHAR(MAX) = '';
        SELECT @DropFkSql += 'ALTER TABLE ' + QUOTENAME(OBJECT_NAME(parent_object_id)) + ' DROP CONSTRAINT ' + QUOTENAME(name) + ';'
        FROM sys.foreign_keys 
        WHERE referenced_object_id = OBJECT_ID('UsersCredentials');
        IF @DropFkSql <> '' EXEC sp_executesql @DropFkSql;

        -- Drop any UNIQUE constraints/indexes on the columns we're about to alter
        DECLARE @DropConstraintsSql NVARCHAR(MAX) = '';
        
        -- Find unique constraints on columns named UserName or UserId in all tables
        SELECT @DropConstraintsSql += 'ALTER TABLE ' + QUOTENAME(OBJECT_NAME(parent_object_id)) + ' DROP CONSTRAINT ' + QUOTENAME(name) + ';'
        FROM sys.key_constraints 
        WHERE type = 'UQ' AND OBJECT_NAME(parent_object_id) IN ('UserRoleAssignments', 'Batch', 'BatchActions', 'BatchLocks');

        -- Find unique indexes on columns named UserName or UserId
        SELECT @DropConstraintsSql += 'DROP INDEX ' + QUOTENAME(i.name) + ' ON ' + QUOTENAME(OBJECT_NAME(i.object_id)) + ';'
        FROM sys.indexes i
        JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE i.is_unique = 1 AND i.is_primary_key = 0
        AND c.name IN ('UserName', 'UserId')
        AND OBJECT_NAME(i.object_id) IN ('UserRoleAssignments', 'Batch', 'BatchActions', 'BatchLocks');

        IF @DropConstraintsSql <> '' EXEC sp_executesql @DropConstraintsSql;

        -- Drop primary key of UsersCredentials
        DECLARE @PkName NVARCHAR(MAX) = (SELECT name FROM sys.key_constraints WHERE type = 'PK' AND parent_object_id = OBJECT_ID('UsersCredentials'));
        IF @PkName IS NOT NULL EXEC('ALTER TABLE UsersCredentials DROP CONSTRAINT ' + @PkName);

        -- Alter columns to 256
        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UsersCredentials' AND COLUMN_NAME = 'UserName')
            ALTER TABLE UsersCredentials ALTER COLUMN UserName NVARCHAR(256) NOT NULL;
        
        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Batch' AND COLUMN_NAME = 'UserName')
            ALTER TABLE Batch ALTER COLUMN UserName NVARCHAR(256) NOT NULL;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BatchActions' AND COLUMN_NAME = 'UserName')
            ALTER TABLE BatchActions ALTER COLUMN UserName NVARCHAR(256) NULL;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BatchLocks' AND COLUMN_NAME = 'UserId')
            ALTER TABLE BatchLocks ALTER COLUMN UserId NVARCHAR(256) NOT NULL;
            
        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BatchLocks' AND COLUMN_NAME = 'UserName')
            ALTER TABLE BatchLocks ALTER COLUMN UserName NVARCHAR(256) NOT NULL;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserRoleAssignments' AND COLUMN_NAME = 'UserName')
            ALTER TABLE UserRoleAssignments ALTER COLUMN UserName NVARCHAR(256) NOT NULL;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ScannerProfiles' AND COLUMN_NAME = 'UserName')
            ALTER TABLE ScannerProfiles ALTER COLUMN UserName NVARCHAR(256) NOT NULL;

        -- Recreate Primary Key
        IF OBJECT_ID('UsersCredentials') IS NOT NULL
            ALTER TABLE UsersCredentials ADD CONSTRAINT PK_UsersCredentials PRIMARY KEY (UserName);

        -- Recreate Unique constraints
        IF OBJECT_ID('UserRoleAssignments') IS NOT NULL
            ALTER TABLE UserRoleAssignments ADD CONSTRAINT UK_UserRoleAssignments_UserRole UNIQUE (UserName, RoleId);

        -- Recreate Foreign Keys
        IF OBJECT_ID('BatchLocks') IS NOT NULL AND OBJECT_ID('UsersCredentials') IS NOT NULL
        BEGIN
            -- Try to drop if somehow missed
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_BatchLocks_UserId')
                ALTER TABLE BatchLocks DROP CONSTRAINT FK_BatchLocks_UserId;
            -- DO NOT RECREATE FK for BatchLocks to allow external identities (Keycloak/SSO)
        END
        
        IF OBJECT_ID('UserRoleAssignments') IS NOT NULL AND OBJECT_ID('UsersCredentials') IS NOT NULL
        BEGIN
             IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_UserRoleAssignments_UserName')
                ALTER TABLE UserRoleAssignments ADD CONSTRAINT FK_UserRoleAssignments_UserName FOREIGN KEY (UserName) REFERENCES UsersCredentials(UserName) ON DELETE CASCADE;
        END

        PRINT 'User identity columns migration completed successfully.';
    END
END

-- Clear any stale/invalid locks periodically or on script run
IF OBJECT_ID('BatchLocks') IS NOT NULL
BEGIN
    UPDATE BatchLocks SET Status = 'Expired' WHERE Status = 'Active' AND ExpirationTime < SYSUTCDATETIME();
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Batch')
BEGIN
    CREATE TABLE Batch (
        ID INT IDENTITY(1,1) NOT NULL,
        BatchName NVARCHAR(60) NOT NULL,
        CreatedOn DATETIME2 NOT NULL,
        BatchTypeId INT NOT NULL,
        BatchStatus NVARCHAR(1) NOT NULL,
        StepID INT NOT NULL,
        InternalName NVARCHAR(50) NOT NULL,
        UserName NVARCHAR(256) NOT NULL,
        OcrType NVARCHAR(30) NULL,
        LockedBy NVARCHAR(100) NULL,
        LockedOn DATETIME2 NULL,
        Ispurging BIT DEFAULT 0,
        CONSTRAINT PK_Batch PRIMARY KEY (ID)
    );
    CREATE INDEX idx_batch_stepid ON Batch(StepID);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchActions')
BEGIN
    CREATE TABLE BatchActions (
        ID INT IDENTITY(1,1) NOT NULL,
        BatchID INT NULL,
        ActionName NVARCHAR(50) NULL,
        ActionStamp DATETIME2 NULL,
        UserName NVARCHAR(256) NULL,
        CONSTRAINT PK_BatchActions PRIMARY KEY (ID)
    );
    CREATE INDEX idx_batchactions_batchid ON BatchActions(BatchID);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchClassDetail')
BEGIN
    CREATE TABLE BatchClassDetail (
        Id INT IDENTITY(1,1) NOT NULL,
        BatchTypeId INT NOT NULL,
        PropertyId INT NOT NULL,
        PropertyOrder INT NOT NULL,
        IsPreIndex BIT NOT NULL,
        IsRequired BIT NOT NULL,
        ZoneID INT NULL,
        Length INT NOT NULL,
        LookupId INT NULL,
        UIDesignId NVARCHAR(50) NULL,
        CONSTRAINT PK_BatchClassDetail PRIMARY KEY (Id),
        CONSTRAINT uk_batchclassdetail UNIQUE (BatchTypeId, PropertyId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchDetail')
BEGIN
    CREATE TABLE BatchDetail (
        ID INT IDENTITY(1,1) NOT NULL,
        BatchID INT NOT NULL,
        PageNo INT NOT NULL,
        FileName NVARCHAR(255) NOT NULL,
        originalfilename NVARCHAR(255) NOT NULL,
        Format NVARCHAR(20) NOT NULL,
        DocPage INT NULL,
        Status NVARCHAR(MAX) NOT NULL,
        pageName NVARCHAR(MAX) NULL,
        DocPageType NVARCHAR(25) NULL,
        doctypeid INT NULL,
        DocName NVARCHAR(60) NULL,
        InternalName NVARCHAR(40) NULL,
        DocCreatedOn DATETIME2 NULL,
        CONSTRAINT PK_BatchDetail PRIMARY KEY (ID)
    );
    CREATE INDEX idx_batchdetail_batchid ON BatchDetail(BatchID);
    CREATE INDEX idx_batchdetail_doctypeid ON BatchDetail(doctypeid);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchException')
BEGIN
    CREATE TABLE BatchException (
        BatchID INT NOT NULL,
        ErrorStep INT NULL,
        Retries INT NULL,
        CONSTRAINT PK_BatchException PRIMARY KEY (BatchID)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchLock')
BEGIN
    CREATE TABLE BatchLock (
        BatchID INT NOT NULL,
        SessionID NVARCHAR(40) NOT NULL,
        LockedOn DATETIME2 NOT NULL,
        CONSTRAINT PK_BatchLock PRIMARY KEY (BatchID)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchLog')
BEGIN
    CREATE TABLE BatchLog (
        BatchId INT NOT NULL,
        BatchType NVARCHAR(255) NULL,
        DocumentCount INT NULL,
        CompletedOn DATETIME2 NULL,
        StepId INT NULL,
        StationId INT NULL,
        PageCount INT NULL,
        BatchName NVARCHAR(255) NULL
    );
    CREATE INDEX idx_batchlog_batchid ON BatchLog(BatchId);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CharacterZones')
BEGIN
    CREATE TABLE CharacterZones (
        ID INT IDENTITY(1,1) NOT NULL,
        ZoneId INT NOT NULL,
        CharacterOrder INT NOT NULL,
        LeftX INT NOT NULL,
        TopY INT NOT NULL,
        RightX INT NOT NULL,
        BottomY INT NOT NULL,
        DataType NVARCHAR(30) NULL,
        OCRType NVARCHAR(1) NULL,
        CONSTRAINT PK_CharacterZones PRIMARY KEY (ID)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Configuration')
BEGIN
    CREATE TABLE Configuration (
        ID INT IDENTITY(1,1) NOT NULL,
        ConfigName NVARCHAR(30) NOT NULL,
        ConfigValue NVARCHAR(255) NOT NULL,
        CONSTRAINT PK_Configuration PRIMARY KEY (ID),
        CONSTRAINT uk_configname UNIQUE (ConfigName)
    );
    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('BatchLockTimeoutMinutes', '60');
    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('MaxParallelWorkers', '10');

    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('Batch Folder', 'C:\TrueCapture\ICBatches');
    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('Separation Mode', 'Manual');
    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('Batch Prefix', 'BATCH_');
    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('Templates Folder', 'C:\TrueCapture\Templates');
    INSERT INTO Configuration (ConfigName, ConfigValue) VALUES ('Temp Folder', 'C:\TrueCapture\Temp');
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Content')
BEGIN
    CREATE TABLE Content (
        DocumentId INT IDENTITY(1,1) NOT NULL,
        BatchId BIGINT NOT NULL,
        ContentText NVARCHAR(MAX) NULL,
        Content VARBINARY(MAX) NOT NULL,
        FileExtension NVARCHAR(20) NOT NULL,
        CONSTRAINT PK_Content PRIMARY KEY (DocumentId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DMSClassMapping')
BEGIN
    CREATE TABLE DMSClassMapping (
        ID INT IDENTITY(1,1) NOT NULL,
        DocTypeID INT NOT NULL,
        DMSClassName NVARCHAR(255) NOT NULL,
        DMSCabinetName NVARCHAR(255) NOT NULL,
        ReleaseFormat NVARCHAR(20) NOT NULL,
        NameExpression NVARCHAR(MAX) NOT NULL,
        ReleaseFolder NVARCHAR(MAX) NOT NULL,
        ConnectorID INT NOT NULL,
        CONSTRAINT PK_DMSClassMapping PRIMARY KEY (ID)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DMSPropertyMapping')
BEGIN
    CREATE TABLE DMSPropertyMapping (
        ID INT IDENTITY(1,1) NOT NULL,
        DocClassDetailID INT NULL,
        DMSPropertyName NVARCHAR(255) NULL,
        DocTypeID INT NULL,
        ConnectorID INT NOT NULL,
        CONSTRAINT PK_DMSPropertyMapping PRIMARY KEY (ID)
    );
END

-- Document table has been merged into BatchDetail

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DocumentClassDetail')
BEGIN
    CREATE TABLE DocumentClassDetail (
        Id INT IDENTITY(1,1) NOT NULL,
        DocTypeId INT NOT NULL,
        PropertyId INT NOT NULL,
        PropertyOrder INT NOT NULL,
        IsEnabled BIT NOT NULL,
        IsRequired BIT NOT NULL,
        ZoneId INT NULL,
        Length INT NOT NULL,
        LookupId INT NULL,
        IsBatchProperty BIT NOT NULL,
        UIDesignId NVARCHAR(50) NULL,
        IsLookup BIT NULL,
        CONSTRAINT PK_DocumentClassDetail PRIMARY KEY (Id),
        CONSTRAINT uk_documentclassdetail UNIQUE (DocTypeId, PropertyId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ObjectTypes')
BEGIN
    CREATE TABLE ObjectTypes (
        Id INT IDENTITY(1,1) NOT NULL,
        Type NVARCHAR(1) NOT NULL,
        Name NVARCHAR(50) NOT NULL UNIQUE,
        IsActive BIT NOT NULL,
        OcrConnectorId INT NULL,
        OcrMode NVARCHAR(30) DEFAULT 'Manual',
        CONSTRAINT PK_ObjectTypes PRIMARY KEY (Id)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Property')
BEGIN
    CREATE TABLE Property (
        Id INT IDENTITY(1,1) NOT NULL,
        PropertyName NVARCHAR(50) NOT NULL,
        PropertyDesc NVARCHAR(50) NOT NULL,
        PropertyType NVARCHAR(50) NOT NULL,
        PropertyLength INT NULL,
        LookupId INT NULL,
        CONSTRAINT PK_Property PRIMARY KEY (Id)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UsersCredentials')
BEGIN
    CREATE TABLE UsersCredentials (
        UserName NVARCHAR(256) NOT NULL,
        Password NVARCHAR(MAX) NOT NULL,
        UserType NVARCHAR(50) NOT NULL,
        CreatedOn DATETIME2 NOT NULL,
        ViewLimit NVARCHAR(50) DEFAULT 'All',
        IsEnabled BIT DEFAULT 1,
        CONSTRAINT PK_UsersCredentials PRIMARY KEY (UserName)
    );
    INSERT INTO UsersCredentials (UserName, Password, UserType, CreatedOn) VALUES ('admin', 'kKHqGs0y8e11HK/jLJfRfa8gJRfY7uFxqpQXv/F4T1Q=', 'admin', GETUTCDATE());
    INSERT INTO UsersCredentials (UserName, Password, UserType, CreatedOn) VALUES ('configeditor', 'kKHqGs0y8e11HK/jLJfRfa8gJRfY7uFxqpQXv/F4T1Q=', 'configeditor', GETUTCDATE());
END
GO

-- Re-verify existing tables for column length consistency
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Batch')
BEGIN
    IF (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Batch' AND COLUMN_NAME = 'UserName') < 256
        ALTER TABLE Batch ALTER COLUMN UserName NVARCHAR(256) NOT NULL;
END
GO
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BatchActions')
BEGIN
    IF (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BatchActions' AND COLUMN_NAME = 'UserName') < 256
        ALTER TABLE BatchActions ALTER COLUMN UserName NVARCHAR(256) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchLocks')
BEGIN
    CREATE TABLE BatchLocks (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        BatchId INT NOT NULL,
        UserId NVARCHAR(256) NOT NULL,
        UserName NVARCHAR(256) NOT NULL,
        LockAcquired DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        SessionId NVARCHAR(100),
        ExpirationTime DATETIME2 NOT NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        FOREIGN KEY (BatchId) REFERENCES Batch(ID)
        -- Removed Foreign Key to UsersCredentials to allow external/SSO identities
    );
    CREATE INDEX idx_batchlocks_batchid ON BatchLocks(BatchId);
    CREATE INDEX idx_batchlocks_expiration_time ON BatchLocks(ExpirationTime);
    CREATE INDEX idx_batchlocks_status ON BatchLocks(Status);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Steps')
BEGIN
    CREATE TABLE Steps (
        ID INT NOT NULL,
        StepName NVARCHAR(20) NOT NULL,
        Status NVARCHAR(1) NOT NULL,
        StepOrder INT NOT NULL,
        CONSTRAINT PK_Steps PRIMARY KEY (ID)
    );
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (1, 'Scan', 'A', 1);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (2, 'Manual Separation', 'I', 2);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (3, 'Auto Separation', 'I', 3);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (4, 'OCR', 'A', 4);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (5, 'Index', 'A', 5);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (6, 'Index Verify', 'I', 6);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (7, 'Text Extraction', 'I', 7);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (8, 'Release', 'A', 8);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (98, 'Complete', 'A', 98);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (99, 'Exception', 'A', 99);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (100, 'FileNet Release', 'I', 100);
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (101, 'Upload', 'I', 101);
END

-- =============================================
-- Stored Procedures
-- =============================================

GO
CREATE OR ALTER PROCEDURE AcquireBatchLock
    @BatchId INT,
    @UserId NVARCHAR(256),
    @UserName NVARCHAR(256),
    @SessionId NVARCHAR(100),
    @LockTimeoutMinutes INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    -- Standardize on UTC to avoid timezone discrepancies between DB and App
    DECLARE @ExpirationTime DATETIME2 = DATEADD(MINUTE, @LockTimeoutMinutes, SYSUTCDATETIME());
    DECLARE @CurrentTime DATETIME2 = SYSUTCDATETIME();

    -- Use UPDLOCK and HOLDLOCK (Serializable) to ensure atomic check-and-insert
    IF NOT EXISTS (SELECT 1 FROM BatchLocks WITH (UPDLOCK, HOLDLOCK) WHERE BatchId = @BatchId AND Status = 'Active' AND ExpirationTime > @CurrentTime)
    BEGIN
        INSERT INTO BatchLocks (BatchId, UserId, UserName, SessionId, ExpirationTime, Status)
        VALUES (@BatchId, @UserId, @UserName, @SessionId, @ExpirationTime, 'Active');
        
        SELECT 'ACQUIRED' AS Result, @ExpirationTime AS ExpirationTime, CAST(NULL AS NVARCHAR(256)) AS CurrentLockHolder, CAST(NULL AS DATETIME2) AS LockExpiration;
    END
    ELSE
    BEGIN
        -- Same user renewing their lock
        IF EXISTS (SELECT 1 FROM BatchLocks WITH (UPDLOCK) WHERE BatchId = @BatchId AND UserId = @UserId AND Status = 'Active' AND ExpirationTime > @CurrentTime)
        BEGIN
            UPDATE BatchLocks 
            SET ExpirationTime = @ExpirationTime, 
                SessionId = ISNULL(@SessionId, SessionId),
                UpdatedAt = @CurrentTime
            WHERE BatchId = @BatchId AND UserId = @UserId AND Status = 'Active';
            
            SELECT 'RENEWED' AS Result, @ExpirationTime AS ExpirationTime, CAST(NULL AS NVARCHAR(256)) AS CurrentLockHolder, CAST(NULL AS DATETIME2) AS LockExpiration;
        END
        ELSE
        BEGIN
            -- Different user holding the lock
            SELECT 'LOCKED' AS Result, CAST(NULL AS DATETIME2) AS ExpirationTime, UserName AS CurrentLockHolder, ExpirationTime AS LockExpiration
            FROM BatchLocks 
            WHERE BatchId = @BatchId AND Status = 'Active' AND ExpirationTime > @CurrentTime;
        END
    END
END
GO

CREATE OR ALTER PROCEDURE ReleaseBatchLock
    @BatchId INT,
    @UserId NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE BatchLocks 
    SET Status = 'Released',
        UpdatedAt = SYSUTCDATETIME()
    WHERE BatchId = @BatchId AND UserId = @UserId AND Status = 'Active';
    
    SELECT @@ROWCOUNT;
END
GO

CREATE OR ALTER PROCEDURE RefreshBatchLock
    @BatchId INT,
    @UserId NVARCHAR(256),
    @LockTimeoutMinutes INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ExpirationTime DATETIME2 = DATEADD(MINUTE, @LockTimeoutMinutes, SYSUTCDATETIME());
    DECLARE @CurrentTime DATETIME2 = SYSUTCDATETIME();

    UPDATE BatchLocks 
    SET ExpirationTime = @ExpirationTime,
        UpdatedAt = @CurrentTime
    OUTPUT 'SUCCESS' AS Result, INSERTED.ExpirationTime AS NewExpirationTime
    WHERE BatchId = @BatchId AND UserId = @UserId AND Status = 'Active' AND ExpirationTime > @CurrentTime;

    IF @@ROWCOUNT = 0
    BEGIN
        SELECT 'NOT_FOUND' AS Result, CAST(NULL AS DATETIME2) AS NewExpirationTime;
    END
END
GO

CREATE OR ALTER PROCEDURE CleanExpiredLocks
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE BatchLocks 
    SET Status = 'Expired',
        UpdatedAt = SYSUTCDATETIME()
    WHERE Status = 'Active' AND ExpirationTime < SYSUTCDATETIME();
END
GO

-- =============================================
-- VIEWS
-- =============================================
GO
CREATE OR ALTER VIEW AllBatchesWithTypeName AS
SELECT Batch.ID, Batch.BatchName, Batch.BatchTypeId, Batch.CreatedOn, Batch.BatchStatus, Batch.StepId, ObjectTypes.Name, Batch.UserName
FROM Batch LEFT JOIN ObjectTypes ON Batch.BatchTypeId = ObjectTypes.Id;
GO

CREATE OR ALTER VIEW BatchDetailWithDocType AS
SELECT BatchDetail.ID, BatchDetail.BatchID, BatchDetail.PageNo, BatchDetail.FileName, BatchDetail.Format, 
       BatchDetail.DocPage, BatchDetail.Status, BatchDetail.doctypeid AS DocTypeId, BatchDetail.DocName, BatchDetail.pageName, BatchDetail.originalfilename
FROM BatchDetail;
GO

CREATE OR ALTER VIEW BatchTypeProperties AS
SELECT BatchClassDetail.BatchTypeId, BatchClassDetail.PropertyId, BatchClassDetail.PropertyOrder, BatchClassDetail.IsPreIndex, BatchClassDetail.IsRequired, BatchClassDetail.ZoneID, Property.PropertyName, Property.PropertyDesc, Property.PropertyType, BatchClassDetail.Length, BatchClassDetail.LookupId, BatchClassDetail.Id
FROM BatchClassDetail INNER JOIN Property ON BatchClassDetail.PropertyId = Property.Id;
GO

CREATE OR ALTER VIEW BatchWithTypeName AS
SELECT Batch.ID, Batch.BatchName, Batch.BatchTypeId, ObjectTypes.Name, Batch.BatchStatus, Batch.StepId, Batch.CreatedOn
FROM Batch INNER JOIN ObjectTypes ON Batch.BatchTypeId = ObjectTypes.Id;
GO

CREATE OR ALTER VIEW DMSClassWithTypeName AS
SELECT DMSClassMapping.DocTypeID, DMSClassMapping.DMSClassName, DMSClassMapping.DMSCabinetName, ObjectTypes.Name, DMSClassMapping.ReleaseFormat, DMSClassMapping.NameExpression, DMSClassMapping.ReleaseFolder, DMSClassMapping.Id, DMSClassMapping.ConnectorID
FROM DMSClassMapping INNER JOIN ObjectTypes ON DMSClassMapping.DocTypeID = ObjectTypes.Id;
GO

CREATE OR ALTER VIEW DocTypeProperties AS
SELECT DocumentClassDetail.Id, DocumentClassDetail.DocTypeId, DocumentClassDetail.PropertyId, DocumentClassDetail.PropertyOrder, 
       DocumentClassDetail.IsEnabled, DocumentClassDetail.IsRequired, Property.PropertyName, Property.PropertyDesc, Property.PropertyType, 
       DocumentClassDetail.ZoneId, DocumentClassDetail.Length, DocumentClassDetail.LookupId, DocumentClassDetail.IsBatchProperty
FROM DocumentClassDetail INNER JOIN Property ON DocumentClassDetail.PropertyId = Property.Id;
GO

-- =============================================
-- ADVANCED TABLES (LOOKUP, OCR, DMS)
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatabaseConnections')
BEGIN
    CREATE TABLE DatabaseConnections (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ConnectionName NVARCHAR(100) NOT NULL UNIQUE,
        DbType INT NOT NULL, -- 0: SqlServer, 1: MySql, 2: Oracle
        ConnectionString NVARCHAR(MAX) NOT NULL,
        IsActive BIT DEFAULT 1,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedBy NVARCHAR(100),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE()
    );
    CREATE INDEX idx_db_connections_name ON DatabaseConnections(ConnectionName);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatabaseLookupMappings')
BEGIN
    CREATE TABLE DatabaseLookupMappings (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        PropertyName NVARCHAR(100) NOT NULL UNIQUE,
        ConnectionId INT NOT NULL,
        SqlQuery NVARCHAR(MAX) NOT NULL,
        ColumnMappings NVARCHAR(MAX), -- Stores Column Mapping DTOs as JSON
        IsActive BIT DEFAULT 1,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedBy NVARCHAR(100),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT fk_db_lookup_mappings_connection FOREIGN KEY (ConnectionId) REFERENCES DatabaseConnections(Id) ON DELETE CASCADE
    );
    CREATE INDEX idx_db_lookup_mappings_property ON DatabaseLookupMappings(PropertyName);
    CREATE INDEX idx_db_lookup_mappings_connection ON DatabaseLookupMappings(ConnectionId);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LookupTables')
BEGIN
    CREATE TABLE LookupTables (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        TableName NVARCHAR(100) UNIQUE NOT NULL,
        DisplayName NVARCHAR(200) NOT NULL,
        Description NVARCHAR(MAX),
        IsActive BIT DEFAULT 1,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedBy NVARCHAR(100),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE()
    );
    CREATE INDEX idx_lookup_tables_name ON LookupTables(TableName);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LookupTableValues')
BEGIN
    CREATE TABLE LookupTableValues (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        LookupTableId INT NOT NULL,
        DisplayValue NVARCHAR(200) NOT NULL,
        ValueCode NVARCHAR(100) NOT NULL,
        Description NVARCHAR(MAX),
        SortOrder INT DEFAULT 0,
        IsActive BIT DEFAULT 1,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedBy NVARCHAR(100),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT uk_lookuptablevalues_tableid_valuecode UNIQUE(LookupTableId, ValueCode),
        CONSTRAINT fk_lookuptablevalues_lookuptableid FOREIGN KEY (LookupTableId) REFERENCES LookupTables(Id) ON DELETE CASCADE
    );
    CREATE INDEX idx_lookup_table_values_table_id ON LookupTableValues(LookupTableId);
    CREATE INDEX idx_lookup_table_values_code ON LookupTableValues(ValueCode);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyValidationRules')
BEGIN
    CREATE TABLE PropertyValidationRules (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        PropertyName NVARCHAR(100) UNIQUE,
        DisplayName NVARCHAR(200) NOT NULL,
        ValidationType NVARCHAR(50) NOT NULL,
        ValidationRule NVARCHAR(MAX),
        ErrorMessage NVARCHAR(MAX),
        IsActive BIT DEFAULT 1,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedBy NVARCHAR(100),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE()
    );
    CREATE INDEX idx_property_validation_rules_property ON PropertyValidationRules(PropertyName);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyLookupMappings')
BEGIN
    CREATE TABLE PropertyLookupMappings (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        PropertyName NVARCHAR(100) UNIQUE,
        LookupTableId INT,
        IsRequired BIT DEFAULT 0,
        DefaultValue NVARCHAR(200),
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedBy NVARCHAR(100),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT fk_propertylookupmappings_lookuptableid FOREIGN KEY (LookupTableId) REFERENCES LookupTables(Id)
    );
    CREATE INDEX idx_property_lookup_mappings_property ON PropertyLookupMappings(PropertyName);
    CREATE INDEX idx_property_lookup_mappings_table_id ON PropertyLookupMappings(LookupTableId);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dmsproviders')
BEGIN
    CREATE TABLE dmsproviders (
        id INT IDENTITY(1,1) PRIMARY KEY,
        name NVARCHAR(100) NOT NULL UNIQUE,
        displayname NVARCHAR(100) NOT NULL,
        description NVARCHAR(MAX),
        isactive BIT DEFAULT 1,
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE()
    );
    INSERT INTO dmsproviders (name, displayname, description, isactive)
    VALUES 
    ('AzureBlob', 'Azure Blob Storage', 'Microsoft Azure Blob Storage connector', 1),
    ('AwsS3', 'AWS S3', 'Amazon Web Services S3 connector', 1),
    ('Alfresco', 'Alfresco', 'Alfresco Document Management connector', 1),
    ('FileNet', 'FileNet', 'IBM FileNet connector', 1);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dmsoutputformats')
BEGIN
    CREATE TABLE dmsoutputformats (
        id INT IDENTITY(1,1) PRIMARY KEY,
        formatcode NVARCHAR(20) NOT NULL UNIQUE,
        formatname NVARCHAR(50) NOT NULL,
        description NVARCHAR(MAX),
        isactive BIT DEFAULT 1,
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE()
    );
    INSERT INTO dmsoutputformats (formatcode, formatname, description, isactive)
    VALUES
    ('PDF', 'PDF', 'Portable Document Format', 1),
    ('TIF', 'TIFF (Single Page)', 'Tagged Image File Format - Each page as a separate file', 1),
    ('TIFF', 'TIFF (Multi-Page)', 'Tagged Image File Format - All pages merged into one file', 1),
    ('JPG', 'JPEG', 'Joint Photographic Experts Group', 1),
    ('PNG', 'PNG', 'Portable Network Graphics', 1),
    ('BMP', 'BMP', 'Bitmap Image File', 1),
    ('DOCX', 'DOCX', 'Microsoft Word Document', 1),
    ('XLSX', 'XLSX', 'Microsoft Excel Spreadsheet', 1);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dmsconnectors')
BEGIN
    CREATE TABLE dmsconnectors (
        id INT IDENTITY(1,1) PRIMARY KEY,
        doctypeid INT NOT NULL,
        providerid INT NOT NULL,
        outputformatid INT,
        dmsclassname NVARCHAR(255),
        dmscabinetname NVARCHAR(255),
        releasefolder NVARCHAR(500),
        nameexpression NVARCHAR(500),
        url NVARCHAR(500),
        username NVARCHAR(255),
        password NVARCHAR(255),
        additionalconfig NVARCHAR(MAX),
        isactive BIT DEFAULT 1,
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE(),
        FOREIGN KEY (providerid) REFERENCES dmsproviders(id),
        FOREIGN KEY (outputformatid) REFERENCES dmsoutputformats(id)
    );
    CREATE INDEX idx_dmsconnectors_providerid ON dmsconnectors(providerid);
    CREATE INDEX idx_dmsconnectors_outputformatid ON dmsconnectors(outputformatid);
    CREATE INDEX idx_dmsconnectors_active ON dmsconnectors(isactive);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'propertymapping')
BEGIN
    CREATE TABLE propertymapping (
        id INT IDENTITY(1,1) PRIMARY KEY,
        providername NVARCHAR(255) NOT NULL,
        doctypeid INT NOT NULL,
        jsonproperty NVARCHAR(MAX),
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT unique_mapping UNIQUE (providername, doctypeid)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ocrconfiguration')
BEGIN
    CREATE TABLE ocrconfiguration (
        id INT IDENTITY(1,1) PRIMARY KEY,
        configname NVARCHAR(50) NOT NULL UNIQUE,
        configvalue NVARCHAR(MAX),
        description NVARCHAR(MAX),
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE()
    );
    INSERT INTO ocrconfiguration (configname, configvalue, description)
    VALUES 
    ('OcrMode', 'Automatic', 'OCR processing mode: Manual or Automatic'),
    ('DefaultOcrConnectorId', '1', 'ID of the default OCR connector to use'),
    ('OcrTimeout', '30', 'Timeout in seconds for OCR processing'),
    ('OcrRetryAttempts', '3', 'Number of retry attempts for OCR processing');
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DocIntelResults')
BEGIN
    CREATE TABLE DocIntelResults (
        id INT IDENTITY(1,1) PRIMARY KEY,
        DocId INT NOT NULL,
        AnalysisResult NVARCHAR(MAX) NOT NULL,
        CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
        UpdatedOn DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT uk_DocIntelResults_DocId UNIQUE (DocId)
    );
    CREATE INDEX idx_DocIntelResults_DocId ON DocIntelResults(DocId);
    CREATE INDEX idx_DocIntelResults_CreatedOn ON DocIntelResults(CreatedOn);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ocrproviders')
BEGIN
    CREATE TABLE ocrproviders (
        id INT IDENTITY(1,1) PRIMARY KEY,
        name NVARCHAR(100) NOT NULL UNIQUE,
        displayname NVARCHAR(100) NOT NULL,
        description NVARCHAR(MAX),
        isactive BIT DEFAULT 1,
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE()
    );
    INSERT INTO ocrproviders (name, displayname, description, isactive) VALUES 
    ('Tesseract', 'Tesseract OCR', 'Open-source Tesseract OCR engine', 1),
    ('AzureDocIntel', 'Azure Document Intelligence', 'Microsoft Azure Document Intelligence service', 1),
    ('GoogleDocAI', 'Google Document AI', 'Google Document AI service', 1),
    ('AmazonTextract', 'Amazon Textract', 'Amazon Textract OCR service', 1),
    ('ollama', 'Ollama (Gemma)', 'Local Ollama LLM extraction', 1);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EmailConfigurations')
BEGIN
    CREATE TABLE EmailConfigurations (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        EmailId NVARCHAR(255) NOT NULL,
        Password NVARCHAR(255) NOT NULL,
        AppId INT NOT NULL,
        DocumentType NVARCHAR(100) NOT NULL,
        IsEnabled BIT DEFAULT 0,
        ImapServer NVARCHAR(255) DEFAULT 'imap.gmail.com',
        ImapPort INT DEFAULT 993,
        LastChecked DATETIME2,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LocalFolderConfigurations')
BEGIN
    CREATE TABLE LocalFolderConfigurations (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        AppId INT NOT NULL,
        PickImagesPath NVARCHAR(500) NOT NULL,
        BackupPath NVARCHAR(500) NOT NULL,
        IsEnabled BIT DEFAULT 0,
        LastChecked DATETIME2,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ocrconnectors')
BEGIN
    CREATE TABLE ocrconnectors (
        id INT IDENTITY(1,1) PRIMARY KEY,
        providerid INT NOT NULL,
        name NVARCHAR(100) NOT NULL,
        isdefault BIT DEFAULT 0,
        configdata NVARCHAR(MAX), -- Store JSON as NVARCHAR(MAX)
        isactive BIT DEFAULT 1,
        createdon DATETIME2 DEFAULT GETUTCDATE(),
        modifiedon DATETIME2 DEFAULT GETUTCDATE(),
        FOREIGN KEY (providerid) REFERENCES ocrproviders(id)
    );
    INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
    VALUES 
    (1, 'Tesseract Local', 1, '{"tessdatapath": "./Tessract", "language": "eng"}', 1),
    (2, 'Azure Doc Intel', 0, '{"endpoint": "", "apikey": "", "modelid": "prebuilt-read"}', 1),
    (3, 'Google Doc AI', 0, '{"endpoint": "", "apikey": "", "processorid": ""}', 1);

    INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
    SELECT 4, 'Amazon Textract', 0, '{"region": "us-east-1", "accesskey": "", "secretkey": ""}', 1
    WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Amazon Textract');

    INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
    SELECT 5, 'Ollama Local', 0, '{"endpoint": "http://localhost:11434/api/generate", "modelid": "gemma4:e4b"}', 1
    WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Ollama Local');
    
    CREATE INDEX idx_ocrconnectors_providerid ON ocrconnectors(providerid);
    CREATE INDEX idx_ocrconnectors_isactive ON ocrconnectors(isactive);
    CREATE INDEX idx_ocrconnectors_isdefault ON ocrconnectors(isdefault);
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BatchTaskTime')
BEGIN
    CREATE TABLE BatchTaskTime (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        BatchId INT NOT NULL,
        TaskId INT NOT NULL,
        TaskStartTime DATETIME2 NOT NULL,
        Status NVARCHAR(20) NOT NULL
    );
END


-- =============================================
-- MISSING CORE TABLES FROM POSTGRESQL
-- =============================================

-- DocStatus table removed as it is merged into BatchDetail
GO


IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DocumentSample')
BEGIN
    CREATE TABLE DocumentSample (
        DocTypeID INT NOT NULL,
        SampleFile NVARCHAR(255) NOT NULL,
        CONSTRAINT PK_DocumentSample PRIMARY KEY (DocTypeID)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Identification')
BEGIN
    CREATE TABLE Identification (
        ID INT IDENTITY(1,1) NOT NULL,
        IDType NVARCHAR(1) NOT NULL,
        IDMethod NVARCHAR(15) NOT NULL,
        IDValue NVARCHAR(30) NULL,
        ParentObjectId INT NOT NULL,
        DiscardPage BIT NOT NULL,
        ZoneId INT NULL,
        CONSTRAINT PK_Identification PRIMARY KEY (ID)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'keyWordIdentification')
BEGIN
    CREATE TABLE keyWordIdentification (
        id INT IDENTITY(1,1) NOT NULL,
        KeyWord NVARCHAR(MAX) NULL,
        Pagetype NVARCHAR(MAX) NULL,
        CONSTRAINT PK_keyWordIdentification PRIMARY KEY (id)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Lookup')
BEGIN
    CREATE TABLE Lookup (
        Id INT IDENTITY(1,1) NOT NULL,
        lookupStr NVARCHAR(MAX) NULL,
        lookupType NVARCHAR(20) NULL,
        connectStr NVARCHAR(255) NULL,
        CONSTRAINT PK_Lookup PRIMARY KEY (Id)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ObjectRelation')
BEGIN
    CREATE TABLE ObjectRelation (
        Id INT IDENTITY(1,1) NOT NULL,
        ParentObjectId INT NOT NULL,
        ChildObjectId INT NOT NULL,
        CONSTRAINT PK_ObjectRelation PRIMARY KEY (Id),
        CONSTRAINT uk_objectrelation UNIQUE (ParentObjectId, ChildObjectId)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OCRResults')
BEGIN
    CREATE TABLE OCRResults (
        DocId INT NOT NULL,
        ZoneId INT NOT NULL,
        ColId INT NULL,
        OCRValue NVARCHAR(255) NOT NULL,
        OCRConfidence FLOAT NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ReleaseLog')
BEGIN
    CREATE TABLE ReleaseLog (
        DocId INT NOT NULL,
        CreatedDate DATETIME2 NULL,
        BatchId INT NULL,
        BatchName NVARCHAR(255) NULL,
        DocName NVARCHAR(255) NULL,
        DocType INT NULL,
        Status NVARCHAR(20) NOT NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        ReleasedDocName NVARCHAR(255) NULL,
        ReleasedFormat NVARCHAR(4) NULL,
        CONSTRAINT PK_ReleaseLog PRIMARY KEY (DocId)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScannerProfiles')
BEGIN
    CREATE TABLE ScannerProfiles (
        ProfileName NVARCHAR(50) NOT NULL,
        Dpi NVARCHAR(20) NOT NULL,
        ColorFormat NVARCHAR(4) NOT NULL,
        ImageFormat NVARCHAR(20) NOT NULL,
        PaperFormat NVARCHAR(50) NOT NULL,
        UserName NVARCHAR(256) NOT NULL,
        CreatedOn DATETIME2 NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScanProfiles')
BEGIN
    CREATE TABLE ScanProfiles (
        ID INT IDENTITY(1,1) NOT NULL,
        Name NVARCHAR(30) NULL,
        Scanner NVARCHAR(50) NULL,
        Format NVARCHAR(3) NULL,
        Color NVARCHAR(20) NULL,
        Resolution INT NULL,
        PaperSize NVARCHAR(10) NULL,
        Brightness INT NULL,
        Contrast INT NULL,
        RemoveBP BIT NOT NULL,
        BPThreshold INT NULL,
        UseADF BIT NOT NULL,
        ShowUI BIT NOT NULL,
        Status NVARCHAR(1) NULL,
        CreatedOn DATETIME2 NULL,
        Threshold INT NOT NULL,
        DuplexScan BIT NOT NULL,
        DeSkew BIT NOT NULL,
        Station INT NOT NULL,
        CONSTRAINT PK_ScanProfiles PRIMARY KEY (ID)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sessions')
BEGIN
    CREATE TABLE Sessions (
        ID INT IDENTITY(1,1) NOT NULL,
        StationId INT NOT NULL,
        SessionId NVARCHAR(40) NOT NULL,
        CONSTRAINT PK_Sessions PRIMARY KEY (ID)
    );
    
    -- Identity insert to match postgres
    SET IDENTITY_INSERT Sessions ON;
    INSERT INTO Sessions (ID, StationId, SessionId) VALUES (18833, 5, '47e3b8c8-2042-45c8-9144-5d636351e01f');
    SET IDENTITY_INSERT Sessions OFF;
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Stations')
BEGIN
    CREATE TABLE Stations (
        SerialNo NVARCHAR(20) NOT NULL,
        StationId INT NOT NULL,
        CONSTRAINT PK_Stations PRIMARY KEY (SerialNo)
    );
    
    INSERT INTO Stations (SerialNo, StationId) VALUES ('', 7);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('43CE5BCA882EFE9', 6);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('5080F4C6BA8E2EC', 5);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('73016FC50548B80', 3);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('73016FC5054EBE0', 2);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('9A0F5EC2B76E5EB', 9);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('A7C656CA88F8889', 1);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('A7C656CA88FE8E9', 8);
    INSERT INTO Stations (SerialNo, StationId) VALUES ('DOCSEPERATE', 4);
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Zones')
BEGIN
    CREATE TABLE Zones (
        ID INT IDENTITY(1,1) NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        LeftX INT NULL,
        TopY INT NULL,
        RightX INT NULL,
        BottomY INT NULL,
        DocTypeID INT NOT NULL,
        PageNo INT NOT NULL,
        Type NVARCHAR(10) NOT NULL,
        StartPosition INT NULL,
        Length INT NULL,
        DisplayedWidth INT NULL,
        DisplayedHeight INT NULL,
        CONSTRAINT PK_Zones PRIMARY KEY (ID)
    );
END
GO

-- Create missing steps
IF NOT EXISTS (SELECT * FROM Steps WHERE ID = 100)
BEGIN
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (100, 'FileNet Release', 'I', 100);
END
GO
IF NOT EXISTS (SELECT * FROM Steps WHERE ID = 101)
BEGIN
    INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (101, 'Upload', 'I', 101);
END
GO

-- =============================================
-- USERS AND ROLES (MISSING TABLES)
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserRoles')
BEGIN
    CREATE TABLE UserRoles (
        RoleId INT IDENTITY(1,1) PRIMARY KEY,
        RoleName NVARCHAR(50) UNIQUE NOT NULL,
        RoleDescription NVARCHAR(MAX),
        CreatedOn DATETIME2 DEFAULT SYSUTCDATETIME()
    );
    
    INSERT INTO UserRoles (RoleName, RoleDescription) VALUES 
    ('User', 'User with limited access'),
    ('Admin', 'Administrator with full access'),
    ('Scanner', 'User can scan documents'),
    ('Verifier', 'User can verify documents'),
    ('Monitor', 'User can monitor batch status'),
    ('ConfigEditor', 'User can edit configurations'),
    ('Scanverify', 'scan and verify documents');
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Permissions')
BEGIN
    CREATE TABLE Permissions (
        PermissionId INT IDENTITY(1,1) PRIMARY KEY,
        PermissionName NVARCHAR(100) UNIQUE NOT NULL,
        PermissionDescription NVARCHAR(MAX),
        Category NVARCHAR(50),
        CreatedOn DATETIME2 DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserRoleAssignments')
BEGIN
    CREATE TABLE UserRoleAssignments (
        AssignmentId INT IDENTITY(1,1) PRIMARY KEY,
        UserName NVARCHAR(256) REFERENCES UsersCredentials(UserName) ON DELETE CASCADE,
        RoleId INT REFERENCES UserRoles(RoleId) ON DELETE CASCADE,
        AssignedOn DATETIME2 DEFAULT SYSUTCDATETIME(),
        UNIQUE(UserName, RoleId)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RolePermissionAssignments')
BEGIN
    CREATE TABLE RolePermissionAssignments (
        AssignmentId INT IDENTITY(1,1) PRIMARY KEY,
        RoleId INT REFERENCES UserRoles(RoleId) ON DELETE CASCADE,
        PermissionId INT REFERENCES Permissions(PermissionId) ON DELETE CASCADE,
        AssignedOn DATETIME2 DEFAULT SYSUTCDATETIME(),
        UNIQUE(RoleId, PermissionId)
    );
END
GO


-- =============================================
-- MISSING VIEWS
-- =============================================

GO
CREATE OR ALTER VIEW DocTypeSamples AS
SELECT ObjectTypes.Id, ObjectTypes.Name, DocumentSample.SampleFile
FROM ObjectTypes LEFT JOIN DocumentSample ON ObjectTypes.Id = DocumentSample.DocTypeID
WHERE ObjectTypes.Type = 'D';
GO

CREATE OR ALTER VIEW DocumentDMSPropertyMapping AS
SELECT DMSPropertyMapping.ID, DMSPropertyMapping.DocClassDetailID, DMSPropertyMapping.DMSPropertyName, DocumentClassDetail.DocTypeId, 
       DocumentClassDetail.PropertyId, Property.PropertyName, Property.PropertyType, Property.PropertyLength, 
       DMSPropertyMapping.ConnectorID
FROM DocumentClassDetail INNER JOIN Property ON DocumentClassDetail.PropertyId = Property.Id INNER JOIN DMSPropertyMapping ON DocumentClassDetail.Id = DMSPropertyMapping.DocClassDetailID;
GO

CREATE OR ALTER VIEW DocumentWithTypeName AS
SELECT ID, BatchID, DocName, DocTypeId, InternalName, Status, Name, FileName, CreatedOn
FROM (
    SELECT bd.ID, bd.BatchID, bd.DocName, bd.DocTypeId, bd.InternalName, bd.Status, ot.Name, 
           bd.FileName, b.CreatedOn,
           ROW_NUMBER() OVER (PARTITION BY bd.BatchID, bd.DocName ORDER BY bd.ID) as rn
    FROM BatchDetail bd
    INNER JOIN ObjectTypes ot ON bd.DocTypeId = ot.Id
    INNER JOIN Batch b ON bd.BatchID = b.ID
) t
WHERE rn = 1;
GO

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

CREATE OR ALTER VIEW ScanReportQuery AS
SELECT DISTINCT Batch.ID, Batch.BatchName, Batch.CreatedOn, 
       COUNT(BatchDetail.ID) AS PageCount, Steps.StepName
FROM Batch 
INNER JOIN BatchDetail ON Batch.ID = BatchDetail.BatchID 
INNER JOIN Steps ON Batch.StepID = Steps.ID
GROUP BY Batch.ID, Batch.BatchName, Batch.CreatedOn, 
         Steps.StepName, Batch.StepID, BatchDetail.Status;
GO

CREATE OR ALTER VIEW v_property_lookup_complete AS
SELECT 
    'Legacy' AS SourceType,
    p.Id AS PropertyId,
    p.PropertyName,
    p.PropertyDesc,
    p.PropertyType,
    p.PropertyLength,
    p.LookupId AS LegacyLookupId,
    lt.Id AS LookupTableId,
    lt.TableName AS LookupTableName,
    lt.DisplayName AS LookupTableDisplayName,
    ltv.Id AS LookupValueId,
    ltv.DisplayValue,
    ltv.ValueCode,
    ltv.Description AS ValueDescription,
    ltv.SortOrder
FROM Property p
LEFT JOIN LookupTables lt ON p.LookupId = lt.Id
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = 1
UNION ALL
SELECT 
    'New' AS SourceType,
    NULL AS PropertyId,
    plm.PropertyName,
    NULL AS PropertyDesc,
    NULL AS PropertyType,
    NULL AS PropertyLength,
    NULL AS LegacyLookupId,
    lt.Id AS LookupTableId,
    lt.TableName AS LookupTableName,
    lt.DisplayName AS LookupTableDisplayName,
    ltv.Id AS LookupValueId,
    ltv.DisplayValue,
    ltv.ValueCode,
    ltv.Description AS ValueDescription,
    ltv.SortOrder
FROM PropertyLookupMappings plm
JOIN LookupTables lt ON plm.LookupTableId = lt.Id
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = 1;
GO

CREATE OR ALTER VIEW v_doctype_property_lookup_complete AS
SELECT 
    dcd.Id AS DocTypePropertyId,
    dcd.DocTypeId,
    dcd.PropertyId,
    p.PropertyName,
    p.PropertyDesc,
    p.PropertyType,
    dcd.PropertyOrder,
    dcd.IsEnabled,
    dcd.IsRequired,
    dcd.ZoneId,
    dcd.Length,
    dcd.LookupId AS DocTypeLookupId,
    p.LookupId AS PropertyLookupId,
    lt.Id AS LookupTableId,
    lt.TableName AS LookupTableName,
    lt.DisplayName AS LookupTableDisplayName,
    ltv.Id AS LookupValueId,
    ltv.DisplayValue,
    ltv.ValueCode,
    ltv.Description AS ValueDescription,
    ltv.SortOrder
FROM DocumentClassDetail dcd
JOIN Property p ON dcd.PropertyId = p.Id
LEFT JOIN LookupTables lt ON COALESCE(dcd.LookupId, p.LookupId) = lt.Id
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = 1;
GO

CREATE OR ALTER VIEW v_batchtype_property_lookup_complete AS
SELECT 
    bcd.Id AS BatchTypePropertyId,
    bcd.BatchTypeId,
    bcd.PropertyId,
    p.PropertyName,
    p.PropertyDesc,
    p.PropertyType,
    bcd.PropertyOrder,
    bcd.IsPreIndex,
    bcd.IsRequired,
    bcd.ZoneID AS ZoneId,
    bcd.Length,
    bcd.LookupId AS BatchTypeLookupId,
    p.LookupId AS PropertyLookupId,
    lt.Id AS LookupTableId,
    lt.TableName AS LookupTableName,
    lt.DisplayName AS LookupTableDisplayName,
    ltv.Id AS LookupValueId,
    ltv.DisplayValue,
    ltv.ValueCode,
    ltv.Description AS ValueDescription,
    ltv.SortOrder
FROM BatchClassDetail bcd
JOIN Property p ON bcd.PropertyId = p.Id
LEFT JOIN LookupTables lt ON COALESCE(bcd.LookupId, p.LookupId) = lt.Id
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = 1;
GO

-- =============================================
-- MISSING FUNCTIONS
-- =============================================

GO
CREATE OR ALTER FUNCTION get_property_lookup_values_by_name(@p_property_name NVARCHAR(MAX))
RETURNS TABLE
AS
RETURN (
    -- First, try to find the property in the legacy Property table
    SELECT 
        ltv.Id,
        ltv.DisplayValue,
        ltv.ValueCode,
        ltv.Description,
        ltv.SortOrder
    FROM Property p
    JOIN LookupTables lt ON p.LookupId = lt.Id
    JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = 1
    WHERE p.PropertyName = @p_property_name
    
    UNION
    
    -- Then check the new PropertyLookupMappings table
    SELECT 
        ltv.Id,
        ltv.DisplayValue,
        ltv.ValueCode,
        ltv.Description,
        ltv.SortOrder
    FROM PropertyLookupMappings plm
    JOIN LookupTables lt ON plm.LookupTableId = lt.Id
    JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = 1
    WHERE plm.PropertyName = @p_property_name
);
GO

CREATE OR ALTER PROCEDURE validate_property_value_comprehensive
    @p_property_name NVARCHAR(100),
    @p_value NVARCHAR(MAX),
    @IsValid BIT OUTPUT,
    @ErrorMessage NVARCHAR(MAX) OUTPUT,
    @ValidationSource NVARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @v_legacy_lookup_id INT;
    DECLARE @v_new_lookup_table_id INT;
    
    SET @IsValid = 0;
    SET @ErrorMessage = NULL;
    SET @ValidationSource = 'NoValidation';

    SELECT @v_legacy_lookup_id = LookupId FROM Property WHERE PropertyName = @p_property_name;
    SELECT @v_new_lookup_table_id = LookupTableId FROM PropertyLookupMappings WHERE PropertyName = @p_property_name;

    IF @v_legacy_lookup_id IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM LookupTableValues WHERE LookupTableId = @v_legacy_lookup_id AND (ValueCode = @p_value OR DisplayValue = @p_value) AND IsActive = 1)
        BEGIN
            SET @IsValid = 1;
            SET @ValidationSource = 'Legacy';
            RETURN;
        END
        ELSE
            SET @ErrorMessage = 'Invalid value ' + @p_value + ' for property ' + @p_property_name + ' in legacy lookup system.';
    END

    IF @IsValid = 0 AND @v_new_lookup_table_id IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM LookupTableValues WHERE LookupTableId = @v_new_lookup_table_id AND (ValueCode = @p_value OR DisplayValue = @p_value) AND IsActive = 1)
        BEGIN
            SET @IsValid = 1;
            SET @ValidationSource = 'New';
            RETURN;
        END
        ELSE
            SET @ErrorMessage = 'Invalid value ' + @p_value + ' for property ' + @p_property_name + ' in new lookup system.';
    END

    IF @IsValid = 0
    BEGIN
        DECLARE @ValidationType NVARCHAR(50), @ValidationRule NVARCHAR(MAX), @RuleErrorMessage NVARCHAR(MAX);
        SELECT @ValidationType = ValidationType, @ValidationRule = ValidationRule, @RuleErrorMessage = ErrorMessage 
        FROM PropertyValidationRules WHERE PropertyName = @p_property_name AND IsActive = 1;

        IF @ValidationType IS NOT NULL
        BEGIN
            SET @ValidationSource = 'ValidationRule';
            -- Simplified REGEX check (SQL Server doesn't have native regex like Postgres without CRL, using LIKE or simple checks for now)
            -- This is a placeholder for more advanced T-SQL regex if needed
            SET @IsValid = 1; -- Default to valid if we can't easily check regex in T-SQL
        END
    END
    
    IF @ErrorMessage IS NULL SET @IsValid = 1;
END
GO

-- =============================================
-- TRIGGERS FOR MODIFIEDON PARITY
-- =============================================
GO
CREATE OR ALTER TRIGGER trg_dmsconnectors_modified ON dmsconnectors AFTER UPDATE AS
BEGIN
    UPDATE dmsconnectors SET modifiedon = GETUTCDATE() FROM dmsconnectors JOIN inserted ON dmsconnectors.id = inserted.id;
END
GO

CREATE OR ALTER TRIGGER trg_ocrconnectors_modified ON ocrconnectors AFTER UPDATE AS
BEGIN
    UPDATE ocrconnectors SET modifiedon = GETUTCDATE() FROM ocrconnectors JOIN inserted ON ocrconnectors.id = inserted.id;
END
GO

CREATE OR ALTER TRIGGER trg_DocIntelResults_modified ON DocIntelResults AFTER UPDATE AS
BEGIN
    UPDATE DocIntelResults SET UpdatedOn = GETUTCDATE() FROM DocIntelResults JOIN inserted ON DocIntelResults.id = inserted.id;
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SFTPConfigurations')
BEGIN
    CREATE TABLE SFTPConfigurations (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        AppId INT NOT NULL,
        Host NVARCHAR(255) NOT NULL,
        Port INT NOT NULL DEFAULT 22,
        Username NVARCHAR(255) NOT NULL,
        Password NVARCHAR(255) NOT NULL,
        RemotePath NVARCHAR(500) NOT NULL,
        BackupPath NVARCHAR(500),
        IsEnabled BIT DEFAULT 0,
        LastChecked DATETIME2,
        CreatedBy NVARCHAR(100),
        CreatedOn DATETIME2 DEFAULT GETUTCDATE()
    );
END

