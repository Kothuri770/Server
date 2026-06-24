
-- =============================================
-- CORE BATCH MANAGEMENT TABLES
-- =============================================

-- Create Batch table if it doesn't exist
CREATE TABLE IF NOT EXISTS Batch (
    ID SERIAL NOT NULL,
    BatchName VARCHAR(60) NOT NULL,
    CreatedOn TIMESTAMP NOT NULL,
    BatchTypeId INTEGER NOT NULL,
    BatchStatus VARCHAR(1) NOT NULL,
    StepID INTEGER NOT NULL,
    InternalName VARCHAR(50) NOT NULL,
    UserName VARCHAR(30) NOT NULL,
    OcrType VARCHAR(30) NULL,
    LockedBy VARCHAR(100) NULL,
    LockedOn TIMESTAMP NULL,
    Ispurging BOOLEAN DEFAULT FALSE,
    CONSTRAINT PK_Batch PRIMARY KEY (ID)
);
CREATE INDEX IF NOT EXISTS idx_batch_stepid ON Batch(StepID);


-- Create BatchActions table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchActions (
    ID SERIAL NOT NULL,
    BatchID INTEGER NULL,
    ActionName VARCHAR(50) NULL,
    ActionStamp TIMESTAMP NULL,
    UserName VARCHAR(255) NULL,
    CONSTRAINT PK_BatchActions PRIMARY KEY (ID)
);
CREATE INDEX IF NOT EXISTS idx_batchactions_batchid ON BatchActions(BatchID);


-- Create BatchClassDetail table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchClassDetail (
    Id SERIAL NOT NULL,
    BatchTypeId INTEGER NOT NULL,
    PropertyId INTEGER NOT NULL,
    PropertyOrder INTEGER NOT NULL,
    IsPreIndex BOOLEAN NOT NULL,
    IsRequired BOOLEAN NOT NULL,
    ZoneID INTEGER NULL,
    Length INTEGER NOT NULL,
    LookupId INTEGER NULL,
    UIDesignId VARCHAR(50) NULL,
    CONSTRAINT PK_BatchClassDetail PRIMARY KEY (Id)
);


-- Create BatchDetail table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchDetail (
    ID SERIAL NOT NULL,
    BatchID INTEGER NOT NULL,
    PageNo INTEGER NOT NULL,
    FileName VARCHAR(255) NOT NULL,
    originalfilename VARCHAR(255) NOT NULL,
    Format VARCHAR(20) NOT NULL,
    DocPage INTEGER NULL,
    Status TEXT NOT NULL,
    pageName TEXT NULL,
    DocPageType VARCHAR(25) NULL,
    doctypeid INTEGER NULL,
    DocName VARCHAR(60) NULL,
    InternalName VARCHAR(40) NULL,
    DocCreatedOn TIMESTAMP NULL,
    CONSTRAINT PK_BatchDetail PRIMARY KEY (ID)
);
CREATE INDEX IF NOT EXISTS idx_batchdetail_batchid ON BatchDetail(BatchID);
CREATE INDEX IF NOT EXISTS idx_batchdetail_doctypeid ON BatchDetail(doctypeid);


-- Create BatchException table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchException (
    BatchID INTEGER NOT NULL,
    ErrorStep INTEGER NULL,
    Retries INTEGER NULL,
    CONSTRAINT PK_BatchException PRIMARY KEY (BatchID)
);


-- Create BatchLock table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchLock (
    BatchID INTEGER NOT NULL,
    SessionID VARCHAR(40) NOT NULL,
    LockedOn TIMESTAMP NOT NULL,
    CONSTRAINT PK_BatchLock PRIMARY KEY (BatchID)
);


-- Create BatchLog table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchLog (
    BatchId INTEGER NOT NULL,
    BatchType VARCHAR(255) NULL,
    DocumentCount INTEGER NULL,
    CompletedOn TIMESTAMP NULL,
    StepId INTEGER NULL,
    StationId INTEGER NULL,
    PageCount INTEGER NULL,
    BatchName VARCHAR(255) NULL
);
CREATE INDEX IF NOT EXISTS idx_batchlog_batchid ON BatchLog(BatchId);


-- Create CharacterZones table if it doesn't exist
CREATE TABLE IF NOT EXISTS CharacterZones (
    ID SERIAL NOT NULL,
    ZoneId INTEGER NOT NULL,
    CharacterOrder INTEGER NOT NULL,
    LeftX INTEGER NOT NULL,
    TopY INTEGER NOT NULL,
    RightX INTEGER NOT NULL,
    BottomY INTEGER NOT NULL,
    DataType VARCHAR(30) NULL,
    OCRType VARCHAR(1) NULL,
    CONSTRAINT PK_CharacterZones PRIMARY KEY (ID)
);


-- Create Configuration table if it doesn't exist
CREATE TABLE IF NOT EXISTS Configuration (
    ID SERIAL NOT NULL,
    ConfigName VARCHAR(30) NOT NULL,
    ConfigValue VARCHAR(255) NOT NULL,
    CONSTRAINT PK_Configuration PRIMARY KEY (ID)
);

-- Clean up duplicates and add unique constraint
DO $$
BEGIN
    -- Remove duplicates before adding unique constraint
    DELETE FROM Configuration 
    WHERE ID NOT IN (
        SELECT MIN(ID) 
        FROM Configuration 
        GROUP BY ConfigName
    );

    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'uk_configname' AND table_name = 'configuration') THEN
        ALTER TABLE Configuration ADD CONSTRAINT uk_configname UNIQUE (ConfigName);
    END IF;
END $$;

INSERT INTO configuration (configname,configvalue) VALUES ('BatchLockTimeoutMinutes','60')
ON CONFLICT (configname) DO NOTHING;

INSERT INTO configuration (configname,configvalue) VALUES ('MaxParallelWorkers', '10')
ON CONFLICT (configname) DO NOTHING;



-- Create Content table if it doesn't exist
CREATE TABLE IF NOT EXISTS Content (
    DocumentId SERIAL NOT NULL,
    BatchId BIGINT NOT NULL,
    ContentText TEXT NULL,
    Content BYTEA NOT NULL,
    FileExtension VARCHAR(20) NOT NULL,
    CONSTRAINT PK_Content PRIMARY KEY (DocumentId)
);


-- Create DMSClassMapping table if it doesn't exist
CREATE TABLE IF NOT EXISTS DMSClassMapping (
    ID SERIAL NOT NULL,
    DocTypeID INTEGER NOT NULL,
    DMSClassName VARCHAR(255) NOT NULL,
    DMSCabinetName VARCHAR(255) NOT NULL,
    ReleaseFormat VARCHAR(20) NOT NULL,
    NameExpression TEXT NOT NULL,
    ReleaseFolder TEXT NOT NULL,
    ConnectorID INTEGER NOT NULL,
    CONSTRAINT PK_DMSClassMapping PRIMARY KEY (ID)
);


-- Create DMSPropertyMapping table if it doesn't exist
CREATE TABLE IF NOT EXISTS DMSPropertyMapping (
    ID SERIAL NOT NULL,
    DocClassDetailID INTEGER NULL,
    DMSPropertyName VARCHAR(255) NULL,
    DocTypeID INTEGER NULL,
    ConnectorID INTEGER NOT NULL,
    CONSTRAINT PK_DMSPropertyMapping PRIMARY KEY (ID)
);


-- DocStatus table removed as it is merged into BatchDetail



