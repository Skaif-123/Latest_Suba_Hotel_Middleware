using System.Threading.Tasks;
using System.Threading;
using System;

using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces;

public interface IItemMasterRepository
{
    Task<ItemMaster?> FindByProductNameAsync(string productName, CancellationToken ct = default);

    Task<ItemMaster?> FindByProductNameAsyncFrontDesk(string productName, CancellationToken ct = default);
}
