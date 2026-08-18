using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
using System;


namespace AgentSyncConsole.Interfaces;

public interface ITaxMasterRepository
{
    Task<TaxMaster?> FindByTypeAndRateAsync(string gstType, string rate, CancellationToken ct = default);
}
