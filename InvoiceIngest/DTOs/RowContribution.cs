namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors one entry in rowContributions:
///   ROWID -> { invoiceIds: Set, lineItemKeys: Set }
/// Populated by trackContribution() and read during status
/// resolution to trace a write failure back to its ThirdPartyData
/// source row.
/// </summary>
public sealed class RowContribution
{
    public HashSet<string> InvoiceIds { get; } = new();

    public HashSet<string> LineItemKeys { get; } = new();
}
