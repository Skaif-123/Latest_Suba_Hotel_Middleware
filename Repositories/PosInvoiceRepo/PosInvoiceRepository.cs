using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces.PosInvoiceInterface;
using AgentSyncConsole.Models.PosInoviceModel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AgentSyncConsole.Repositories.PosInvoiceRepo
{
  
    /// <summary>
    /// SQL-backed repository for the existing "PosInvoice" table. Reuses the
    /// shared SqlConnectionFactory + Dapper exactly like every other repository in
    /// the project (AgentSyncConsole.InvoiceIngest.Repositories.InvoiceRepository
    /// for the map/insert/update shape, AgentSyncConsole.Repositories.InvoiceRepository
    /// for the partial-column-update shape). No new table was created.
    /// </summary>
    public sealed class PosInvoiceRepository : IPosInvoiceRepository
    {
        private const string Table = "PosInvoice";

        private readonly SqlConnectionFactory _factory;
        private readonly ILogger<PosInvoiceRepository> _logger;

        public PosInvoiceRepository(SqlConnectionFactory factory, ILogger<PosInvoiceRepository> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> QueryInvoiceMapAsync(
            IReadOnlyCollection<string> invoiceIds, int inChunk, CancellationToken ct = default)
        {
            var map = new Dictionary<string, string>();
            if (invoiceIds.Count == 0) return map;

            var unique = invoiceIds.Distinct().ToList();
            using var conn = await _factory.CreateOpenConnectionAsync(ct);

            for (var i = 0; i < unique.Count; i += inChunk)
            {
                var chunk = unique.Skip(i).Take(inChunk).ToList();

                try
                {
                    var sql = $"SELECT ROWID, Invoice_ID FROM {Table} WHERE Invoice_ID IN @Ids";
                    var rows = await conn.QueryAsync<PosInvoice>(
                        new CommandDefinition(sql, new { Ids = chunk }, cancellationToken: ct));

                    foreach (var row in rows)
                    {
                        if (!string.IsNullOrEmpty(row.Invoice_ID) && !map.ContainsKey(row.Invoice_ID))
                        {
                            map[row.Invoice_ID] = row.ROWID.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "QueryInvoiceMapAsync (PosInvoice) error for chunk starting at index {Index}", i);
                }
            }

            return map;
        }

        public async Task<int> InsertRowsAsync(IReadOnlyList<PosInvoice> rows, CancellationToken ct = default)
        {
            if (rows.Count == 0) return 0;

            var sql = $"""
            INSERT INTO {Table}
                (Invoice_ID, Invoice_Number, Invoice_No, posPointId, posPointName, Invoice_status,
                 Owner_Type, GSTin_ID, Subtotal, NetTotal, HotelID,PaymentMode)
            VALUES
                (@Invoice_ID, @Invoice_Number, @Invoice_No, @posPointId, @posPointName, @Invoice_status,
                 @Owner_Type, @GSTin_ID, @Subtotal, @NetTotal, @HotelID,@Payment_Term)
            """;

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: ct));
        }

        public async Task<int> UpdateRowsAsync(IReadOnlyList<PosInvoice> rows, CancellationToken ct = default)
        {
            if (rows.Count == 0) return 0;

            var sql = $"""
            UPDATE {Table}
            SET Invoice_Number = @Invoice_Number,
                Invoice_No = @Invoice_No,
                posPointId = @posPointId,
                posPointName = @posPointName,
                Invoice_status = @Invoice_status,
                Owner_Type = @Owner_Type,
                GSTin_ID = @GSTin_ID,
                Subtotal = @Subtotal,
                NetTotal = @NetTotal,
            HotelID = @HotelID
            WHERE ROWID = @ROWID
            """;

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: ct));
        }

        public async Task<IReadOnlyList<PosInvoice>> GetAllRowsAsync(CancellationToken ct = default)
        {
            var sql = $"""
            SELECT ROWID, Invoice_ID, Invoice_Number, Invoice_No, posPointId, posPointName, Invoice_status,
                   Owner_Type, GSTin_ID, Subtotal,NetTotal, HotelID, PaymentMode
            FROM {Table}
            """;

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            var rows = await conn.QueryAsync<PosInvoice>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.AsList();
        }

        /// <summary>Partial column update by ROWID — mirrors AgentSyncConsole.Repositories.InvoiceRepository.UpdateRowAsync's AddIfSet pattern.</summary>
        public async Task UpdateRowAsync(PosInvoice invoice, CancellationToken ct = default)
        {
            using var conn = await _factory.CreateOpenConnectionAsync(ct);

            var setClauses = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("ROWID", invoice.ROWID);

            void AddIfSet(string column, object? value)
            {
                if (value is null) return;
                setClauses.Add($"{column} = @{column}");
                parameters.Add(column, value);
            }

            AddIfSet("BooksInvoiceID", invoice.BooksInvoiceID);
            AddIfSet("Books_Status", invoice.Books_Status);
            AddIfSet("Response", invoice.Response);

            if (setClauses.Count == 0)
            {
                _logger.LogWarning("PosInvoice.UpdateRowAsync called for ROWID {RowId} with no columns to update", invoice.ROWID);
                return;
            }

            var sql = $"UPDATE {Table} SET {string.Join(", ", setClauses)} WHERE ROWID = @ROWID";
            await conn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
    }

}