-- Document table has been merged into BatchDetail


-- Create DocumentClassDetail table if it doesn't exist
CREATE TABLE IF NOT EXISTS DocumentClassDetail (
    Id SERIAL NOT NULL,
    DocTypeId INTEGER NOT NULL,
    PropertyId INTEGER NOT NULL,
    PropertyOrder INTEGER NOT NULL,
    IsEnabled BOOLEAN NOT NULL,
    IsRequired BOOLEAN NOT NULL,
    ZoneId INTEGER NULL,
    Length INTEGER NOT NULL,
    LookupId INTEGER NULL,
    IsBatchProperty BOOLEAN NOT NULL,
    UIDesignId VARCHAR(50) NULL,
    IsLookup BOOLEAN NULL,
    CONSTRAINT PK_DocumentClassDetail PRIMARY KEY (Id)
);


-- Create DocumentSample table if it doesn't exist
CREATE TABLE IF NOT EXISTS DocumentSample (
    DocTypeID INTEGER NOT NULL,
    SampleFile VARCHAR(255) NOT NULL,
    CONSTRAINT PK_DocumentSample PRIMARY KEY (DocTypeID)
);


-- Create Identification table if it doesn't exist
CREATE TABLE IF NOT EXISTS Identification (
    ID SERIAL NOT NULL,
    IDType VARCHAR(1) NOT NULL,
    IDMethod VARCHAR(15) NOT NULL,
    IDValue VARCHAR(30) NULL,
    ParentObjectId INTEGER NOT NULL,
    DiscardPage BOOLEAN NOT NULL,
    ZoneId INTEGER NULL,
    CONSTRAINT PK_Identification PRIMARY KEY (ID)
);


-- Create keyWordIdentification table if it doesn't exist
CREATE TABLE IF NOT EXISTS keyWordIdentification (
    id SERIAL NOT NULL,
    KeyWord TEXT NULL,
    Pagetype TEXT NULL,
    PRIMARY KEY (id)
);


-- Create Lookup table if it doesn't exist
CREATE TABLE IF NOT EXISTS Lookup (
    Id SERIAL NOT NULL,
    lookupStr TEXT NULL,
    lookupType VARCHAR(20) NULL,
    connectStr VARCHAR(255) NULL,
    CONSTRAINT PK_Lookup PRIMARY KEY (Id)
);


-- Create ObjectRelation table if it doesn't exist
CREATE TABLE IF NOT EXISTS ObjectRelation (
    Id SERIAL NOT NULL,
    ParentObjectId INTEGER NOT NULL,
    ChildObjectId INTEGER NOT NULL,
    CONSTRAINT PK_ObjectRelation PRIMARY KEY (Id)
);


-- Create ObjectTypes table if it doesn't exist
CREATE TABLE IF NOT EXISTS ObjectTypes (
    Id SERIAL NOT NULL,
    Type VARCHAR(1) NOT NULL,
    Name VARCHAR(50) NOT NULL UNIQUE,
    IsActive BOOLEAN NOT NULL,
    OcrConnectorId INTEGER NULL,
    OcrMode VARCHAR(30) DEFAULT 'Manual',
    CONSTRAINT PK_ObjectTypes PRIMARY KEY (Id)
);


-- Create OCRResults table if it doesn't exist
CREATE TABLE IF NOT EXISTS OCRResults (
    DocId INTEGER NOT NULL,
    ZoneId INTEGER NOT NULL,
    ColId INTEGER NULL,
    OCRValue VARCHAR(255) NOT NULL,
    OCRConfidence DOUBLE PRECISION NOT NULL
);


-- Create Property table if it doesn't exist
CREATE TABLE IF NOT EXISTS Property (
    Id SERIAL NOT NULL,
    PropertyName VARCHAR(50) NOT NULL,
    PropertyDesc VARCHAR(50) NOT NULL,
    PropertyType VARCHAR(50) NOT NULL,
    PropertyLength INTEGER NULL,
    LookupId INTEGER NULL,
    CONSTRAINT PK_Property PRIMARY KEY (Id)
);


-- Create ReleaseLog table if it doesn't exist
CREATE TABLE IF NOT EXISTS ReleaseLog (
    DocId INTEGER NOT NULL,
    CreatedDate TIMESTAMP NULL,
    BatchId INTEGER NULL,
    BatchName VARCHAR(255) NULL,
    DocName VARCHAR(255) NULL,
    DocType INTEGER NULL,
    Status VARCHAR(20) NOT NULL,
    ErrorMessage TEXT NULL,
    ReleasedDocName VARCHAR(255) NULL,
    ReleasedFormat VARCHAR(4) NULL,
    CONSTRAINT PK_ReleaseLog PRIMARY KEY (DocId)
);


-- Create ScannerProfiles table if it doesn't exist
CREATE TABLE IF NOT EXISTS ScannerProfiles (
    ProfileName VARCHAR(50) NOT NULL,
    Dpi VARCHAR(20) NOT NULL,
    ColorFormat VARCHAR(4) NOT NULL,
    ImageFormat VARCHAR(20) NOT NULL,
    PaperFormat VARCHAR(50) NOT NULL,
    UserName TEXT NOT NULL,
    CreatedOn TIMESTAMP NOT NULL
);


-- Create ScanProfiles table if it doesn't exist
CREATE TABLE IF NOT EXISTS ScanProfiles (
    ID SERIAL NOT NULL,
    Name VARCHAR(30) NULL,
    Scanner VARCHAR(50) NULL,
    Format VARCHAR(3) NULL,
    Color VARCHAR(20) NULL,
    Resolution INTEGER NULL,
    PaperSize VARCHAR(10) NULL,
    Brightness INTEGER NULL,
    Contrast INTEGER NULL,
    RemoveBP BOOLEAN NOT NULL,
    BPThreshold INTEGER NULL,
    UseADF BOOLEAN NOT NULL,
    ShowUI BOOLEAN NOT NULL,
    Status VARCHAR(1) NULL,
    CreatedOn TIMESTAMP NULL,
    Threshold INTEGER NOT NULL,
    DuplexScan BOOLEAN NOT NULL,
    DeSkew BOOLEAN NOT NULL,
    Station INTEGER NOT NULL,
    CONSTRAINT PK_ScanProfiles PRIMARY KEY (ID)
);


-- Create Sessions table if it doesn't exist
CREATE TABLE IF NOT EXISTS Sessions (
    ID SERIAL NOT NULL,
    StationId INTEGER NOT NULL,
    SessionId VARCHAR(40) NOT NULL,
    CONSTRAINT PK_Sessions PRIMARY KEY (ID)
);


-- Create Stations table if it doesn't exist
CREATE TABLE IF NOT EXISTS Stations (
    SerialNo VARCHAR(20) NOT NULL,
    StationId INTEGER NOT NULL,
    CONSTRAINT PK_Stations PRIMARY KEY (SerialNo)
);


-- Create Steps table if it doesn't exist
CREATE TABLE IF NOT EXISTS Steps (
    ID INTEGER NOT NULL,
    StepName VARCHAR(20) NOT NULL,
    Status VARCHAR(1) NOT NULL,
    StepOrder INTEGER NOT NULL,
    CONSTRAINT PK_Steps PRIMARY KEY (ID)
);

