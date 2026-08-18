using AgentSyncConsole.InvoiceIngest.Models;

namespace AgentSyncConsole.InvoiceIngest.Interfaces;

/// <summary>
/// Replaces: invoiceTable = ds.table('Invoice') and the
/// queryInvoiceMap() ZCQL query
///   SELECT ROWID, InvoiceID FROM Invoice WHERE InvoiceID IN (...)
/// chunked at IN_CHUNK, plus invoiceTable.insertRows()/updateRows().
/// </summary>
public interface IInvoiceRepository
{
    /// <summary>
    /// Returns InvoiceID -> ROWID for the given IDs, querying in
    /// IN_CHUNK-sized chunks exactly like queryInvoiceMap().
    /// </summary>
    Task<Dictionary<string, string>> QueryInvoiceMapAsync(
        IReadOnlyCollection<string> invoiceIds,
        int inChunk,
        CancellationToken cancellationToken = default);

    /// <summary>Batch insert — mirrors invoiceTable.insertRows(batch).</summary>
    Task<int> InsertRowsAsync(IReadOnlyList<Invoice> rows, CancellationToken cancellationToken = default);

    /// <summary>Batch update — mirrors invoiceTable.updateRows(batch).</summary>
    Task<int> UpdateRowsAsync(IReadOnlyList<Invoice> rows, CancellationToken cancellationToken = default);
}
