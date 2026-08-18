namespace AgentSyncConsole.Models
{
    /// <summary>
    /// One row queued for the GST_Master DataStore table. Matches the object
    /// literal pushed into gstInsertRows for every gstinDetails[] entry (not
    /// just the default/active one used for Customer.GST_NO).
    /// </summary>
    public class GSTMasterRecord
    {
        public string CustomerID { get; set; } = "";
        public string GST_No { get; set; } = "";
        public string Place_Of_Supply { get; set; } = "";
        public string Name { get; set; } = "";

        // Original does `isDefault: String(gst.isDefault)` -> "true" / "false" / "undefined".
        public string IsDefault { get; set; } = "undefined";
        public string BooksID { get; set; } = "";
    }
}
