namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// One entry from hotelogix.response.data.transactions[], as used
/// by buildTransactionMap() / calculateTaxRate(). Only the fields
/// actually read by the original are modeled: id, hsnCode,
/// taxBreakup[].amount.
/// </summary>
public sealed class TransactionLookup
{
    public string Id { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public List<double> TaxBreakupAmounts { get; set; } = new();
}
