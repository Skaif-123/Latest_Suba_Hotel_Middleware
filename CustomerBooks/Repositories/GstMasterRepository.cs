using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.CustomerBooks.Interfaces;
using AgentSyncConsole.CustomerBooks.Models;

namespace AgentSyncConsole.CustomerBooks.Repositories
{
    /// <summary>
    /// Ported from CustomerBooksSync.Api's Repositories/GstMasterRepository.cs,
    /// reusing the shared AgentSyncConsole.Helpers.SqlConnectionFactory (see
    /// CustomerRepository.cs for why). One correctness fix versus the source:
    /// the original UpdateBooksIdAsync's SQL text had a malformed parameter
    /// placeholder ("WHERE Id = @" followed by a stray line break) which
    /// would have thrown at execution time — corrected to "WHERE Id = @RowId"
    /// to match the parameter object actually being passed in.
    /// </summary>
    public class GstMasterRepository : IGstMasterRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public GstMasterRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<GstMasterRecord>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Array.Empty<GstMasterRecord>();
            }

            const string sql = @"
                SELECT *
                FROM dbo.GST_Master
                WHERE CustomerID = @CustomerId";

            using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
            var rows = await connection.QueryAsync<GstMasterRecord>(
                new CommandDefinition(sql, new { CustomerId = customerId }, cancellationToken: ct));

            return rows.AsList();
        }

        public async Task UpdateBooksIdAsync(int rowId, string booksId, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE dbo.GST_Master
                SET BooksID = @BooksId
                WHERE Id = @RowId";

            using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                sql, new { BooksId = booksId, RowId = rowId }, cancellationToken: ct));
        }
    }
}
