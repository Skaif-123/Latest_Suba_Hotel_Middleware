using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace AgentSyncConsole.Repositories;

/// <summary>Equivalent of: SELECT locationID, stateCode, locationName, gstNo FROM Location_Master WHERE hotelID = ? LIMIT 1</summary>
public class LocationMasterRepository : ILocationMasterRepository
{
    private readonly SqlConnectionFactory _factory;

    public LocationMasterRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<LocationMaster?> FindByHotelIdAsync(string hotelId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT TOP 1 hotelID, locationID, locationName, stateCode, gstNo
                              FROM Location_Master
                              WHERE hotelID = @HotelID";
        return await conn.QuerySingleOrDefaultAsync<LocationMaster>(
            new CommandDefinition(sql, new { HotelID = hotelId }, cancellationToken: ct));
    }
}
