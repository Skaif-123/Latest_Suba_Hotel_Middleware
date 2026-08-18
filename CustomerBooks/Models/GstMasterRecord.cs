namespace AgentSyncConsole.CustomerBooks.Models
{
    /// <summary>
    /// Maps directly to dbo.GST_Master via Dapper for this module's
    /// per-customer GST-registration lookup.
    ///
    /// NOTE ON NAMING: AgentSyncConsole.Models already has an unrelated
    /// "GSTMasterRecord" used by the Agent/Corporate sync's duplicate-check +
    /// bulk-insert path (IGSTMasterRepository.ExistsAsync/BulkInsertAsync).
    /// That type represents a queued insert row with different fields
    /// (IsDefault as a stringified bool, no numeric Id/BooksID pair used for
    /// PUT-vs-POST). This type instead represents an existing row read back
    /// by CustomerID with a real integer Id used for the taxinfo write-back,
    /// so it was kept as its own type in its own namespace rather than
    /// merged into the existing one.
    /// </summary>
    public class GstMasterRecord
    {
        public int Id { get; set; }
        public string CustomerID { get; set; } = string.Empty;
        public string? GST_No { get; set; }
        public string? Place_Of_Supply { get; set; }
        public string? Name { get; set; }
        public bool isDefault { get; set; }

        /// <summary>Zoho Books Tax Information ID once registered.</summary>
        public string? BooksID { get; set; }
    }
}
