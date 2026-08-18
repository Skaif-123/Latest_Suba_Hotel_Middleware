using System.Transactions;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces.TransactionInterface;

/// <summary>
/// SQL Server equivalent of catalystApp.datastore().table('Transaction') as used
/// by the Hotelogix Transaction Sync function (queryTransactionMap, insertRows,
/// updateRows). New — no Transaction table/repository existed anywhere in this
/// project prior to this conversion.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// SQL Server equivalent of queryTransactionMap(): returns { Transaction_ID: ROWID }
    /// for the given IDs only, chunked at inChunk to mirror IN_CHUNK in the original.
    /// </summary>
    Task<Dictionary<string, long>> GetTransactionRowIdMapAsync(
        IEnumerable<string> transactionIds, int inChunk, CancellationToken ct = default);

    /// <summary>
    /// Returns the full TransactionModule rows (Transaction_ID, Tax_value, HSN_Code, etc.)
    /// for the given Transaction_IDs only, keyed by Transaction_ID, chunked at inChunk.
    /// Used by Invoice Sync to copy Tax_Value/HSN_Code onto InvoiceLineItem without ever
    /// parsing transaction JSON itself.
    /// </summary>
    Task<Dictionary<string, Models.Transaction>> GetTransactionsByIdsAsync(
        IEnumerable<string> transactionIds, int inChunk, CancellationToken ct = default);

    /// <summary>Batch insert. Returns the number of rows actually inserted.</summary>
    Task<int> InsertRowsAsync(IReadOnlyList<Models.Transaction> rows, CancellationToken ct = default);

    /// <summary>Batch update by ROWID. Returns the number of rows actually updated.</summary>
    Task<int> UpdateRowsAsync(IReadOnlyList<Models.Transaction> rows, CancellationToken ct = default);
}