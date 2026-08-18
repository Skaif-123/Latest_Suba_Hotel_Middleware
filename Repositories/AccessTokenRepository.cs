using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using System.Threading.Tasks;
using System.Threading;

namespace AgentSyncConsole.Repositories;

/// <summary>
/// Equivalent of: SELECT * FROM accesToken WHERE application = ? ORDER BY CREATEDTIME DESC LIMIT 1,
/// plus the save/update side of the token lifecycle.
/// </summary>
public class AccessTokenRepository : IAccessTokenRepository
{
    private readonly SqlConnectionFactory _factory;

    public AccessTokenRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<AccessTokenRecord?> GetLatestAsync(string application, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT TOP 1 ROWID, application, accessToken, refreshToken, expiresAt, CREATEDTIME, MODIFIEDTIME
                              FROM accesToken
                              WHERE application = @Application
                              ORDER BY CREATEDTIME DESC";
        return await conn.QuerySingleOrDefaultAsync<AccessTokenRecord>(
            new CommandDefinition(sql, new { Application = application }, cancellationToken: ct));
    }

    public async Task SaveAsync(AccessTokenRecord token, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        if (token.ROWID > 0)
        {
            const string updateSql = @"UPDATE accesToken
                                        SET accessToken = @accessToken,
                                            refreshToken = @refreshToken,
                                            expiresAt = @expiresAt,
                                            MODIFIEDTIME = SYSUTCDATETIME()
                                        WHERE ROWID = @ROWID";
            await conn.ExecuteAsync(new CommandDefinition(updateSql, token, cancellationToken: ct));
        }
        else
        {
            const string insertSql = @"

accesToken (application, accessToken, refreshToken, expiresAt, CREATEDTIME)
                                        VALUES (@application, @accessToken, @refreshToken, @expiresAt, SYSUTCDATETIME())";
            await conn.ExecuteAsync(new CommandDefinition(insertSql, token, cancellationToken: ct));
        }
    }
}
