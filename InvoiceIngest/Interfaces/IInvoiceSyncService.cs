using AgentSyncConsole.InvoiceIngest.DTOs;

namespace AgentSyncConsole.InvoiceIngest.Interfaces;

/// <summary>
/// Replaces the exported Catalyst function
///   module.exports = async (context, basicIO) => { ... }
/// One call = one execution/page, matching the original's
/// single-page-per-invocation design (Catalyst scheduled the
/// function repeatedly; Program.cs now drives the while(hasMore) loop).
/// </summary>
public interface IInvoiceSyncService
{
    Task<SyncSummaryResult> RunOnceAsync(CancellationToken cancellationToken = default);
    Task RunOnceAsync(object invoiceDate, CancellationToken ct);
}
