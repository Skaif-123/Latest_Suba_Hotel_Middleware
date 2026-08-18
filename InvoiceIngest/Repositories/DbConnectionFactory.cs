using System.Data;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.InvoiceIngest.Interfaces;

namespace AgentSyncConsole.InvoiceIngest.Repositories;

/// <summary>
/// Thin adapter over the shared <see cref="SqlConnectionFactory"/> (the same
/// factory the Agent/Corporate/Books modules use, bound to
/// ConnectionStrings:AgentSyncDb). Both the original InvoiceSync project and
/// this project point at the same physical database, so during the merge
/// this was consolidated onto a single connection string / factory instead
/// of opening a second connection string ("InvoiceSyncDb") and a second
/// pooled connection to the same server. See merge summary.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly SqlConnectionFactory _factory;

    public DbConnectionFactory(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await _factory.CreateOpenConnectionAsync(cancellationToken);
    }
}
