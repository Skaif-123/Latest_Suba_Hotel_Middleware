namespace AgentSyncConsole.CustomerBooks.Configuration
{
    /// <summary>
    /// Ported 1:1 from CustomerBooksSync.Api's Models/BooksApiOptions.cs.
    /// Bound to the "CustomerBooksApi" section of appsettings.json (kept
    /// separate from the existing "ZohoBooks" section used by the Invoice
    /// Sync module — same Zoho org, different sub-system, different tunables
    /// such as CodeCustomFieldId/PageSize that only this module needs).
    /// </summary>
    public class CustomerBooksSettings
    {
        public const string SectionName = "CustomerBooksApi";

        public string Hostname { get; set; } = "www.zohoapis.in";
        public string OrganizationId { get; set; } = "60059112783";
        public string CodeCustomFieldId { get; set; } = "3233228000000896531";
        public string HotelIDCustomField { get; set; } = "3233228000001216003";

        /// <summary>Rows fetched per page from dbo.Customer.</summary>
        public int PageSize { get; set; } = 300;

        public int RequestTimeoutMs { get; set; } = 15000;
        public int MaxRetries { get; set; } = 3;
    }
}
