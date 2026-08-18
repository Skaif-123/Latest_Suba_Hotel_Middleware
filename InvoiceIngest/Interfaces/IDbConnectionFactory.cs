using System.Data;

namespace AgentSyncConsole.InvoiceIngest.Interfaces;

/// <summary>
/// Replaces Catalyst's implicit `app.datastore()` / `app.zcql()`
/// connection. Produces an open SQL Server connection for
/// Dapper-based repositories.
/// </summary>
public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
