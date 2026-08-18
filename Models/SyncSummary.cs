using System.Collections.Generic;

namespace AgentSyncConsole.Models
{
    /// <summary>
    /// Mirrors the "summary" object built at the end of the original Job
    /// Function (both the success/partial_success path and the FATAL ERROR
    /// path's inline summary object).
    /// </summary>
    public class SyncSummary
    {
        public string Status { get; set; } = "completed";
        public int TotalRowsScanned { get; set; }
        public int TotalAgentsFound { get; set; }
        public int TotalCorporatesFound { get; set; }
        public int TotalInserted { get; set; }
        public int TotalUpdated { get; set; }
        public int TotalFailed { get; set; }
        public int TotalSkipped { get; set; }
        public List<FailedRecord> FailedRecords { get; set; } = new List<FailedRecord>();
        public long ExecutionTime { get; set; }
        public int? NextOffset { get; set; }
        public bool HasMore { get; set; }
    }
}
