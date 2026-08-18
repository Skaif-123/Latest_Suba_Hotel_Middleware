namespace AgentSyncConsole.InvoiceIngest.Enums;

/// <summary>
/// Represents ThirdPartyData.status values used by the original
/// JavaScript ('Processed' / 'Failed' / null). Kept as string
/// constants at the write boundary (Constants.SyncConstants) to
/// preserve exact DB literal casing; this enum is for in-memory
/// branching only where useful.
/// </summary>
public enum ThirdPartyStatus
{
    Unprocessed = 0,
    Processed = 1,
    Failed = 2
}
