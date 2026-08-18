namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors runBatches() return value: { confirmed, failed }.
/// Generic over the row type since runBatches() is used for
/// Invoice, InvoiceLineItem, and ThirdPartyData rows alike.
/// </summary>
/// <typeparam name="TRow">Row type being batched.</typeparam>
public sealed class BatchResult<TRow>
{
    public int Confirmed { get; set; }

    public List<FailedRow<TRow>> Failed { get; set; } = new();
}

/// <summary>
/// Mirrors a single entry pushed into runBatches()'s `failed` array:
///   { row: singleRow, error: rowErr.toString() }
/// </summary>
public sealed class FailedRow<TRow>
{
    public required TRow Row { get; init; }

    public required string Error { get; init; }
}
