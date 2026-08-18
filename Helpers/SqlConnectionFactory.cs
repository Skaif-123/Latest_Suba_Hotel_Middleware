using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace AgentSyncConsole.Helpers;

/// <summary>Creates ADO.NET connections from the configured connection string. Registered as a singleton.</summary>
public class SqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AgentSyncDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:AgentSyncDb in appsettings.json");
    }

    public SqlConnection CreateOpenConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
