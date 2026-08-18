using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using System.Threading;
using System;


namespace AgentSyncConsole.Interfaces;

public interface IInvoiceRepository
{
    Task<IReadOnlyList<Invoice>> GetAllRowsAsync(CancellationToken ct = default);
    Task UpdateRowAsync(Invoice invoice, CancellationToken ct = default);
}
