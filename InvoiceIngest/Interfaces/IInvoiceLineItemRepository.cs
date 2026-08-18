using AgentSyncConsole.InvoiceIngest.Enums;
using AgentSyncConsole.InvoiceIngest.Models;

namespace AgentSyncConsole.InvoiceIngest.Interfaces;

/// <summary>
/// Replaces: lineItemTable = ds.table('Invoice_LineItem') and the
/// queryLineItemMap() ZCQL query, which tries Hotelogix_Trans_ID
/// first and falls back to a composite key (HSN_SAC_Code + Name +
/// Amount) if that column doesn't exist, plus
/// lineItemTable.insertRows()/updateRows().
/// </summary>
public interface IInvoiceLineItemRepository
{
    /// <summary>
    /// Returns (map, keyMode) exactly like queryLineItemMap():
    /// map keyed by "InvoiceID_TransId" when keyMode == TransId,
    /// or "InvoiceID_HSN_Name_Amount" when keyMode == Composite.
    /// </summary>
    Task<(Dictionary<string, string> Map, LineItemKeyMode KeyMode)> QueryLineItemMapAsync(
        IReadOnlyCollection<string> invoiceIds,
        int inChunk,
        CancellationToken cancellationToken = default);

    Task<int> InsertRowsAsync(IReadOnlyList<InvoiceLineItem> rows, CancellationToken cancellationToken = default);

    Task<int> UpdateRowsAsync(IReadOnlyList<InvoiceLineItem> rows, CancellationToken cancellationToken = default);
}
