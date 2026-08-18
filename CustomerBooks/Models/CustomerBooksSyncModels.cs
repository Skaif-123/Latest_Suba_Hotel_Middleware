namespace AgentSyncConsole.CustomerBooks.Models
{
    /// <summary>Outcome of syncing a single Customer row to Zoho Books.</summary>
    public class CustomerSyncResult
    {
        public string CustomerId { get; set; } = string.Empty;
        public int RowId { get; set; }
        public bool Success { get; set; }
        public string BooksId { get; set; } = string.Empty;
        public string Status { get; set; } = "Failed";
        public string ErrorMessage { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public bool WasCreate { get; set; }
    }

    /// <summary>
    /// Run summary for the Customer Books Sync module.
    ///
    /// Named "CustomerBooksSyncSummary" (rather than reusing the original
    /// CustomerBooksSync.Api name "SyncSummary") purely to keep log output
    /// and any future cross-module summary aggregation unambiguous — the
    /// solution already has AgentSyncConsole.Models.SyncSummary (Agent/
    /// Corporate) and AgentSyncConsole.Models.InvoiceSyncSummary (Books
    /// Invoice Sync). Same isolation rule applied here for consistency.
    /// </summary>
    public class CustomerBooksSyncSummary
    {
        public string Status { get; set; } = "success";
        public int TotalScanned { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public long ExecutionTimeMs { get; set; }
    }
}
