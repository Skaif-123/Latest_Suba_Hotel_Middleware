using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Models.PosInoviceModel;

namespace AgentSyncConsole.Interfaces.PosInvoiceInterface
{

    /// <summary>
    /// Replaces the equivalent of AgentSyncConsole.InvoiceIngest.Interfaces.IInvoiceLineItemRepository
    /// (composite-key upsert map, since POS line items have no natural TransId column
    /// 
    /// 
    /// Invoice_LineItem.Hotelogix_Trans_ID) plus the Books-flavor
    /// InvoiceLineItemRepository.GetByInvoiceIdAsync used at the SQL -> Books stage.
    /// </summary>
    public interface IPosInvoiceLineItemRepository
    {
        /// <summary>
        /// Returns "InvoiceID_Product_Name_hsnCode_Total_Price" -> ROWID for the given
        /// invoice IDs, chunked exactly like InvoiceLineItemRepository.QueryLineItemMapAsync's
        /// composite-key fallback mode.
        /// </summary>
        Task<Dictionary<string, string>> QueryLineItemMapAsync(
            IReadOnlyCollection<string> invoiceIds, int inChunk, CancellationToken ct = default);

        Task<int> InsertRowsAsync(IReadOnlyList<PosInvoiceLineItem> rows, CancellationToken ct = default);

        Task<int> UpdateRowsAsync(IReadOnlyList<PosInvoiceLineItem> rows, CancellationToken ct = default);

        /// <summary>Mirrors the Books-flavor InvoiceLineItemRepository.GetByInvoiceIdAsync.</summary>
        Task<IReadOnlyList<PosInvoiceLineItem>> GetByInvoiceIdAsync(string invoiceId, CancellationToken ct = default);
    }

}
