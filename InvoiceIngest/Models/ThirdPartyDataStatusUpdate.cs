namespace AgentSyncConsole.Models;

/// <summary>
/// Lightweight write-back DTO for status/response updates against ThirdPartyData,
/// used by the batched and single-row status write-back paths in
/// TransactionSyncService. Kept separate from the full ThirdPartyData model
/// since only ROWID/status/response are ever written back for this flow.
/// </summary>
public class ThirdPartyDataStatusUpdate
{
    public long ROWID { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Response { get; set; }
}
