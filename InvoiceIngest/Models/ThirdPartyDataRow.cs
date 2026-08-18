namespace AgentSyncConsole.InvoiceIngest.Models;

/// <summary>
/// Maps to SQL Server "ThirdPartyData" table (was Catalyst
/// datastore table 'ThirdPartyData'). Matches columns selected by:
///   SELECT ROWID, invoice, status, response FROM ThirdPartyData
///   WHERE invoice IS NOT NULL
///   AND (status IS NULL OR status != 'Processed')
///   ORDER BY ROWID ASC
/// and columns written back in the status-resolution step.
/// </summary>
public sealed class ThirdPartyDataRow
{
    public string ROWID { get; set; }
    public string? Invoice { get; set; }
    public string? Status { get; set; }
    public string? Response { get; set; }
}
