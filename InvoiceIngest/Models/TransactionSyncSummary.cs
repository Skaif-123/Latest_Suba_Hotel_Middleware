namespace AgentSyncConsole.Models;

/// <summary>Execution summary — mirrors the "summary" object written via basicIO.write() in the original Catalyst source.</summary>
public class TransactionSyncSummary
{
    public string Status { get; set; } = "success";
    public string? Message { get; set; }

    public int ProcessedRows { get; set; }
    public int TotalTransactionInserted { get; set; }
    public int TotalTransactionUpdated { get; set; }
    public bool ExecutionStoppedEarly { get; set; }
    public int ProcessedThirdPartyRows { get; set; }
    public int FailedThirdPartyRows { get; set; }

    // Diagnostics — mirror the extra fields appended to the original summary object.
    public int TransactionIdsFound { get; set; }
    public int UniqueTransactionIds { get; set; }
    public int TransactionMapCount { get; set; }
    public int TransactionInsertRowsCount { get; set; }
    public int TransactionUpdateRowsCount { get; set; }
    public long ExecutionTimeMs { get; set; }
}
