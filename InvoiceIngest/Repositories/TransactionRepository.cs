using System.Transactions;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces.TransactionInterface;
using AgentSyncConsole.Models;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AgentSyncConsole.Repositories;

/// <summary>
/// SQL-backed equivalent of catalystApp.datastore().table('Transaction') used by
/// the Hotelogix Transaction Sync function. queryTransactionMap()/insertRows()/
/// updateRows() map to parameterized, batched SELECT/INSERT/UPDATE statements.
/// Insert and update batches are each wrapped in their own SQL transaction so a
/// partially-failing batch never leaves half-written rows behind — the caller
/// (TransactionSyncService's runBatches-equivalent helper) then retries the
/// batch row-by-row on failure, exactly like the original runBatches().
/// "Transaction" is a SQL Server reserved word, so every reference to the table
/// is bracket-quoted. New — no Transaction table/repository existed anywhere in
/// this project prior to this conversion.
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly SqlConnectionFactory _factory;
    private readonly ILogger<TransactionRepository> _logger;

    public TransactionRepository(SqlConnectionFactory factory, ILogger<TransactionRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<Dictionary<string, long>> GetTransactionRowIdMapAsync(
        IEnumerable<string> transactionIds, int inChunk, CancellationToken ct = default)
    {
        var map = new Dictionary<string, long>();

        var unique = transactionIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (unique.Count == 0) return map;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        for (var i = 0; i < unique.Count; i += inChunk)
        {
            var chunk = unique.Skip(i).Take(inChunk).ToList();

            try
            {
                const string sql = @"SELECT ROWID, Transaction_ID FROM [TransactionModule] WHERE Transaction_ID IN @Ids";

                var rows = await conn.QueryAsync<Models.Transaction>(
                    new CommandDefinition(sql, new { Ids = chunk }, cancellationToken: ct));

                foreach (var row in rows)
                {
                    if (!string.IsNullOrEmpty(row.Transaction_ID) && !map.ContainsKey(row.Transaction_ID))
                    {
                        map[row.Transaction_ID] = row.ROWID;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetTransactionRowIdMapAsync error for chunk starting at index {Index}", i);
            }
        }

        return map;
    }

    public async Task<Dictionary<string, Models.Transaction>> GetTransactionsByIdsAsync(
        IEnumerable<string> transactionIds, int inChunk, CancellationToken ct = default)
    {
        var map = new Dictionary<string, Models.Transaction>();

        var unique = transactionIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (unique.Count == 0) return map;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        for (var i = 0; i < unique.Count; i += inChunk)
        {
            var chunk = unique.Skip(i).Take(inChunk).ToList();

            try
            {
                const string sql = @"SELECT ROWID, Transaction_ID, Reservation_ID, Tax_value, HSN_Code, Product_Name, Amount, Rate FROM [TransactionModule] WHERE Transaction_ID IN @Ids";

                var rows = await conn.QueryAsync<Models.Transaction>(
                    new CommandDefinition(sql, new { Ids = chunk }, cancellationToken: ct));

                foreach (var row in rows)
                {
                    if (!string.IsNullOrEmpty(row.Transaction_ID) && !map.ContainsKey(row.Transaction_ID))
                    {
                        map[row.Transaction_ID] = row;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetTransactionsByIdsAsync error for chunk starting at index {Index}", i);
            }
        }

        return map;
    }

    public async Task<int> InsertRowsAsync(IReadOnlyList<Models.Transaction> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return 0;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        using var sqlTransaction = conn.BeginTransaction();

        try
        {
            const string sql = @"INSERT INTO [TransactionModule]
                (Transaction_ID, Reservation_ID, Tax_value, HSN_Code, Product_Name, Amount, Rate,CreatedTime)
                VALUES (@Transaction_ID, @Reservation_ID, @Tax_value, @HSN_Code, @Product_Name, @Amount, @Rate,SYSDATETIME())";

            var affected = await conn.ExecuteAsync(
                new CommandDefinition(sql, rows, sqlTransaction, cancellationToken: ct));

            if (affected != rows.Count)
            {
                throw new InvalidOperationException(
                    $"Expected {rows.Count} TransactionModu row(s) to insert but {affected} were affected.");
            }

            sqlTransaction.Commit();
            return affected;
        }
        catch
        {
            sqlTransaction.Rollback();
            throw;
        }
    }

    public async Task<int> UpdateRowsAsync(IReadOnlyList<Models.Transaction> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return 0;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        using var sqlTransaction = conn.BeginTransaction();

        try
        {
            const string sql = @"UPDATE TransactionModule SET
                Reservation_ID = @Reservation_ID,
                Tax_value = @Tax_value,
                HSN_Code = @HSN_Code,
                Product_Name = @Product_Name,
                Amount = @Amount,
                Rate = @Rate,
               
                syncTime= SYSDATETIME(),
                syncStatus = @status,
                syncResponse = @response
                WHERE Transaction_ID = @Transaction_ID";

            foreach (var row in rows)
            {
                Console.WriteLine(
                    $"ROWID={row.ROWID}, TransactionID={row.Transaction_ID}, ReservationID={row.Reservation_ID}");
            }

            var affected = await conn.ExecuteAsync(
                new CommandDefinition(sql, rows, sqlTransaction, cancellationToken: ct));
             
            // A ROWID that doesn't match any row (e.g. the same-execution "pending"
            // placeholder — see TransactionSyncService's PendingRowIdSentinel comment)
            // affects 0 rows without SQL Server throwing. Surface that as a failure
            // here instead of silently under-counting, so it flows into the same
            // failure/retry path as a genuine error.
            if (affected != rows.Count)
            {
                throw new InvalidOperationException(
                    $"Expected {rows.Count} Transaction row(s) to update but {affected} were affected — possible missing ROWID.");
            }

            sqlTransaction.Commit();
            return affected;
        }
        catch
        {
            sqlTransaction.Rollback();
            throw;
        }
    }
}