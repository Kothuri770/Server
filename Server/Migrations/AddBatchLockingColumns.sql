-- Add locking columns to Batch table
ALTER TABLE Batch ADD COLUMN IF NOT EXISTS LockedOn TIMESTAMP WITHOUT TIME ZONE;
ALTER TABLE Batch ADD COLUMN IF NOT EXISTS LockedBy VARCHAR(255);

-- Add configuration for background services
INSERT INTO Configuration (ConfigName, ConfigValue) 
VALUES ('MaxParallelWorkers', '10')
ON CONFLICT (ConfigName) DO NOTHING;

INSERT INTO Configuration (ConfigName, ConfigValue) 
VALUES ('BatchLockTimeoutMinutes', '60')
ON CONFLICT (ConfigName) DO NOTHING;
