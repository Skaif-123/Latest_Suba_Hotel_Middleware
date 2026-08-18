using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace AgentSyncConsole.Interfaces;

public interface ICustomerRepository
{
    Task<Customer?> FindByCustomerIdAsync(string customerId, CancellationToken ct = default);
}
