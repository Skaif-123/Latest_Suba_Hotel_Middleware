using System.Threading.Tasks;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Repositories;

namespace AgentSyncConsole.Services
{
    /// <summary>
    /// Wraps the two duplicate-protection lookups used by the original code:
    ///   1) the live per-agent/per-corporate lookup (decides insert vs. update
    ///      while building the page)
    ///   2) the final lookup performed again immediately before the bulk
    ///      insert, in case a concurrent execution inserted the row meanwhile
    /// Both use the exact same query shape (CustomerID + Customer_Sub_Type),
    /// so this is a thin, explicit wrapper rather than duplicated SQL.
    /// </summary>
    public class DuplicateCheckService : IDuplicateCheckService
    {
        private readonly IAgentCorporateCustomerRepository _customerRepository;

        public DuplicateCheckService(IAgentCorporateCustomerRepository customerRepository)
        {
            _customerRepository = customerRepository;
        }

        public Task<CustomerLookupResult?> FindExistingAgentAsync(string customerId)
        {
            return _customerRepository.FindByCustomerIdAsync(customerId);
        }

        public Task<CustomerLookupResult?> FindExistingCorporateAsync(string customerId)
        {
            return _customerRepository.FindByCustomerIdCorporateAsync(customerId);
        }
    }
}
