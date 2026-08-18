using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Threading;

namespace AgentSyncConsole.Repositories;

/// <summary>
/// SQL-backed equivalent of catalystApp.datastore().table('Invoice').
/// getAllRows() / updateRow() map to SELECT * and a column-aware UPDATE.
/// </summary>
public class InvoiceRepository : IInvoiceRepository
{
    private readonly SqlConnectionFactory _factory;
    private readonly ILogger<InvoiceRepository> _logger;

    public InvoiceRepository(SqlConnectionFactory factory, ILogger<InvoiceRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Invoice>> GetAllRowsAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT ROWID, InvoiceID, Customer_Name, Hotel_ID, Invoice_Number,
                                     Invoice_Date, Payment_Term, Due_Date, BooksInvoiceID,
                                     Books_Status, Response, ThirdParty_status, Location_Name, Owner_Type
                              FROM Invoice";
        Console.WriteLine($"Executing SQL: {sql}");
        var rows = await conn.QueryAsync<Invoice>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Updates only the columns provided (non-null). Mirrors the Catalyst
    /// updateRow(...) pattern where each call passes a partial object keyed by ROWID.
    /// </summary>
    public async Task UpdateRowAsync(Invoice invoice, CancellationToken ct = default)
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

        var CurrentSyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        AddIfSet("BooksInvoiceID", invoice.BooksInvoiceID);
        AddIfSet("Books_Status", invoice.Books_Status);
        AddIfSet("Response", invoice.Response);
        AddIfSet("ThirdParty_status", invoice.ThirdParty_status);
        AddIfSet("syncTime", CurrentSyncTime);
        AddIfSet("syncstatus", invoice.ThirdParty_status);
        AddIfSet("syncresponse", invoice.Response);

        if (setClauses.Count == 0)
        {
            _logger.LogWarning("UpdateRowAsync called for ROWID {RowId} with no columns to update", invoice.ROWID);
            return;
        }

        var sql = $"UPDATE Invoice SET {string.Join(", ", setClauses)} WHERE ROWID = @ROWID";
        await conn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }
}