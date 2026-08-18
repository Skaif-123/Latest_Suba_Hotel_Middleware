namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors the object returned by calculateTaxRate():
///   { taxRate: Number, hsnCode: String }
/// </summary>
public sealed class TaxRateResult
{
    public double TaxRate { get; set; }
    public string HsnCode { get; set; } = string.Empty;
}
