using AgentSyncConsole.InvoiceIngest.Models;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.InvoiceIngest.Interfaces;

/// <summary>
/// Replaces: thirdPartyTable = ds.table('ThirdPartyData') and the
/// STEP 1 ZCQL page fetch:
///   SELECT ROWID, invoice, status, response FROM ThirdPartyData
///   WHERE invoice IS NOT NULL
///   AND (status IS NULL OR status != 'Processed')
///   ORDER BY ROWID ASC LIMIT PAGE_SIZE
/// plus thirdPartyTable.updateRows()/updateRow() for status writeback.
/// </summary>
public interface IThirdPartyRepository
{
    Task<List<ThirdPartyDataRow>> FetchUnprocessedPageAsync(
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Batch update — mirrors thirdPartyTable.updateRows(batch) for the Processed path.</summary>
    Task<int> UpdateRowsAsync(IReadOnlyList<ThirdPartyDataRow> rows, CancellationToken cancellationToken = default);

    /// <summary>Single-row update — mirrors thirdPartyTable.updateRow(...) for the Failed path.</summary>
    Task UpdateRowAsync(ThirdPartyDataRow row, CancellationToken cancellationToken = default);
}

public interface IThirdPartyDataRepository
{
    Task<List<ThirdPartyData>> GetUnprocessedTransactionRowsAsync(int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Batched status/response write-back for successfully processed rows —
    /// mirrors thirdPartyTable.updateRows(successUpdates) in the original.
    /// Returns the number of rows actually updated.
    /// </summary>
    Task<int> UpdateTransactionStatusBatchAsync(IReadOnlyList<ThirdPartyDataStatusUpdate> updates, CancellationToken ct = default);

    /// <summary>
    /// Single-row status/response write-back — mirrors the direct
    /// (non-batched) thirdPartyTable.updateRow(...) loop used for failures
    /// in the original source.
    /// </summary>
    Task UpdateTransactionStatusAsync(long rowId, string status, string response, CancellationToken ct = default);
}
