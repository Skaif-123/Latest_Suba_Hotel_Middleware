namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors the `summary` object built at the end of the original
/// function and passed to basicIO.write(JSON.stringify(summary)),
/// plus the equivalent shape used in the catch-all error response.
/// </summary>
public sealed class SyncSummaryResult
{
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }

    public int ProcessedRows { get; set; }
    public int TotalInvoicesInserted { get; set; }
    public int TotalInvoicesUpdated { get; set; }
    public int TotalLineItemsInserted { get; set; }
    public int TotalLineItemsUpdated { get; set; }
    public bool ExecutionStoppedEarly { get; set; }
    public int ProcessedThirdPartyRows { get; set; }
    public int FailedThirdPartyRows { get; set; }

    // ---- Diagnostics (present only on success path, matching original) ----
    public int? InvoiceIdsFound { get; set; }
    public int? UniqueInvoiceIds { get; set; }
    public int? InvoiceMapCount { get; set; }
    public int? InvoiceInsertRows { get; set; }
    public int? InvoiceUpdateRows { get; set; }
    public int? LineItemInsertRows { get; set; }
    public int? LineItemUpdateRows { get; set; }
}