-- Create UsersCredentials table if it doesn't exist
CREATE TABLE IF NOT EXISTS UsersCredentials (
    UserName VARCHAR(30) NOT NULL,
    Password TEXT NOT NULL,
    UserType VARCHAR(50) NOT NULL,
    CreatedOn TIMESTAMP NOT NULL,
    ViewLimit VARCHAR(50) DEFAULT 'All',
    IsEnabled BOOLEAN DEFAULT TRUE,
    CONSTRAINT PK_UsersCredentials PRIMARY KEY (UserName)
);



-- Insert into UsersCredentials
INSERT INTO UsersCredentials (UserName, Password, UserType, CreatedOn) VALUES ('admin', 'kKHqGs0y8e11HK/jLJfRfa8gJRfY7uFxqpQXv/F4T1Q=', 'admin', '2024-03-16 14:56:43.430')
ON CONFLICT (UserName) DO NOTHING;



-- Create Zones table if it doesn't exist
CREATE TABLE IF NOT EXISTS Zones (
    ID SERIAL NOT NULL,
    Name VARCHAR(100) NOT NULL,
    LeftX INTEGER NULL,
    TopY INTEGER NULL,
    RightX INTEGER NULL,
    BottomY INTEGER NULL,
    DocTypeID INTEGER NOT NULL,
    PageNo INTEGER NOT NULL,
    Type VARCHAR(10) NOT NULL,
    StartPosition INTEGER NULL,
    Length INTEGER NULL,
    CONSTRAINT PK_Zones PRIMARY KEY (ID)
);

-- =============================================
-- DATA INSERTIONS
-- =============================================

-- =============================================
-- INITIAL DATA INSERTIONS
-- =============================================

-- Insert into Sessions with explicit ID (resets sequence automatically)
INSERT INTO Sessions (ID, StationId, SessionId) VALUES (18833, 5, '47e3b8c8-2042-45c8-9144-5d636351e01f')
ON CONFLICT (ID) DO NOTHING;

-- Insert into Stations
INSERT INTO Stations (SerialNo, StationId) VALUES ('', 7) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('43CE5BCA882EFE9', 6) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('5080F4C6BA8E2EC', 5) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('73016FC50548B80', 3) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('73016FC5054EBE0', 2) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('9A0F5EC2B76E5EB', 9) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('A7C656CA88F8889', 1) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('A7C656CA88FE8E9', 8) ON CONFLICT (SerialNo) DO NOTHING;
INSERT INTO Stations (SerialNo, StationId) VALUES ('DOCSEPERATE', 4) ON CONFLICT (SerialNo) DO NOTHING;

-- Insert into Steps
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (1, 'Scan', 'A', 1) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (2, 'Manual Separation', 'I', 2) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (3, 'Auto Separation', 'I', 3) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (4, 'OCR', 'A', 4) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (5, 'Index', 'A', 5) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (6, 'Index Verify', 'I', 6) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (7, 'Text Extraction', 'I', 7) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (8, 'Release', 'A', 8) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (98, 'Complete', 'A', 98) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (99, 'Exception', 'A', 99) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (100, 'FileNet Release', 'I', 100) ON CONFLICT (ID) DO NOTHING;
INSERT INTO Steps (ID, StepName, Status, StepOrder) VALUES (101, 'Upload', 'I', 101) ON CONFLICT (ID) DO NOTHING;


-- =============================================
-- VIEWS
-- =============================================

CREATE OR REPLACE VIEW AllBatchesWithTypeName AS
SELECT Batch.ID, Batch.BatchName, Batch.BatchTypeId, Batch.CreatedOn, Batch.BatchStatus, Batch.StepId, ObjectTypes.Name,Batch.username
FROM Batch LEFT JOIN ObjectTypes ON Batch.BatchTypeId = ObjectTypes.Id;


CREATE OR REPLACE VIEW BatchDetailWithDocType AS
SELECT BatchDetail.ID, BatchDetail.BatchID, BatchDetail.PageNo, BatchDetail.FileName, BatchDetail.Format, 
       BatchDetail.DocPage, BatchDetail.Status, BatchDetail.doctypeid AS DocTypeId, BatchDetail.DocName, BatchDetail.pageName,BatchDetail.originalfilename
FROM BatchDetail;


CREATE OR REPLACE VIEW BatchTypeProperties AS
SELECT BatchClassDetail.BatchTypeId, BatchClassDetail.PropertyId, BatchClassDetail.PropertyOrder, BatchClassDetail.IsPreIndex, BatchClassDetail.IsRequired, BatchClassDetail.ZoneID, Property.PropertyName, Property.PropertyDesc, Property.PropertyType, BatchClassDetail.Length, BatchClassDetail.LookupId, BatchClassDetail.Id
FROM BatchClassDetail INNER JOIN Property ON BatchClassDetail.PropertyId = Property.Id;


CREATE OR REPLACE VIEW BatchWithTypeName AS
SELECT Batch.ID, Batch.BatchName, Batch.BatchTypeId, ObjectTypes.Name, Batch.BatchStatus, Batch.StepId, Batch.CreatedOn
FROM Batch INNER JOIN ObjectTypes ON Batch.BatchTypeId = ObjectTypes.Id;


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


CREATE OR REPLACE VIEW DMSClassWithTypeName AS
SELECT DMSClassMapping.DocTypeID, DMSClassMapping.DMSClassName, DMSClassMapping.DMSCabinetName, ObjectTypes.Name, DMSClassMapping.ReleaseFormat, DMSClassMapping.NameExpression, DMSClassMapping.ReleaseFolder, DMSClassMapping.Id, DMSClassMapping.ConnectorID
FROM DMSClassMapping INNER JOIN ObjectTypes ON DMSClassMapping.DocTypeID = ObjectTypes.Id;


CREATE OR REPLACE VIEW DocTypeProperties AS
SELECT DocumentClassDetail.Id, DocumentClassDetail.DocTypeId, DocumentClassDetail.PropertyId, DocumentClassDetail.PropertyOrder, 
       DocumentClassDetail.IsEnabled, DocumentClassDetail.IsRequired, Property.PropertyName, Property.PropertyDesc, Property.PropertyType, 
       DocumentClassDetail.ZoneId, DocumentClassDetail.Length, DocumentClassDetail.LookupId, DocumentClassDetail.IsBatchProperty
FROM DocumentClassDetail INNER JOIN Property ON DocumentClassDetail.PropertyId = Property.Id;


CREATE OR REPLACE VIEW DocTypeSamples AS
SELECT ObjectTypes.Id, ObjectTypes.Name, DocumentSample.SampleFile
FROM ObjectTypes LEFT JOIN DocumentSample ON ObjectTypes.Id = DocumentSample.DocTypeID
WHERE ObjectTypes.Type = 'D';


CREATE OR REPLACE VIEW DocumentDMSPropertyMapping AS
SELECT DMSPropertyMapping.ID, DMSPropertyMapping.DocClassDetailID, DMSPropertyMapping.DMSPropertyName, DocumentClassDetail.DocTypeId, 
       DocumentClassDetail.PropertyId, Property.PropertyName, Property.PropertyType, Property.PropertyLength, 
       DMSPropertyMapping.ConnectorID
FROM DocumentClassDetail INNER JOIN Property ON DocumentClassDetail.PropertyId = Property.Id INNER JOIN DMSPropertyMapping ON DocumentClassDetail.Id = DMSPropertyMapping.DocClassDetailID;


