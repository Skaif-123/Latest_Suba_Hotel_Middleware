using System.Net.NetworkInformation;
using AgentSyncConsole.InvoiceIngest.Constants;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.InvoiceIngest.Models;
using Dapper;

namespace AgentSyncConsole.InvoiceIngest.Repositories;

/// <summary>
/// Replaces thirdPartyTable = ds.table('ThirdPartyData') and the
/// STEP 1 ZCQL page fetch, plus the status writeback (Processed
/// via batch, Failed via single-row update).
/// </summary>
public sealed class ThirdPartyRepository : IThirdPartyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ThirdPartyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<ThirdPartyDataRow>> FetchUnprocessedPageAsync(
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Mirrors:
        //   SELECT ROWID, invoice, status, response FROM ThirdPartyData
        //   WHERE invoice IS NOT NULL
        //   AND (status IS NULL OR status != 'Processed')
        //   ORDER BY ROWID ASC LIMIT PAGE_SIZE
        //AND(status IS NULL OR status != 'Processed')

        const string sql = $"""
            SELECT ROWID, invoice AS Invoice, status AS Status, response AS Response
            FROM {TableNames.ThirdPartyData}
            WHERE invoice IS NOT NULL            
            AND ISNULL(syncstatus,'') =''
            ORDER BY ROWID ASC
            """;
        //Console.WriteLine($"sql query: {sql}");
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        //Console.WriteLine($"sql connection: {connection.ConnectionString}");
        //Console.WriteLine($"sql connection: {connection.ConnectionTimeout}");
        //Console.WriteLine($"sql connection: {connection.Database}");
        var rows = await connection.QueryAsync<ThirdPartyDataRow>(new CommandDefinition(
            sql,
            new { PageSize = pageSize, Processed = SyncConstants.StatusProcessed },
            cancellationToken: cancellationToken));
        Console.WriteLine($"rows: {rows.Count()}");

        return rows.ToList();
    }

    public async Task<int> UpdateRowsAsync(IReadOnlyList<ThirdPartyDataRow> rows, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0) return 0;

        const string sql = $"""
            UPDATE {TableNames.ThirdPartyData}
            SET syncstatus = @Status,
                syncresponse = @Response,
                syncTime=SYSDATETIME()
            WHERE ROWID = @ROWID
            """;

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(sql, rows, cancellationToken: cancellationToken));
    }

    public async Task UpdateRowAsync(ThirdPartyDataRow row, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            UPDATE {TableNames.ThirdPartyData}
            SET syncstatus = @Status,
                syncresponse = @Response,
                syncTime=SYSDATETIME()
            WHERE ROWID = @ROWID
            """;

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: cancellationToken));
    }
}

