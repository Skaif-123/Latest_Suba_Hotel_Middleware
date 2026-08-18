using Dapper;
using AgentSyncConsole.InvoiceIngest.Constants;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.InvoiceIngest.Models;
using Microsoft.Extensions.Logging;

namespace AgentSyncConsole.InvoiceIngest.Repositories;

/// <summary>
/// Replaces invoiceTable = ds.table('Invoice') and queryInvoiceMap().
/// Preserves: unique-ID dedup, IN_CHUNK chunking, "first ROWID wins"
/// merge behavior (`if (iID && !map[iID]) { map[iID] = iROW; }`).
/// </summary>
public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<InvoiceRepository> _logger;

    public InvoiceRepository(IDbConnectionFactory connectionFactory, ILogger<InvoiceRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> QueryInvoiceMapAsync(
        IReadOnlyCollection<string> invoiceIds,
        int inChunk,
        CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, string>();

        if (invoiceIds.Count == 0)
        {
            return map;
        }

        var unique = invoiceIds.Distinct().ToList();

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        Console.WriteLine($"QueryInvoiceMapAsync: unique.Count = {unique.Count}, inChunk = {inChunk}");
        for (var i = 0; i < unique.Count; i += inChunk)
        {
            var chunk = unique.Skip(i).Take(inChunk).ToList();

            try
            {
                const string sql = $"""
                    SELECT ROWID, InvoiceID
                    FROM {TableNames.Invoice}
                    WHERE InvoiceID IN @Ids
                    """;

                var rows = await connection.QueryAsync<Invoice>(
                    new CommandDefinition(sql, new { Ids = chunk }, cancellationToken: cancellationToken));

                foreach (var inv in rows)
                {
                    var iID = inv.InvoiceID ?? string.Empty;
                    var iROW = inv.ROWID.ToString();

                    if (!string.IsNullOrEmpty(iID) && !map.ContainsKey(iID))
                    {
                        map[iID] = iROW;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation("queryInvoiceMap error: {Error}", e.Message);
            }
        }

        return map;
    }

    public async Task<int> InsertRowsAsync(IReadOnlyList<Invoice> rows, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0) return 0;

        const string sql = $"""
            INSERT INTO {TableNames.Invoice}
                (Hotel_ID, Customer_Name, Location_Name, Invoice_Number, Invoice_Date,
                 Owner_Type, Payment_Term, Due_Date, InvoiceID, Reservation_ID,CreatedTime)
            VALUES
                (@Hotel_ID, @Customer_Name, @Location_Name, @Invoice_Number, @Invoice_Date,
                 @Owner_Type, @Payment_Term, @Due_Date, @InvoiceID, @Reservation_ID,SYSDATETIME())
            """;

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: cancellationToken));
        if (affected != rows.Count)
        {
            throw new Exception(
                $"Invoice insert mismatch. Expected: {rows.Count}, Actual: {affected}");
        }
        return affected;
    }

    public async Task<int> UpdateRowsAsync(IReadOnlyList<Invoice> rows, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0) return 0;

        const string sql = $"""
            UPDATE {TableNames.Invoice}
            SET Hotel_ID = @Hotel_ID,
                Customer_Name = @Customer_Name,
                Location_Name = @Location_Name,
                Invoice_Number = @Invoice_Number,
                Invoice_Date = @Invoice_Date,
                Owner_Type = @Owner_Type,
                Payment_Term = @Payment_Term,
                Due_Date = @Due_Date,
                Reservation_ID = @Reservation_ID
            WHERE ROWID = @ROWID
            """;
        Console.WriteLine($"sql update invoice main : {sql}");
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: cancellationToken));
        return affected;
    }
}