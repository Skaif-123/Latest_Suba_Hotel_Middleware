using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Converted 1:1 from the Catalyst "CatalystToBooksInvoices" function (index.js).
/// Preserves every business rule, validation, SQL update, API interaction, log
/// entry, retry mechanism, and execution summary from the original.
/// </summary>
public interface IBooksInvoiceSyncService
{
    Task<InvoiceSyncSummary> RunAsync(CancellationToken ct = default);
}
