using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using System.Threading.Tasks;
using System.Threading;

namespace AgentSyncConsole.Repositories;

/// <summary>Equivalent of: SELECT GST_ID FROM Tax_Master WHERE GST_Type = ? AND Rate = ? LIMIT 1</summary>
public class TaxMasterRepository : ITaxMasterRepository
{
    private readonly SqlConnectionFactory _factory;

    public TaxMasterRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<TaxMaster?> FindByTypeAndRateAsync(string gstType, string rate, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT GST_Type, cast(Rate as decimal(10,4)) as rate, GST_ID
                              FROM Tax_Master
                              WHERE GST_Type = @GstType AND Rate = @Rate";
        return await conn.QuerySingleOrDefaultAsync<TaxMaster>(
            new CommandDefinition(sql, new { GstType = gstType, Rate = rate }, cancellationToken: ct));
    }
}

