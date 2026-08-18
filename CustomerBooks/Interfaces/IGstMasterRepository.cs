using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentSyncConsole.CustomerBooks.Models;

namespace AgentSyncConsole.CustomerBooks.Interfaces
{
    /// <summary>
    /// NOTE: AgentSyncConsole.Interfaces already declares an unrelated
    /// IGSTMasterRepository (ExistsAsync/BulkInsertAsync duplicate-check +
    /// insert path used by Agent/Corporate sync). This interface serves a
    /// different purpose (per-customer read + single-row Books-ID write-back)
    /// against a different record shape, so it is kept separate.
    /// </summary>
    public interface IGstMasterRepository
    {
        /// <summary>Targeted per-customer lookup (WHERE CustomerID = @CustomerId), never a full-table scan.</summary>
        Task<IReadOnlyList<GstMasterRecord>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default);

        /// <summary>Writes the returned Zoho Books Tax Information ID back so re-runs PUT instead of duplicating.</summary>
        Task UpdateBooksIdAsync(int rowId, string booksId, CancellationToken ct = default);
    }
}
