using System.Threading;
using System.Threading.Tasks;
using AgentSyncConsole.CustomerBooks.Models;

namespace AgentSyncConsole.CustomerBooks.Interfaces
{
    public interface ICustomerBooksSyncService
    {
        /// <summary>
        /// Runs a full sync over every Customer row, one page at a time, in a
        /// single loop. Returns once every page has been processed. Called
        /// by PipelineRunner between Corporate Sync and Invoice JSON -> SQL.
        /// </summary>
        Task<CustomerBooksSyncSummary> RunFullSyncAsync(CancellationToken ct = default);
    }
}
