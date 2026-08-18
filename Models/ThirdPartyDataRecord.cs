namespace AgentSyncConsole.Models
{
    /// <summary>
    /// One row from ThirdPartyData. Matches the original ZCQL projection:
    /// SELECT ROWID, agent FROM ThirdPartyData WHERE agent IS NOT NULL ORDER BY ROWID.
    /// Agent_Status / Corporate_Status are written back after a row's agent(s)/
    /// corporate(s) have been processed, so already-processed rows are skipped
    /// on the next page fetch instead of being re-synced every run.
    /// </summary>
    public class ThirdPartyDataRecord
    {
        public string ROWID { get; set; }

        // Raw JSON string payload - parsed later exactly like JSON.parse(row.agent) was.
        public string agent { get; set; }
        public string corporates { get; set; }

        public string Agent_Status { get; set; }
        public string Corporate_Status { get; set; }
    }
}