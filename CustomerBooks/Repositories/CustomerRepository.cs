using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.CustomerBooks.Interfaces;
using AgentSyncConsole.CustomerBooks.Models;

namespace AgentSyncConsole.CustomerBooks.Repositories
{
    /// <summary>
    /// Ported from CustomerBooksSync.Api's Repositories/CustomerRepository.cs.
    ///
    /// SHARED-INFRASTRUCTURE NOTE: the original project had its own
    /// ISqlConnectionFactory/SqlConnectionFactory pointing at the exact same
    /// database (same server, same "SUBAZOHOBooks" database, same
    /// credentials) as AgentSyncConsole's existing Helpers.SqlConnectionFactory
    /// (ConnectionStrings:AgentSyncDb). Since this is genuinely the same
    /// connection, duplicating a second factory/connection string would only
    /// create drift risk, so this repository reuses the existing shared
    /// AgentSyncConsole.Helpers.SqlConnectionFactory instead of introducing a
    /// second one. Dapper opens the connection automatically if it isn't
    /// already open, so behavior is unchanged.
    /// </summary>
    public class CustomerRepository : ICustomerRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ILogger<CustomerRepository> _logger;

        public CustomerRepository(SqlConnectionFactory connectionFactory, ILogger<CustomerRepository> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<CustomerRecord>> GetPageAsync(int offset, int pageSize, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT *
                FROM dbo.Customer
                ORDER BY ROWId
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
            var rows = await connection.QueryAsync<CustomerRecord>(
                new CommandDefinition(sql, new { Offset = offset, PageSize = pageSize }, cancellationToken: ct));

            return rows.AsList();
        }

        // ROOT CAUSE FIX (booksID silently not persisted despite the
        // Contact being created in Zoho Books): the previous version wrote
        // booksID + Response + status in one single UPDATE per row, batched
        // inside one shared transaction. Response holds the *entire* raw
        // Zoho Books JSON payload (contact_persons, addresses, custom
        // fields, etc.), which is by far the most likely piece of that
        // statement to fail for a given row (oversized text for the
        // column, odd characters, encoding, etc.). Because all three
        // columns were written together:
        //   1) one bad Response value threw and rolled back the WHOLE
        //      transactional batch (every row in the page, not just the
        //      offending one), and
        //   2) the per-row fallback re-issued the exact same combined
        //      statement, so the offending row failed again for the same
        //      reason and its booksID was never written — even though the
        //      Contact genuinely exists in Zoho Books.
        // Fix: the booksID/status write (the only two columns the rest of
        // the pipeline actually depends on for correctness/idempotency) is
        // now fully decoupled from the best-effort Response write. A
        // failure persisting the diagnostic Response text can no longer
        // undo a successful booksID write.
        private const string CriticalUpdateSql = @"
                UPDATE dbo.Customer
                SET booksID = @BooksId,
                    syncstatus = @Status,
                    syncTime = SYSDATETIME()
                WHERE ROWID = @RowId";

        private const string ResponseOnlyUpdateSql = @"
                UPDATE dbo.Customer
                SET syncresponse = @Response
                WHERE ROWID = @RowId";

        // Kept for the happy-path batch attempt, where writing everything
        // in one statement is fine as long as nothing throws.
        private const string FullUpdateSql = @"
                UPDATE dbo.Customer
                SET booksID = @BooksId,
                    syncresponse = @Response,
                    syncstatus = @Status,
                    syncTime = SYSDATETIME()
                WHERE ROWID = @RowId";

        public async Task UpdateResultsAsync(IEnumerable<CustomerSyncResult> results, CancellationToken ct = default)
        {
            var resultList = results.ToList();
            if (resultList.Count == 0)
            {
                return;
            }

            using (var connection = await _connectionFactory.CreateOpenConnectionAsync(ct))
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    foreach (var result in resultList)
                    {
                        await connection.ExecuteAsync(new CommandDefinition(
                            FullUpdateSql,
                            new { result.BooksId, result.Response, result.Status, result.RowId },
                            transaction,
                            cancellationToken: ct));
                    }

                    transaction.Commit();
                    _logger.LogInformation("Customer table updated for {Count} rows.", resultList.Count);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch update of Customer results failed. Falling back to per-row updates...");
                    transaction.Rollback();
                }
            }

            // Per-row fallback: write the critical fields (booksID/status)
            // and the diagnostic field (Response) as two independent
            // operations, so a Response failure can never take the
            // booksID write down with it.
            foreach (var result in resultList)
            {
                try
                {
                    using var rowConnection = await _connectionFactory.CreateOpenConnectionAsync(ct);
                    await rowConnection.ExecuteAsync(new CommandDefinition(
                        CriticalUpdateSql,
                        new { result.BooksId, result.Status, result.RowId },
                        cancellationToken: ct));
                }
                catch (Exception rowEx)
                {
                    _logger.LogError(
                        rowEx,
                        "CRITICAL: booksID/status update failed for RowId={RowId}, CustomerId={CustomerId}, BooksId={BooksId}. This customer was synced to Zoho Books but the local record was NOT updated — it will be re-created as a duplicate on the next run unless this is fixed.",
                        result.RowId, result.CustomerId, result.BooksId);
                    // Never stop the run — continue with the next row's update.
                    continue;
                }

                try
                {
                    using var responseConnection = await _connectionFactory.CreateOpenConnectionAsync(ct);
                    await responseConnection.ExecuteAsync(new CommandDefinition(
                        ResponseOnlyUpdateSql,
                        new { result.Response, result.RowId },
                        cancellationToken: ct));
                }
                catch (Exception responseEx)
                {
                    // Diagnostic-only field — booksID above already
                    // succeeded, so this is a warning, not an error.
                    _logger.LogWarning(
                        responseEx,
                        "Response write failed for RowId={RowId} (booksID/status were still saved successfully).",
                        result.RowId);
                }
            }
        }
    }
}