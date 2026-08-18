using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using System.Threading.Tasks;
using System.Threading;
using System;
namespace AgentSyncConsole.Repositories;

/// <summary>Equivalent of: SELECT * FROM Invoice_LineItem WHERE InvoiceID = ?</summary>
public class InvoiceLineItemRepository : IInvoiceLineItemRepository
{
    private readonly SqlConnectionFactory _factory;

    public InvoiceLineItemRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<InvoiceLineItem>> GetByInvoiceIdAsync(string invoiceId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT InvoiceID, Name, Description, HSN_SAC_Code, Quality, Rate, Tax_Rate, Amount
                              FROM Invoice_LineItem
                              WHERE InvoiceID = @InvoiceID";
        Console.WriteLine($"Executing SQL: {sql} with InvoiceID: {invoiceId}");
        var rows = await conn.QueryAsync<InvoiceLineItem>(
            new CommandDefinition(sql, new { InvoiceID = invoiceId }, cancellationToken: ct));
        return rows.AsList();
    }
}
