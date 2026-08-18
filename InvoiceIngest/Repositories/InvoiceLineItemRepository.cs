using System.Transactions;
using Dapper;
using AgentSyncConsole.InvoiceIngest.Constants;
using AgentSyncConsole.InvoiceIngest.Enums;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.InvoiceIngest.Models;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AgentSyncConsole.InvoiceIngest.Repositories;

/// <summary>
/// Replaces lineItemTable = ds.table('Invoice_LineItem') and
/// queryLineItemMap(). Preserves the exact fallback behavior:
/// try Hotelogix_Trans_ID first; if that column doesn't exist on
/// the schema, switch keyMode to 'composite' for ALL remaining
/// chunks in this call and key by HSN_SAC_Code + Name + Amount.
/// </summary>
public sealed class InvoiceLineItemRepository : IInvoiceLineItemRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<InvoiceLineItemRepository> _logger;

    public InvoiceLineItemRepository(IDbConnectionFactory connectionFactory, ILogger<InvoiceLineItemRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<(Dictionary<string, string> Map, LineItemKeyMode KeyMode)> QueryLineItemMapAsync(
        IReadOnlyCollection<string> invoiceIds,
        int inChunk,
        CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, string>();
        var keyMode = LineItemKeyMode.TransId;

        if (invoiceIds.Count == 0)
        {
            return (map, keyMode);
        }

        var unique = invoiceIds.Distinct().ToList();

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        for (var i = 0; i < unique.Count; i += inChunk)
        {
            var chunk = unique.Skip(i).Take(inChunk).ToList();

            // ---- Primary: Hotelogix_Trans_ID column ----
            try
            {
                const string sql = $"""
                    SELECT ROWID, InvoiceID, Hotelogix_Trans_ID
                    FROM {TableNames.InvoiceLineItem}
                    WHERE InvoiceID IN @Ids
                    """;
                var rows = await connection.QueryAsync<InvoiceLineItem>(
                    new CommandDefinition(sql, new { Ids = chunk }, cancellationToken: cancellationToken));
                Console.WriteLine($"sql query lineitem: {sql}");
                Console.WriteLine($"sql rows lineitem: {rows}");

                foreach (var row in rows)
                {
                    var key = $"{row.InvoiceID}_{row.Hotelogix_Trans_ID}";
                    var rID = row.ROWID ?? string.Empty;

                    if (key != "_" && !map.ContainsKey(key))
                    {
                        map[key] = rID;
                    }
                }
            }
            catch (Exception transIdErr)
            {
                // Column absent — switch to composite for all remaining chunks.
                keyMode = LineItemKeyMode.Composite;
                _logger.LogInformation("Hotelogix_Trans_ID absent, using composite key");
                _ = transIdErr;

                try
                {
                    const string fbSql = $"""
                        SELECT ROWID, InvoiceID, HSN_SAC_Code, Name, Amount
                        FROM {TableNames.InvoiceLineItem}
                        WHERE InvoiceID IN @Ids
                        """;
                    Console.WriteLine("fbSql query lineitem", fbSql.ToString());
                    var fb = await connection.QueryAsync<InvoiceLineItem>(
                        new CommandDefinition(fbSql, new { Ids = chunk }, cancellationToken: cancellationToken));

                    foreach (var row in fb)
                    {
                        var key = $"{row.InvoiceID}_{row.HSN_SAC_Code}_{row.Name}_{row.Amount}";
                        var rID = row.ROWID ?? string.Empty;

                        if (key != "___" && !map.ContainsKey(key))
                        {
                            map[key] = rID;
                        }
                    }
                }
                catch (Exception compositeErr)
                {
                    _logger.LogInformation("Composite line item query error: {Error}", compositeErr.Message);
                }
            }
        }

        return (map, keyMode);
    }

    public async Task<int> InsertRowsAsync(IReadOnlyList<InvoiceLineItem> rows, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0) return 0;

        // Two statement variants: with and without Hotelogix_Trans_ID,
        // matching `if (lineItemKeyMode === 'transId') { lineItemData.Hotelogix_Trans_ID = transId; }`
        var withTransId = rows.Where(r => r.Hotelogix_Trans_ID is not null).ToList();
        var withoutTransId = rows.Where(r => r.Hotelogix_Trans_ID is null).ToList();

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = 0;

        //if (withTransId.Count > 0)
        //{
        //    const string sql = $"""
        //        INSERT INTO {TableNames.InvoiceLineItem}
        //            (InvoiceID, Name, Description, HSN_SAC_Code, Quality, Rate, Tax_Rate, TransactionID, Amount, Hotelogix_Trans_ID,CreatedTime)
        //        VALUES
        //            (@InvoiceID, @Name, @Description, @HSN_SAC_Code, @Quality, @Rate, @Tax_Rate, @TransactionID, @Amount, @Hotelogix_Trans_ID,SYSDATETIME())
        //        """;
        //    Console.WriteLine($"sql insert with transId invoice item : {sql}");
        //    affected += await connection.ExecuteAsync(new CommandDefinition(sql, withTransId, cancellationToken: cancellationToken));
        //}

        if (withoutTransId.Count > 0)
        {
            const string sql = $"""
                INSERT INTO {TableNames.InvoiceLineItem}
                    (InvoiceID, Name, Description, HSN_SAC_Code, Quality, Rate, Tax_Rate, TransactionID, Amount,CreatedTime)
                VALUES
                    (@InvoiceID, @Name, @Description, @HSN_SAC_Code, @Quality, @Rate, @Tax_Rate, @TransactionID, @Amount,SYSDATETIME())
                """;
            affected += await connection.ExecuteAsync(new CommandDefinition(sql, withoutTransId, cancellationToken: cancellationToken));
        }

        Console.WriteLine($"Total rows inserted invoice item : {affected}");

        return affected;
    }

    public async Task<int> UpdateRowsAsync(IReadOnlyList<InvoiceLineItem> rows, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"rows.Count: {rows.Count}");
        if (rows.Count == 0) return 0;

        const string sql = $"""
            UPDATE Invoice_LineItem
            SET Name = @Name,
                Description = @Description,
                HSN_SAC_Code = @HSN_SAC_Code,
                Quality = @Quality,
                Rate = @Rate,
                Tax_Rate = @Tax_Rate,
                TransactionID = @TransactionID,
                Amount = @Amount
            WHERE ROWID = @ROWID
            """;

        //Console.WriteLine($"update sql invoice item : {sql}");
        //Console.WriteLine($"ROWID          : @ROWID");
        //Console.WriteLine($"Name           : {name}");
        //Console.WriteLine($"Description    : {description}");
        //Console.WriteLine($"HSN_SAC_Code   : {hsnSacCode}");
        //Console.WriteLine($"Quality        : {quality}");
        //Console.WriteLine($"Rate           : {rate}");
        //Console.WriteLine($"Tax_Rate       : {taxRate}");
        //Console.WriteLine($"TransactionID  : {transactionId}");
        //Console.WriteLine($"Amount         : {amount}");

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: cancellationToken));
        Console.WriteLine($"Rows updated inoice item : {affected}");
        return affected;
    }
}