CREATE OR REPLACE VIEW DocumentWithTypeName AS
SELECT DISTINCT ON (BatchDetail.BatchID, BatchDetail.DocName) 
       MIN(BatchDetail.ID) OVER (PARTITION BY BatchDetail.BatchID, BatchDetail.DocName) AS ID, 
       BatchDetail.BatchID, 
       BatchDetail.DocName, 
       BatchDetail.doctypeid AS DocTypeId, 
       BatchDetail.InternalName, 
       BatchDetail.Status, 
       ObjectTypes.Name, 
       BatchDetail.FileName, 
       Batch.CreatedOn
FROM ObjectTypes 
INNER JOIN BatchDetail ON ObjectTypes.Id = BatchDetail.doctypeid
INNER JOIN Batch ON BatchDetail.BatchID = Batch.ID
ORDER BY BatchDetail.BatchID, BatchDetail.DocName, BatchDetail.PageNo;


CREATE OR REPLACE VIEW public.jobmonitorreportquery
 AS
 SELECT batch.id,
    batch.batchname,
    batchlog.batchtype,
        CASE
            WHEN steps.stepname::text ~~ '% Separation%'::text THEN 'Classification'::character varying
            WHEN steps.stepname::text !~~ '% Separation%'::text THEN steps.stepname
            ELSE NULL::character varying
        END AS task,
        CASE
            WHEN batch.batchstatus::text = 'A'::text THEN 'Active'::text
            WHEN batch.batchstatus::text = 'I'::text THEN 'InActive'::text
            WHEN batch.batchstatus::text = 'P'::text THEN 'Running'::text
            WHEN batch.batchstatus::text = 'H'::text THEN 'Hold'::text
            ELSE NULL::text
        END AS batchstatus,
    batch.createdon,
    count(DISTINCT batchdetail.doctypeid) AS documentcount,
    count(DISTINCT batchdetail.pageno) AS pagecount,
    batchactions.username
   FROM batch
     JOIN steps ON batch.stepid = steps.id
     JOIN batchlog ON batch.id = batchlog.batchid
     JOIN batchactions ON batch.id = batchactions.batchid
     JOIN batchdetail ON batchactions.batchid = batchdetail.batchid
  WHERE batchactions.actionname::text = 'CREATE'::text AND batchdetail.status = 'A'::text
  GROUP BY batch.id, batch.batchname, batch.createdon, steps.stepname, batch.batchstatus, batch.stepid, batchlog.batchtype, batchactions.username;

ALTER TABLE public.jobmonitorreportquery
    OWNER TO postgres;

CREATE OR REPLACE VIEW ScanReportQuery AS
SELECT DISTINCT Batch.ID, Batch.BatchName, Batch.CreatedOn, 
       COUNT(BatchDetail.ID) AS PageCount, Steps.StepName
FROM Batch 
INNER JOIN BatchDetail ON Batch.ID = BatchDetail.BatchID 
INNER JOIN Steps ON Batch.StepID = Steps.ID
GROUP BY Batch.ID, Batch.BatchName, Batch.CreatedOn, 
         Steps.StepName, Batch.StepID, BatchDetail.Status;



-- =============================================
-- CONSTRAINTS
-- =============================================

-- Add unique constraint to objectrelation table if it doesn't exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'uk_objectrelation' AND table_name = 'objectrelation') THEN
        ALTER TABLE objectrelation ADD CONSTRAINT uk_objectrelation UNIQUE (parentobjectid, childobjectid);
    END IF;
END $$;



-- Add unique constraint to batchclassdetail table if it doesn't exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'uk_batchclassdetail' AND table_name = 'batchclassdetail') THEN
        ALTER TABLE batchclassdetail ADD CONSTRAINT uk_batchclassdetail UNIQUE (batchtypeid, propertyid);
    END IF;
END $$;



-- Add unique constraint to documentclassdetail table if it doesn't exist
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'uk_documentclassdetail' AND table_name = 'documentclassdetail') THEN
        ALTER TABLE documentclassdetail ADD CONSTRAINT uk_documentclassdetail UNIQUE (doctypeid, propertyid);
    END IF;
END $$;

-- =============================================
-- DMS PROVIDERS AND CONNECTORS
-- =============================================

-- Add DisplayedWidth and DisplayedHeight columns to Zones table
ALTER TABLE Zones ADD COLUMN IF NOT EXISTS DisplayedWidth INTEGER NULL;
ALTER TABLE Zones ADD COLUMN IF NOT EXISTS DisplayedHeight INTEGER NULL;


