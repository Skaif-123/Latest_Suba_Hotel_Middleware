-- Replaces Catalyst Cache-based offset persistence.
-- One row per module (ModuleName = 'AgentSync' for this job).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncOffset')
BEGIN
    CREATE TABLE SyncOffset (
        ModuleName    NVARCHAR(100)   NOT NULL PRIMARY KEY,
        CurrentOffset INT             NOT NULL DEFAULT 0,
        UpdatedTime   DATETIME2       NOT NULL DEFAULT GETUTCDATE()
    );
END
