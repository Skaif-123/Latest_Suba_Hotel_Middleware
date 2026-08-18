using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.Models;
using Microsoft.Data.SqlClient;

namespace AgentSyncConsole.Repositories
{
    public class ThirdPartyDataRepository : IThirdPartyDataRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public ThirdPartyDataRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }


        public async Task<List<ThirdPartyData>> GetUnprocessedTransactionRowsAsync(int pageSize, CancellationToken ct = default)
        {
            var rows = new List<ThirdPartyData>();

            using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);

            const string query = @"
                SELECT ROWID, transactions, status, response
                FROM ThirdPartyData
                WHERE transactions IS NOT NULL
                AND ISNULL(syncstatus,'') =''
                ORDER BY ROWID ASC";

            using var command = new SqlCommand(query, connection);
            //command.Parameters.AddWithValue("@PageSize", pageSize);

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ThirdPartyData
                {
                    ROWID = reader["ROWID"] is DBNull ? 0 : Convert.ToInt32(reader["ROWID"]),
                    transactions = reader["transactions"] == DBNull.Value ? null : reader["transactions"]?.ToString(),
                    status = reader["status"] == DBNull.Value ? null : reader["status"]?.ToString(),
                    response = reader["response"] == DBNull.Value ? null : reader["response"]?.ToString()
                });
            }

            return rows;
        }

        public async Task<int> UpdateTransactionStatusBatchAsync(IReadOnlyList<ThirdPartyDataStatusUpdate> updates, CancellationToken ct = default)
        {
            if (updates.Count == 0) return 0;

            using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
            using var sqlTransaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

            try
            {
                var affected = 0;

                foreach (var update in updates)
                {
                    const string query = @"
                        UPDATE ThirdPartyData
                        SET syncstatus = @Status, 
                        syncresponse = @Response,
                        syncTime=SYSDATETIME()
                        WHERE ROWID = @ROWID";

                    using var command = new SqlCommand(query, connection, sqlTransaction);
                    command.Parameters.AddWithValue("@Status", update.Status);
                    command.Parameters.AddWithValue("@Response", (object?)update.Response ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ROWID", update.ROWID);

                    affected += await command.ExecuteNonQueryAsync(ct);
                }

                if (affected != updates.Count)
                {
                    throw new InvalidOperationException(
                        $"Expected {updates.Count} ThirdPartyData row(s) to update but {affected} were affected.");
                }

                await sqlTransaction.CommitAsync(ct);
                return affected;
            }
            catch
            {
                await sqlTransaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task UpdateTransactionStatusAsync(long rowId, string status, string response, CancellationToken ct = default)
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);

            const string query = @"
                UPDATE ThirdPartyData
                SET syncstatus = @Status, 
                syncresponse = @Response, 
                syncTime=SYSDATETIME()
                WHERE ROWID = @ROWID";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@Response", (object?)response ?? DBNull.Value);
            command.Parameters.AddWithValue("@ROWID", rowId);

            await command.ExecuteNonQueryAsync(ct);
        }
    }
}