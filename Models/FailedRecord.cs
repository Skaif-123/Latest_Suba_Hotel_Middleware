namespace AgentSyncConsole.Models
{
    /// <summary>
    /// Exact field-for-field port of the object literal built inside
    /// recordFailure() in the original Job Function. JSON-shaped source
    /// fields (SourceAgentJSON / SourceThirdPartyJSON / CustomerPayload) are
    /// kept as serialized JSON strings since their original shape is
    /// dynamic/arbitrary.
    /// </summary>
    public class FailedRecord
    {
        public string CustomerID { get; set; } = "";
        public string ROWID { get; set; } = "";
        public string ThirdPartyROWID { get; set; } = "";
        public string HotelID { get; set; } = "";
        public string Agent_Name { get; set; } = "";
        public string Customer_Sub_Type { get; set; } = "Agent";
        public string Stage { get; set; } = "";
        public string Error { get; set; } = "";
        public string Stack { get; set; } = "";
        public string SourceAgentJSON { get; set; }

        public string SourceCorporateJSON { get; set; }
        public string SourceThirdPartyJSON { get; set; }
        public string CustomerPayload { get; set; }
    }
}
