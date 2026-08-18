using System.Threading.Tasks;
using AgentSyncConsole.Repositories;

namespace AgentSyncConsole.Interfaces;

/// <summary>Wraps the agent/corporate duplicate-protection lookups against Customer.</summary>
public interface IDuplicateCheckService
{
    Task<CustomerLookupResult?> FindExistingAgentAsync(string customerId);

    Task<CustomerLookupResult?> FindExistingCorporateAsync(string customerId);
}
