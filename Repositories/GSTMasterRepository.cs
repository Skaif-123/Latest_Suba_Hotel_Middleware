using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Repositories
{
    /// <summary>
    /// Replaces the GST_Master DataStore operations:
    ///   - duplicate check: SELECT ROWID FROM GST_Master WHERE CustomerID=... AND GST_No=... LIMIT 1
    ///   - gstMasterTable.insertRows(gstRowsToInsert) -> BulkInsertAsync (SqlBulkCopy)
    /// </summary>
    public class GSTMasterRepository : IGSTMasterRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public GSTMasterRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<bool> ExistsAsync(string customerId, string gstNo)
        {
            const string sql = @"
                SELECT TOP 1 ROWID
                FROM GST_Master
                WHERE CustomerID = @CustomerID
                AND GST_No = @GST_No";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            var rowId = await conn.QueryFirstOrDefaultAsync<string>(sql, new { CustomerID = customerId, GST_No = gstNo });
            return rowId != null;
        }

        public async Task BulkInsertAsync(List<GSTMasterRecord> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            var table = new DataTable();
            table.Columns.Add("CustomerID", typeof(string));
            table.Columns.Add("GST_No", typeof(string));
            table.Columns.Add("Place_Of_Supply", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("isDefault", typeof(string));
            table.Columns.Add("BooksID", typeof(string));

            foreach (var r in rows)
            {
                table.Rows.Add(
                    r.CustomerID ?? "",
                    r.GST_No ?? "",
                    r.Place_Of_Supply ?? "",
                    r.Name ?? "",
                    r.IsDefault ?? "undefined",
                    r.BooksID ?? "");
            }

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            using var bulkCopy = new SqlBulkCopy(conn);
            bulkCopy.DestinationTableName = "GST_Master";

            foreach (DataColumn col in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table);
        }
    }
}
