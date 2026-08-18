using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.DTOs
{

    public sealed class PosInvoiceSyncSummary
    {
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }

        public int ProcessedRows { get; set; }
        public int TotalInvoicesInserted { get; set; }
        public int TotalInvoicesUpdated { get; set; }
        public int TotalLineItemsInserted { get; set; }
        public int TotalLineItemsUpdated { get; set; }
        public int ProcessedThirdPartyRows { get; set; }
        public int FailedThirdPartyRows { get; set; }
    }

    /// <summary>Mirrors AgentSyncConsole.InvoiceIngest.DTOs.RowContribution, scoped to POS Invoices.</summary>
    internal sealed class PosRowContribution
    {
        public HashSet<string> InvoiceIds { get; } = new();
        public HashSet<string> LineItemKeys { get; } = new();
    }

    /// <summary>Mirrors AgentSyncConsole.InvoiceIngest.DTOs.ThirdPartyRowOutcome, scoped to POS Invoices.</summary>
    internal sealed class PosThirdPartyRowOutcome
    {
        public required int ROWID { get; init; }
        public string? Error { get; init; }
    }

}
