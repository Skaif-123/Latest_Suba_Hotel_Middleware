using System.Threading.Tasks;
using AgentSyncConsole.Models;
using AgentSyncConsole.Repositories;

namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Customer table access used by the Agent/Corporate sync (AgentSyncService /
/// CorporateSyncService). Kept separate from <see cref="ICustomerRepository"/>,
/// which is a different contract already used by BooksInvoiceSyncService
/// against the same Customer table for a different purpose (booksID /
/// Place_Of_Supply lookup by CustomerID only). Splitting these avoids forcing
/// one repository class to satisfy two unrelated shapes.
/// </summary>
public interface IAgentCorporateCustomerRepository
{
    Task<CustomerLookupResult?> FindByCustomerIdAsync(string customerId);

    Task<CustomerLookupResult?> FindByCustomerIdCorporateAsync(string customerId);

    Task<bool> ExistsAsync(string customerId);

    Task InsertCustomerAsync(CustomerRecord row);

    Task UpdateCustomerAsync(CustomerRecord row);
}
