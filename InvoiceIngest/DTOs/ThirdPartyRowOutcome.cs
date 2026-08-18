namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors entries pushed into processedThirdPartyRows / failedThirdPartyRows:
///   { ROWID }                 (processed)
///   { ROWID, error }          (failed)
/// </summary>
public sealed class ThirdPartyRowOutcome
{
    public required string ROWID { get; init; }

    public string? Error { get; init; }
}