-- Create DMS Providers table if it doesn't exist
CREATE TABLE IF NOT EXISTS dmsproviders (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    displayname VARCHAR(100) NOT NULL,
    description TEXT,
    isactive BOOLEAN DEFAULT TRUE,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- Create DMS Output Formats table if it doesn't exist
CREATE TABLE IF NOT EXISTS dmsoutputformats (
    id SERIAL PRIMARY KEY,
    formatcode VARCHAR(20) NOT NULL UNIQUE,
    formatname VARCHAR(50) NOT NULL,
    description TEXT,
    isactive BOOLEAN DEFAULT TRUE,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- Create DMS Connectors table if it doesn't exist
CREATE TABLE IF NOT EXISTS dmsconnectors (
    id SERIAL PRIMARY KEY,
    doctypeid INTEGER NOT NULL,
    providerid INTEGER NOT NULL,         -- References dmsproviders table
    outputformatid INTEGER,              -- References dmsoutputformats table
    dmsclassname VARCHAR(255),         -- Name of the DMS class/type
    dmscabinetname VARCHAR(255),       -- Name of the cabinet/folder/bucket
    releasefolder VARCHAR(500),        -- Local folder path for LocalFolder connector
    nameexpression VARCHAR(500),       -- Naming expression for documents
    url VARCHAR(500),                  -- URL for DMS system
    username VARCHAR(255),             -- Username for authentication (for AWS Access Key, etc.)
    password VARCHAR(255),             -- Password for authentication (for AWS Secret Key, etc.)
    additionalconfig JSONB,            -- JSON field for connector-specific configuration
    isactive BOOLEAN DEFAULT TRUE,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (providerid) REFERENCES dmsproviders(id),
    FOREIGN KEY (outputformatid) REFERENCES dmsoutputformats(id)
);

-- =============================================
-- Property Mapping Table
-- =============================================
CREATE TABLE IF NOT EXISTS propertymapping (
    id SERIAL PRIMARY KEY,
    providername VARCHAR(255) NOT NULL,
    doctypeid INTEGER NOT NULL,
    jsonproperty JSONB,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT unique_mapping UNIQUE (providername, doctypeid)
);


-- =============================================
-- DMS INDEXES
-- =============================================

-- Indexes for performance

CREATE INDEX IF NOT EXISTS idx_dmsconnectors_providerid ON dmsconnectors(providerid);
CREATE INDEX IF NOT EXISTS idx_dmsconnectors_outputformatid ON dmsconnectors(outputformatid);
CREATE INDEX IF NOT EXISTS idx_dmsconnectors_active ON dmsconnectors(isactive) WHERE isactive = TRUE;
-- Unique constraint to ensure each document type maps to only one connector



-- Update trigger for modifiedon timestamp
CREATE OR REPLACE FUNCTION update_modified_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.modifiedon = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

CREATE TRIGGER update_dmsconnectors_modified BEFORE UPDATE ON dmsconnectors
    FOR EACH ROW EXECUTE FUNCTION update_modified_column();

-- Sample DMS Providers
INSERT INTO dmsproviders (name, displayname, description, isactive)
VALUES 
('AzureBlob', 'Azure Blob Storage', 'Microsoft Azure Blob Storage connector', TRUE),
('AwsS3', 'AWS S3', 'Amazon Web Services S3 connector', TRUE),
('Alfresco', 'Alfresco', 'Alfresco Document Management connector', TRUE),
('FileNet', 'FileNet', 'IBM FileNet connector', TRUE)
ON CONFLICT (name) DO NOTHING;

-- Sample DMS Output Formats
INSERT INTO dmsoutputformats (formatcode, formatname, description, isactive)
VALUES
('PDF', 'PDF', 'Portable Document Format', TRUE),
('TIF', 'TIFF (Single Page)', 'Tagged Image File Format - Each page as a separate file', TRUE),
('TIFF', 'TIFF (Multi-Page)', 'Tagged Image File Format - All pages merged into one file', TRUE),
('JPG', 'JPEG', 'Joint Photographic Experts Group', TRUE),
('PNG', 'PNG', 'Portable Network Graphics', TRUE),
('BMP', 'BMP', 'Bitmap Image File', TRUE),
('DOCX', 'DOCX', 'Microsoft Word Document', TRUE),
('XLSX', 'XLSX', 'Microsoft Excel Spreadsheet', TRUE)
ON CONFLICT (formatcode) DO NOTHING;


-- Foreign key constraint (assuming there's an ObjectTypes table)
-- ALTER TABLE dmsconnectors ADD CONSTRAINT fk_dmsconnectors_doctypeid 
--     FOREIGN KEY (doctypeid) REFERENCES ObjectTypes(id) ON DELETE CASCADE;


-- =============================================
-- OCR AND DOCUMENT INTELLIGENCE TABLES
-- =============================================

-- Create table for Azure Document Intelligence results
CREATE TABLE IF NOT EXISTS DocIntelResults (
    id SERIAL PRIMARY KEY,
    DocId INTEGER NOT NULL,
    AnalysisResult JSONB NOT NULL,  -- Store the full JSON analysis result
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Ensure each document has only one analysis result
    CONSTRAINT uk_DocIntelResults_DocId UNIQUE (DocId)
-- Foreign key constraint to link to the documents
    
);

-- Create indexes for better performance
CREATE INDEX idx_DocIntelResults_DocId ON DocIntelResults(DocId);
CREATE INDEX idx_DocIntelResults_CreatedOn ON DocIntelResults(CreatedOn);

-- Create a trigger to update the UpdatedOn timestamp
CREATE OR REPLACE FUNCTION update_DocIntelResults_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.UpdatedOn = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

CREATE TRIGGER update_DocIntelResults_updatedon 
    BEFORE UPDATE ON DocIntelResults 
    FOR EACH ROW 
    EXECUTE FUNCTION update_DocIntelResults_timestamp();



-- Create table for OCR providers if it doesn't exist
CREATE TABLE IF NOT EXISTS ocrproviders (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    displayname VARCHAR(100) NOT NULL,
    description TEXT,
    isactive BOOLEAN DEFAULT TRUE,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP
); 
 
-- Create table for OCR connector configurations if it doesn't exist
CREATE TABLE IF NOT EXISTS ocrconnectors (
    id SERIAL PRIMARY KEY,
    providerid INTEGER NOT NULL,           -- References ocrproviders table
    name VARCHAR(100) NOT NULL,            -- Name of the OCR connector
    isdefault BOOLEAN DEFAULT FALSE,       -- Whether this is the default OCR connector
    configdata JSONB,                      -- JSON field for connector-specific configuration
    isactive BOOLEAN DEFAULT TRUE,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (providerid) REFERENCES ocrproviders(id)
);
 
-- Create indexes for performance
CREATE INDEX idx_ocrconnectors_providerid ON ocrconnectors(providerid);
CREATE INDEX idx_ocrconnectors_isactive ON ocrconnectors(isactive);
CREATE INDEX idx_ocrconnectors_isdefault ON ocrconnectors(isdefault);
 
-- Create a trigger to update the modifiedon timestamp
CREATE OR REPLACE FUNCTION update_modified_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.modifiedon = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';
 
CREATE TRIGGER update_ocrconnectors_modified 
    BEFORE UPDATE ON ocrconnectors 
    FOR EACH ROW 
    EXECUTE FUNCTION update_modified_column();
 
-- Insert sample OCR providers
INSERT INTO ocrproviders (name, displayname, description, isactive)
VALUES 
('Tesseract', 'Tesseract OCR', 'Open-source Tesseract OCR engine', TRUE),
('AzureDocIntel', 'Azure Document Intelligence', 'Microsoft Azure Document Intelligence service', TRUE),
('GoogleDocAI', 'Google Document AI', 'Google Document AI service', TRUE),
('AmazonTextract', 'Amazon Textract', 'Amazon Textract OCR service', TRUE),
('ollama', 'Ollama (Gemma)', 'Local Ollama LLM extraction', TRUE)
ON CONFLICT (name) DO NOTHING;
 
-- Insert sample OCR connector configurations
INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
SELECT 1, 'Tesseract Local', TRUE, '{"tessdatapath": "./Tessract", "language": "eng"}', TRUE
WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Tesseract Local');

INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
SELECT 2, 'Azure Doc Intel', FALSE, '{"endpoint": "", "apikey": "", "modelid": "prebuilt-read"}', TRUE
WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Azure Doc Intel');

INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
SELECT 3, 'Google Doc AI', FALSE, '{"endpoint": "", "apikey": "", "processorid": ""}', TRUE
WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Google Doc AI');

INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
SELECT 4, 'Amazon Textract', FALSE, '{"region": "us-east-1", "accesskey": "", "secretkey": ""}', TRUE
WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Amazon Textract');

INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive)
SELECT 5, 'Ollama Local', FALSE, '{"endpoint": "http://localhost:11434/api/generate", "modelid": "gemma4:e4b"}', TRUE
WHERE NOT EXISTS (SELECT 1 FROM ocrconnectors WHERE name = 'Ollama Local');
 
-- Create table for OCR configuration settings
CREATE TABLE IF NOT EXISTS ocrconfiguration (
    id SERIAL PRIMARY KEY,
    configname VARCHAR(50) NOT NULL UNIQUE,
    configvalue TEXT,
    description TEXT,
    createdon TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    modifiedon TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
 
-- Insert OCR configuration settings
INSERT INTO ocrconfiguration (configname, configvalue, description)
VALUES 
('OcrMode', 'Automatic', 'OCR processing mode: Manual or Automatic'),
('DefaultOcrConnectorId', '1', 'ID of the default OCR connector to use'),
('OcrTimeout', '30', 'Timeout in seconds for OCR processing'),
('OcrRetryAttempts', '3', 'Number of retry attempts for OCR processing')
ON CONFLICT (configname) DO NOTHING;



-- =============================================
-- BATCH LOCKS AND USERS
-- =============================================

-- Create BatchLocks table to manage concurrent batch access if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchLocks (

    Id SERIAL PRIMARY KEY,

    BatchId INTEGER NOT NULL,

    UserId VARCHAR(30) NOT NULL,  -- Using VARCHAR(30) to match UserName length in UsersCredentials

    UserName VARCHAR(256) NOT NULL,

    LockAcquired TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    SessionId VARCHAR(100),

    ExpirationTime TIMESTAMP WITH TIME ZONE NOT NULL,

    Status VARCHAR(50) NOT NULL DEFAULT 'Active',

    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (BatchId) REFERENCES Batch(ID),

    FOREIGN KEY (UserId) REFERENCES UsersCredentials(UserName)

);
 
-- Create indexes for performance

CREATE INDEX idx_batchlocks_batchid ON BatchLocks(BatchId);

CREATE INDEX idx_batchlocks_expiration_time ON BatchLocks(ExpirationTime);

CREATE INDEX idx_batchlocks_status ON BatchLocks(Status);
 
-- Function to acquire a lock

CREATE OR REPLACE FUNCTION AcquireBatchLock(

    p_BatchId INTEGER,

    p_UserId VARCHAR,

    p_UserName VARCHAR,

    p_SessionId VARCHAR,

    p_LockTimeoutMinutes INTEGER DEFAULT 30

) RETURNS TABLE (

    Result VARCHAR,

    resultExpirationTime TIMESTAMP WITH TIME ZONE,

    CurrentLockHolder VARCHAR,

    LockExpiration TIMESTAMP WITH TIME ZONE

) AS $$

DECLARE

    v_ExpirationTime TIMESTAMP WITH TIME ZONE;

    v_CurrentLockHolder VARCHAR;

    v_LockExpiration TIMESTAMP WITH TIME ZONE;

BEGIN

    v_ExpirationTime := NOW() + (p_LockTimeoutMinutes * INTERVAL '1 minute');
 
    -- First, try to acquire the lock if no active lock exists

    IF NOT EXISTS (SELECT 1 FROM BatchLocks WHERE BatchId = p_BatchId AND Status = 'Active' AND ExpirationTime > NOW()) THEN

        INSERT INTO BatchLocks (BatchId, UserId, UserName, SessionId, ExpirationTime, Status)

        VALUES (p_BatchId, p_UserId, p_UserName, p_SessionId, v_ExpirationTime, 'Active');

        RETURN QUERY SELECT 'ACQUIRED'::VARCHAR, v_ExpirationTime, NULL::VARCHAR, NULL::TIMESTAMP WITH TIME ZONE;

    ELSE

        -- Check if the lock is held by the same user

        IF EXISTS (SELECT 1 FROM BatchLocks WHERE BatchId = p_BatchId AND UserId = p_UserId AND Status = 'Active' AND ExpirationTime > NOW()) THEN

            -- Renew the existing lock

            UPDATE BatchLocks 

            SET ExpirationTime = v_ExpirationTime, 

                SessionId = COALESCE(p_SessionId, SessionId),

                UpdatedAt = NOW()

            WHERE BatchId = p_BatchId AND UserId = p_UserId AND Status = 'Active';

            RETURN QUERY SELECT 'RENEWED'::VARCHAR, v_ExpirationTime, NULL::VARCHAR, NULL::TIMESTAMP WITH TIME ZONE;

        ELSE

            -- Get information about the current lock holder

            SELECT UserName, ExpirationTime INTO v_CurrentLockHolder, v_LockExpiration

            FROM BatchLocks 

            WHERE BatchId = p_BatchId AND Status = 'Active' AND ExpirationTime > NOW();

            RETURN QUERY SELECT 'LOCKED'::VARCHAR, NULL::TIMESTAMP WITH TIME ZONE, v_CurrentLockHolder, v_LockExpiration;

        END IF;

    END IF;

END;

$$ LANGUAGE plpgsql;
 
-- Function to release a lock

CREATE OR REPLACE FUNCTION ReleaseBatchLock(

    p_BatchId INTEGER,

    p_UserId VARCHAR

) RETURNS INTEGER AS $$

DECLARE

    v_RowCount INTEGER;

BEGIN

    UPDATE BatchLocks 

    SET Status = 'Released',

        UpdatedAt = NOW()

    WHERE BatchId = p_BatchId AND UserId = p_UserId AND Status = 'Active';

    GET DIAGNOSTICS v_RowCount = ROW_COUNT;

    RETURN v_RowCount;

END;

$$ LANGUAGE plpgsql;
 
-- Function to refresh a lock

CREATE OR REPLACE FUNCTION RefreshBatchLock(

    p_BatchId INTEGER,

    p_UserId VARCHAR,

    p_LockTimeoutMinutes INTEGER DEFAULT 30

) RETURNS TABLE (

    Result VARCHAR,

    NewExpirationTime TIMESTAMP WITH TIME ZONE

) AS $$

DECLARE

    v_ExpirationTime TIMESTAMP WITH TIME ZONE;

    v_RowCount INTEGER;

BEGIN

    v_ExpirationTime := NOW() + (p_LockTimeoutMinutes * INTERVAL '1 minute');

    UPDATE BatchLocks 

    SET ExpirationTime = v_ExpirationTime,

        UpdatedAt = NOW()

    WHERE BatchId = p_BatchId AND UserId = p_UserId AND Status = 'Active' AND ExpirationTime > NOW();

    GET DIAGNOSTICS v_RowCount = ROW_COUNT;

    IF v_RowCount > 0 THEN

        RETURN QUERY SELECT 'REFRESHED'::VARCHAR, v_ExpirationTime;

    ELSE

        RETURN QUERY SELECT 'NOT_FOUND'::VARCHAR, NULL::TIMESTAMP WITH TIME ZONE;

    END IF;

END;

$$ LANGUAGE plpgsql;
 
-- Function to clean expired locks

CREATE OR REPLACE FUNCTION CleanExpiredLocks()

RETURNS VOID AS $$

BEGIN

    UPDATE BatchLocks 

    SET Status = 'Expired'

    WHERE Status = 'Active' AND ExpirationTime <= NOW();

    DELETE FROM BatchLocks 

    WHERE Status IN ('Expired', 'Released') AND UpdatedAt < (NOW() - INTERVAL '1 hour');

END;

$$ LANGUAGE plpgsql;
 
-- Optional: Create a scheduled job to run CleanExpiredLocks periodically

-- This assumes you have pg_cron extension installed

-- CREATE EXTENSION IF NOT EXISTS pg_cron;

-- SELECT cron.schedule('clean-expired-batch-locks', '*/5 * * * *', $$SELECT CleanExpiredLocks();$$);








-- Create User Roles table if it doesn't exist
CREATE TABLE IF NOT EXISTS UserRoles (
    RoleId SERIAL PRIMARY KEY,
    RoleName VARCHAR(50) UNIQUE NOT NULL,
    RoleDescription TEXT,
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- Create Permissions table if it doesn't exist
CREATE TABLE IF NOT EXISTS Permissions (
    PermissionId SERIAL PRIMARY KEY,
    PermissionName VARCHAR(100) UNIQUE NOT NULL,
    PermissionDescription TEXT,
    Category VARCHAR(50),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- Create UserRoleAssignments table if it doesn't exist
CREATE TABLE IF NOT EXISTS UserRoleAssignments (
    AssignmentId SERIAL PRIMARY KEY,
    UserName VARCHAR(30) REFERENCES UsersCredentials(UserName) ON DELETE CASCADE,
    RoleId INTEGER REFERENCES UserRoles(RoleId) ON DELETE CASCADE,
    AssignedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(UserName, RoleId)
);


-- Create RolePermissionAssignments table if it doesn't exist
CREATE TABLE IF NOT EXISTS RolePermissionAssignments (
    AssignmentId SERIAL PRIMARY KEY,
    RoleId INTEGER REFERENCES UserRoles(RoleId) ON DELETE CASCADE,
    PermissionId INTEGER REFERENCES Permissions(PermissionId) ON DELETE CASCADE,
    AssignedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(RoleId, PermissionId)
);

-- Insert Default Roles
INSERT INTO UserRoles (RoleName, RoleDescription) VALUES 
('User', 'User with limited access'),
('Admin', 'Administrator with full access'),
('Scanner', 'User can scan documents'),
('Verifier', 'User can verify documents'),
('Monitor', 'User can monitor batch status'),
('ConfigEditor', 'User can edit configurations'),
('Scanverify', 'scan and verify documents')
ON CONFLICT (RoleName) DO NOTHING;







-- =============================================
-- LOOKUP AND VALIDATION TABLES
-- =============================================

-- Fixed Property-Lookup Integration Script for PostgreSQL
-- Addresses ON CONFLICT constraint issues

-- Create or update the lookup tables with proper constraints
CREATE TABLE IF NOT EXISTS LookupTables (
    Id SERIAL PRIMARY KEY,
    TableName VARCHAR(100) UNIQUE NOT NULL,
    DisplayName VARCHAR(200) NOT NULL,
    Description TEXT,
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedBy VARCHAR(100),
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- Create LookupTableValues table if it doesn't exist
CREATE TABLE IF NOT EXISTS LookupTableValues (
    Id SERIAL PRIMARY KEY,
    LookupTableId INTEGER NOT NULL,
    DisplayValue VARCHAR(200) NOT NULL,
    ValueCode VARCHAR(100) NOT NULL,
    Description TEXT,
    SortOrder INTEGER DEFAULT 0,
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedBy VARCHAR(100),
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uk_lookuptablevalues_tableid_valuecode UNIQUE(LookupTableId, ValueCode),
    CONSTRAINT fk_lookuptablevalues_lookuptableid_lookuptables_id 
        FOREIGN KEY (LookupTableId) REFERENCES LookupTables(Id) ON DELETE CASCADE
);


-- Create PropertyValidationRules table if it doesn't exist
CREATE TABLE IF NOT EXISTS PropertyValidationRules (
    Id SERIAL PRIMARY KEY,
    PropertyName VARCHAR(100) UNIQUE,
    DisplayName VARCHAR(200) NOT NULL,
    ValidationType VARCHAR(50) NOT NULL,
    ValidationRule TEXT,
    ErrorMessage TEXT,
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedBy VARCHAR(100),
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- Create PropertyLookupMappings table if it doesn't exist
CREATE TABLE IF NOT EXISTS PropertyLookupMappings (
    Id SERIAL PRIMARY KEY,
    PropertyName VARCHAR(100) UNIQUE,
    LookupTableId INTEGER,
    IsRequired BOOLEAN DEFAULT FALSE,
    DefaultValue VARCHAR(200),
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedBy VARCHAR(100),
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_propertylookupmappings_lookuptableid_lookuptables_id 
        FOREIGN KEY (LookupTableId) REFERENCES LookupTables(Id)
);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_lookup_table_values_table_id ON LookupTableValues(LookupTableId);
CREATE INDEX IF NOT EXISTS idx_lookup_table_values_code ON LookupTableValues(ValueCode);
CREATE INDEX IF NOT EXISTS idx_lookup_tables_name ON LookupTables(TableName);
CREATE INDEX IF NOT EXISTS idx_property_validation_rules_property ON PropertyValidationRules(PropertyName);
CREATE INDEX IF NOT EXISTS idx_property_lookup_mappings_property ON PropertyLookupMappings(PropertyName);
CREATE INDEX IF NOT EXISTS idx_property_lookup_mappings_table_id ON PropertyLookupMappings(LookupTableId);


-- Function to get lookup values by property name
CREATE OR REPLACE FUNCTION get_property_lookup_values_by_name(p_property_name VARCHAR)
RETURNS TABLE(
    Id INTEGER,
    DisplayValue VARCHAR,
    ValueCode VARCHAR,
    Description TEXT,
    SortOrder INTEGER
) AS $func$
BEGIN
    RETURN QUERY
    -- First, try to find the property in the legacy Property table
    SELECT 
        ltv.Id,
        ltv.DisplayValue,
        ltv.ValueCode,
        ltv.Description,
        ltv.SortOrder
    FROM Property p
    JOIN LookupTables lt ON p.LookupId = lt.Id
    JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = TRUE
    WHERE p.PropertyName = p_property_name
    
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
    JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = TRUE
    WHERE plm.PropertyName = p_property_name
    
    ORDER BY SortOrder, DisplayValue;
END;
$func$ LANGUAGE plpgsql;

-- Create a comprehensive view that connects the new lookup system with the existing property system
CREATE OR REPLACE VIEW v_property_lookup_complete AS
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
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = TRUE

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
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = TRUE

ORDER BY PropertyName, SourceType, SortOrder, DisplayValue;

-- Create a view that shows document type properties with their lookup mappings
CREATE OR REPLACE VIEW v_doctype_property_lookup_complete AS
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
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = TRUE
ORDER BY dcd.DocTypeId, dcd.PropertyOrder, p.PropertyName, ltv.SortOrder;

-- Create a view that shows batch type properties with their lookup mappings
CREATE OR REPLACE VIEW v_batchtype_property_lookup_complete AS
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
LEFT JOIN LookupTableValues ltv ON lt.Id = ltv.LookupTableId AND ltv.IsActive = TRUE
ORDER BY bcd.BatchTypeId, bcd.PropertyOrder, p.PropertyName, ltv.SortOrder;

-- Function to validate property values
CREATE OR REPLACE FUNCTION validate_property_value_comprehensive(p_property_name VARCHAR, p_value VARCHAR)
RETURNS TABLE(
    IsValid BOOLEAN,
    ErrorMessage TEXT,
    ValidationSource VARCHAR
) AS $func$
DECLARE
    v_legacy_lookup_id INTEGER;
    v_new_lookup_table_id INTEGER;
    v_validation_rule RECORD;
    v_is_valid BOOLEAN := FALSE;
    v_error_message TEXT := NULL;
    v_validation_source VARCHAR := 'NoValidation';
BEGIN
    -- Check if property exists in the legacy system (Property table)
    SELECT LookupId
    INTO v_legacy_lookup_id
    FROM Property
    WHERE PropertyName = p_property_name
    LIMIT 1;
    
    -- Check if property exists in the new system (PropertyLookupMappings table)
    SELECT plm.LookupTableId
    INTO v_new_lookup_table_id
    FROM PropertyLookupMappings plm
    WHERE plm.PropertyName = p_property_name
    LIMIT 1;
    
    -- If found in legacy system, check against legacy lookup
    IF v_legacy_lookup_id IS NOT NULL THEN
        SELECT COUNT(*) > 0
        INTO v_is_valid
        FROM LookupTableValues ltv
        WHERE ltv.LookupTableId = v_legacy_lookup_id
          AND (ltv.ValueCode = p_value OR ltv.DisplayValue = p_value)
          AND ltv.IsActive = TRUE;
        
        IF v_is_valid THEN
            v_validation_source := 'Legacy';
            RETURN QUERY SELECT v_is_valid AS IsValid, NULL::TEXT AS ErrorMessage, v_validation_source AS ValidationSource;
            RETURN;
        ELSE
            v_error_message := format('Invalid value %s for property %s in legacy lookup system.', p_value, p_property_name);
        END IF;
    END IF;
    
    -- If not validated by legacy system, check new system
    IF NOT v_is_valid AND v_new_lookup_table_id IS NOT NULL THEN
        SELECT COUNT(*) > 0
        INTO v_is_valid
        FROM LookupTableValues ltv
        WHERE ltv.LookupTableId = v_new_lookup_table_id
          AND (ltv.ValueCode = p_value OR ltv.DisplayValue = p_value)
          AND ltv.IsActive = TRUE;
        
        IF v_is_valid THEN
            v_validation_source := 'New';
            RETURN QUERY SELECT v_is_valid AS IsValid, NULL::TEXT AS ErrorMessage, v_validation_source AS ValidationSource;
            RETURN;
        ELSE
            v_error_message := format('Invalid value %s for property %s in new lookup system.', p_value, p_property_name);
        END IF;
    END IF;
    
    -- If not validated by lookup systems, check against validation rules
    IF NOT v_is_valid THEN
        SELECT * INTO v_validation_rule
        FROM PropertyValidationRules
        WHERE PropertyName = p_property_name AND IsActive = TRUE
        LIMIT 1;
        
        IF v_validation_rule IS NOT NULL THEN
            CASE UPPER(v_validation_rule.ValidationType)
                WHEN 'REGEX' THEN
                    IF v_validation_rule.ValidationRule IS NOT NULL AND p_value ~ v_validation_rule.ValidationRule THEN
                        RETURN QUERY SELECT TRUE::BOOLEAN AS IsValid, NULL::TEXT AS ErrorMessage, 'ValidationRule' AS ValidationSource;
                        RETURN;
                    ELSE
                        RETURN QUERY SELECT FALSE::BOOLEAN AS IsValid, COALESCE(v_validation_rule.ErrorMessage, 'Value does not match the required format.') AS ErrorMessage, 'ValidationRule' AS ValidationSource;
                        RETURN;
                    END IF;
                
                WHEN 'RANGE' THEN
                    IF v_validation_rule.ValidationRule IS NOT NULL THEN
                        DECLARE
                            range_parts TEXT[];
                            min_val NUMERIC;
                            max_val NUMERIC;
                            actual_val NUMERIC;
                        BEGIN
                            range_parts := STRING_TO_ARRAY(v_validation_rule.ValidationRule, ',');
                            IF ARRAY_LENGTH(range_parts, 1) >= 2 THEN
                                min_val := CAST(TRIM(range_parts[1]) AS NUMERIC);
                                max_val := CAST(TRIM(range_parts[2]) AS NUMERIC);
                                
                                BEGIN
                                    actual_val := CAST(p_value AS NUMERIC);
                                    
                                    IF actual_val >= min_val AND actual_val <= max_val THEN
                                        RETURN QUERY SELECT TRUE::BOOLEAN AS IsValid, NULL::TEXT AS ErrorMessage, 'ValidationRule' AS ValidationSource;
                                        RETURN;
                                    ELSE
                                        RETURN QUERY SELECT FALSE::BOOLEAN AS IsValid, COALESCE(v_validation_rule.ErrorMessage, format('Value must be between %s and %s.', min_val, max_val)) AS ErrorMessage, 'ValidationRule' AS ValidationSource;
                                        RETURN;
                                    END IF;
                                EXCEPTION
                                    WHEN OTHERS THEN
                                        RETURN QUERY SELECT FALSE::BOOLEAN AS IsValid, COALESCE(v_validation_rule.ErrorMessage, 'Value must be a valid number for range validation.') AS ErrorMessage, 'ValidationRule' AS ValidationSource;
                                        RETURN;
                                END;
                            END IF;
                        END;
                    END IF;
            END CASE;
        END IF;
    END IF;
    
    IF v_error_message IS NOT NULL THEN
        RETURN QUERY SELECT v_is_valid AS IsValid, v_error_message AS ErrorMessage, v_validation_source AS ValidationSource;
    ELSE
        RETURN QUERY SELECT TRUE::BOOLEAN AS IsValid, NULL::TEXT AS ErrorMessage, 'NoValidation' AS ValidationSource;
    END IF;
END;
$func$ LANGUAGE plpgsql;



-- =============================================
-- FINAL TABLES
-- =============================================

-- Create BatchTaskTime table if it doesn't exist
CREATE TABLE IF NOT EXISTS BatchTaskTime (
    Id SERIAL PRIMARY KEY,
    BatchId INTEGER NOT NULL,
    TaskId INTEGER NOT NULL,
    TaskStartTime TIMESTAMP NOT NULL,
    Status VARCHAR(20) NOT NULL
);
 
 CREATE TABLE IF NOT EXISTS EmailConfigurations (
    Id SERIAL PRIMARY KEY,
    EmailId VARCHAR(255) NOT NULL,
    Password VARCHAR(255) NOT NULL,
    AppId INT NOT NULL,
    DocumentType VARCHAR(100) NOT NULL,
    IsEnabled BOOLEAN DEFAULT FALSE,
    ImapServer VARCHAR(255) DEFAULT 'imap.gmail.com',
    ImapPort INT DEFAULT 993,
    LastChecked TIMESTAMP,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS LocalFolderConfigurations (
    Id SERIAL PRIMARY KEY,
    AppId INT NOT NULL,
    PickImagesPath VARCHAR(500) NOT NULL,
    BackupPath VARCHAR(500) NOT NULL,
    IsEnabled BOOLEAN DEFAULT FALSE,
    LastChecked TIMESTAMP,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- =============================================
-- DATABASE LOOKUP TABLES
-- =============================================

-- Create DatabaseConnections table if it doesn't exist
CREATE TABLE IF NOT EXISTS DatabaseConnections (
    Id SERIAL PRIMARY KEY,
    ConnectionName VARCHAR(100) NOT NULL UNIQUE,
    DbType INTEGER NOT NULL, -- 0: SqlServer, 1: MySql, 2: Oracle
    ConnectionString TEXT NOT NULL,
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedBy VARCHAR(100),
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create DatabaseLookupMappings table if it doesn't exist
CREATE TABLE IF NOT EXISTS DatabaseLookupMappings (
    Id SERIAL PRIMARY KEY,
    PropertyName VARCHAR(100) NOT NULL UNIQUE,
    ConnectionId INTEGER NOT NULL,
    SqlQuery TEXT NOT NULL,
    ColumnMappings JSONB, -- Stores Column Mapping DTOs as JSON
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedBy VARCHAR(100),
    UpdatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_db_lookup_mappings_connection FOREIGN KEY (ConnectionId) REFERENCES DatabaseConnections(Id) ON DELETE CASCADE
);

-- Create indexes for performance
CREATE INDEX IF NOT EXISTS idx_db_connections_name ON DatabaseConnections(ConnectionName);
CREATE INDEX IF NOT EXISTS idx_db_lookup_mappings_property ON DatabaseLookupMappings(PropertyName);
CREATE INDEX IF NOT EXISTS idx_db_lookup_mappings_connection ON DatabaseLookupMappings(ConnectionId);

CREATE TABLE IF NOT EXISTS SFTPConfigurations (
    Id SERIAL PRIMARY KEY,
    AppId INT NOT NULL,
    Host VARCHAR(255) NOT NULL,
    Port INT NOT NULL DEFAULT 22,
    Username VARCHAR(255) NOT NULL,
    Password VARCHAR(255) NOT NULL,
    RemotePath VARCHAR(500) NOT NULL,
    BackupPath VARCHAR(500),
    IsEnabled BOOLEAN DEFAULT FALSE,
    LastChecked TIMESTAMP,
    CreatedBy VARCHAR(100),
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

