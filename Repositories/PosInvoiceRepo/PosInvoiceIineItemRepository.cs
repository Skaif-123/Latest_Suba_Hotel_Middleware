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
    /// SQL-backed repository for the existing "Posinvoice_LIneItem" table (name
    /// preserved verbatim). Reuses SqlConnectionFactory + Dapper exactly like
    /// AgentSyncConsole.InvoiceIngest.Repositories.InvoiceLineItemRepository
    /// (composite-key upsert map) and the Books-flavor InvoiceLineItemRepository
    /// (simple GetByInvoiceIdAsync used at the SQL -> Books stage).
    /// </summary>
    public sealed class PosInvoiceLineItemRepository : IPosInvoiceLineItemRepository
    {
        private const string Table = "PosInvoice_LineItem";

        private readonly SqlConnectionFactory _factory;
        private readonly ILogger<PosInvoiceLineItemRepository> _logger;

        public PosInvoiceLineItemRepository(SqlConnectionFactory factory, ILogger<PosInvoiceLineItemRepository> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        private static string CompositeKey(string invoiceId, string productName, string hsnCode, string totalPrice) =>
            $"{invoiceId}_{productName}_{hsnCode}_{totalPrice}";

        public async Task<Dictionary<string, string>> QueryLineItemMapAsync(
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
                    var sql = $"""
                    SELECT ROWID, Invoice_ID, Product_Name, hsnCode, Total_Price
                    FROM {Table}
                    WHERE Invoice_ID IN @Ids
                    """;

                    var rows = await conn.QueryAsync<PosInvoiceLineItem>(
                        new CommandDefinition(sql, new { Ids = chunk }, cancellationToken: ct));

                    foreach (var row in rows)
                    {
                        var key = CompositeKey(row.Invoice_ID, row.Product_Name, row.hsnCode, row.Total_Price.ToString() );
                        if (!map.ContainsKey(key))
                        {
                            map[key] = row.ROWID.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "QueryLineItemMapAsync (PosInvoice) error for chunk starting at index {Index}", i);
                }
            }

            return map;
        }

        public async Task<int> InsertRowsAsync(IReadOnlyList<PosInvoiceLineItem> rows, CancellationToken ct = default)
        {
            if (rows.Count == 0) return 0;

            var sql = $"""
            INSERT INTO {Table}
                (Invoice_ID, Product_Name, hsnCode, Quantity, Unit_Price, Total_Price, TaxValue, NetTotal)
            VALUES
                (@Invoice_ID, @Product_Name, @hsnCode, @Quantity, @Unit_Price, @Total_Price, @TaxValue, @NetTotal)
            """;

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: ct));
        }

        public async Task<int> UpdateRowsAsync(IReadOnlyList<PosInvoiceLineItem> rows, CancellationToken ct = default)
        {
            if (rows.Count == 0) return 0;

            var sql = $"""
            UPDATE {Table}
            SET Quantity = @Quantity,
                Unit_Price = @Unit_Price,
                Total_Price = @Total_Price,
                TaxValue = @TaxValue,
                NetTotal = @NetTotal
                
            WHERE ROWID = @ROWID
            """;

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: ct));
        }

        public async Task<IReadOnlyList<PosInvoiceLineItem>> GetByInvoiceIdAsync(string invoiceId, CancellationToken ct = default)
        {
            var sql = $"""
            SELECT ROWID, Invoice_ID, Product_Name, hsnCode, Quantity, Unit_Price, Total_Price, TaxValue, NetTotal   
            FROM {Table}
            WHERE Invoice_ID = @InvoiceID
            """;

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            var rows = await conn.QueryAsync<PosInvoiceLineItem>(
                new CommandDefinition(sql, new { InvoiceID = invoiceId }, cancellationToken: ct));
            return rows.AsList();
        }
    }

}
