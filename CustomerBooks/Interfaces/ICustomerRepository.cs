using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentSyncConsole.CustomerBooks.Models;

namespace AgentSyncConsole.CustomerBooks.Interfaces
{
    /// <summary>
    /// NOTE: AgentSyncConsole.Interfaces already declares an unrelated
    /// ICustomerRepository (FindByCustomerIdAsync, used by the Agent/
    /// Corporate duplicate-check track). This is a different contract for a
    /// different table-shape/use-case (full-table pagination + batch
    /// write-back for Customer Books Sync), so it is namespaced separately
    /// rather than merged in, matching the InvoiceIngest isolation pattern
    /// already used elsewhere in this project.
    /// </summary>
    public interface ICustomerRepository
    {
        /// <summary>Fetches one page of Customer rows, ordered by ROWId, using OFFSET/FETCH.</summary>
        Task<IReadOnlyList<CustomerRecord>> GetPageAsync(int offset, int pageSize, CancellationToken ct = default);

        /// <summary>
        /// Batch-writes booksID/Response/status back to Customer. Falls back
        /// to per-row updates if the batch (transactional) update fails.
        /// </summary>
        Task UpdateResultsAsync(IEnumerable<CustomerSyncResult> results, CancellationToken ct = default);
    }
}
