using System.Threading;
using System.Threading.Tasks;
using Dapper;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Repositories
{
    /// <summary>
    /// Books-flavor Customer lookup: CustomerID -&gt; booksID / Place_Of_Supply.
    /// Deliberately separate from the Agent/Corporate <see cref="CustomerRepository"/>
    /// (which implements <see cref="IAgentCorporateCustomerRepository"/>) — same
    /// table, different projection/purpose. Previously this dependency of
    /// BooksInvoiceSyncService was left unregistered because Books Invoice
    /// Sync wasn't wired into Main; now that it runs as step 4 of the merged
    /// pipeline, this had to be implemented for real.
    /// </summary>
    public class CustomerRepositoryBooks : ICustomerRepository
    {
        private readonly SqlConnectionFactory _factory;

        public CustomerRepositoryBooks(SqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<Customer?> FindByCustomerIdAsync(string customerId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT TOP 1 CustomerID, booksID, Place_Of_Supply, GST_Treatment
                FROM Customer
                WHERE CustomerID = @CustomerID";

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<Customer>(
                new CommandDefinition(sql, new { CustomerID = customerId }, cancellationToken: ct));
        }
    }
}
