namespace AgentSyncConsole.InvoiceIngest.Models;

/// <summary>
/// Maps to SQL Server "Invoice_LineItem" table (was Catalyst
/// datastore table 'Invoice_LineItem'). Hotelogix_Trans_ID is
/// nullable/optional exactly like in the original — that column
/// only gets populated when lineItemKeyMode == transId (i.e. the
/// column exists on the target schema). When keyMode is
/// 'composite', the column is omitted from writes, matching:
///   if (lineItemKeyMode === 'transId') {
///       lineItemData.Hotelogix_Trans_ID = transId;
///   }
/// </summary>
public sealed class InvoiceLineItem
{
    public string ROWID { get; set; } = string.Empty;
    public string InvoiceID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HSN_SAC_Code { get; set; } = string.Empty;
    public string Quality { get; set; } = "1";
    public string Rate { get; set; } = string.Empty;
    public double Tax_Rate { get; set; }
    public string TransactionID { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;

    /// <summary>Only set/written when lineItemKeyMode == 'transId'.</summary>
    public string? Hotelogix_Trans_ID { get; set; }
}
