using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Models.PosInoviceModel;
using global::AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces.PosInvoiceInterface
{



    /// <summary>
    /// Replaces the equivalent of AgentSyncConsole.InvoiceIngest.Interfaces.IInvoiceRepository
    /// (JSON -> SQL upsert) merged with the partial-column update pattern used by
    /// AgentSyncConsole.Repositories.InvoiceRepository (SQL -> Books status writeback),
    /// scoped to the existing "PosInvoice" table.
    /// </summary>
    public interface IPosInvoiceRepository
    {
        /// <summary>Returns Invoice_ID -> ROWID for the given IDs, chunked exactly 
        /// 
        /// 
        /// InvoiceRepository.QueryInvoiceMapAsync.</summary>
        Task<Dictionary<string, string>> QueryInvoiceMapAsync(
            IReadOnlyCollection<string> invoiceIds, int inChunk, CancellationToken ct = default);

        /// <summary>Batch insert — mirrors InvoiceRepository.InsertRowsAsync.</summary>
        Task<int> InsertRowsAsync(IReadOnlyList<PosInvoice> rows, CancellationToken ct = default);

        /// <summary>Batch update (JSON -> SQL upsert path) — mirrors InvoiceRepository.UpdateRowsAsync.</summary>
        Task<int> UpdateRowsAsync(IReadOnlyList<PosInvoice> rows, CancellationToken ct = default);

        /// <summary>Loads every PosInvoice row — mirrors the Books-flavor InvoiceRepository.GetAllRowsAsync,
        /// used by PosInvoiceBooksSyncService to drive the SQL -> Books loop.</summary>
        Task<IReadOnlyList<PosInvoice>> GetAllRowsAsync(CancellationToken ct = default);

        /// <summary>Partial column update by ROWID — mirrors the Books-flavor InvoiceRepository.UpdateRowAsync (AddIfSet pattern).</summary>
        Task UpdateRowAsync(PosInvoice invoice, CancellationToken ct = default);
    }

}
