using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
namespace AgentSyncConsole.Interfaces;

public interface IInvoiceLineItemRepository
{
    Task<IReadOnlyList<InvoiceLineItem>> GetByInvoiceIdAsync(string invoiceId, CancellationToken ct = default);
}
