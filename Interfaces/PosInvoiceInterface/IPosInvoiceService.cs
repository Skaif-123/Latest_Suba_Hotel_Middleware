using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.DTOs;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces.PosInvoiceInterface
{
   

    /// <summary>
    /// Flow 1 — Hotelogix JSON -> SQL. Replaces the equivalent of
    /// AgentSyncConsole.InvoiceIngest.Interfaces.IInvoiceSyncService, reading
    /// ThirdPartyData.posInvoice instead of ThirdPartyData.invoice. One call =
    /// one page, same single-page-per-invocation design as InvoiceSyncService.
    /// </summary>
    public interface IPosInvoiceService
    {
        Task<PosInvoiceSyncSummary> RunOnceAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Flow 2 — SQL -> Zoho Books. Replaces the equivalent of
    /// AgentSyncConsole.Interfaces.IBooksInvoiceSyncService, scoped to PosInvoice /
    /// Posinvoice_LIneItem and posted against the Cash Customer.
    /// </summary>
    public interface IPosInvoiceBooksSyncService
    {
        Task<InvoiceSyncSummary> RunAsync(CancellationToken ct = default);
    }

}
