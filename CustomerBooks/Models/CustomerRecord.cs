namespace AgentSyncConsole.CustomerBooks.Models
{
    /// <summary>
    /// Maps directly (by column name) to dbo.Customer via Dapper.
    ///
    /// NOTE ON NAMING: AgentSyncConsole.Models already has an unrelated
    /// "CustomerRecord" (the Agent/Corporate sync insert/update row shape)
    /// and a "Customer" DTO (the duplicate-check lookup shape used by
    /// AgentSyncService/CorporateSyncService). Those two types represent
    /// different data and different tables/usages, so per the merge rules
    /// this type was NOT merged into either of them — it lives in its own
    /// AgentSyncConsole.CustomerBooks.Models namespace instead, exactly the
    /// same isolation pattern already used for AgentSyncConsole.InvoiceIngest.
    ///
    /// ROW-KEY FIX: the real dbo.Customer table (confirmed against the
    /// actual schema) has a single row-key column, "ROWID", type BIGINT NOT
    /// NULL — there is no separate "Id" column. This class previously had
    /// both an "int Id" (never populated — no matching column, always 0)
    /// and a "string RowId" (populated, but the wrong CLR type for a
    /// bigint). Both were replaced with one property, "ROWID" (long), that
    /// matches the physical column name and type exactly, so Dapper maps it
    /// unambiguously on read and it round-trips cleanly as a SQL parameter
    /// on write-back.
    /// </summary>
    public class CustomerRecord
    {
        public int ROWID { get; set; }
        public string CustomerID { get; set; } = string.Empty;

        public string? Company_Name { get; set; }
        public string? First_Name { get; set; }
        public string? Last_Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }

        public string? Billing_City { get; set; }
        public string? Billing_State { get; set; }
        public string? Billing_Pincode { get; set; }
        public string? Billing_Country { get; set; }
        public string? Billing_Phone { get; set; }

        public string? Shipping_City { get; set; }
        public string? Shipping_State { get; set; }
        public string? Shipping_Pincode { get; set; }
        public string? Shipping_Country { get; set; }
        public string? Shipping_Phone { get; set; }

        public string? Customer_Sub_Type { get; set; }
        public string? GST_Treatment { get; set; }
        public string? GST_NO { get; set; }
        public string? Pan_No { get; set; }
        public string? Currency { get; set; }
        public string? Place_of_Supply { get; set; }
        public bool? Tax_Preference { get; set; }
        public string? Code { get; set; }

        // Result columns — the only three columns this module ever writes back.
        public string? booksID { get; set; }
        public string? Response { get; set; }
        public string? status { get; set; }
        public string? hotelID { get; set; }
    }
}