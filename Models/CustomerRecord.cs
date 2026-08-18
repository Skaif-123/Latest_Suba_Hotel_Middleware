namespace AgentSyncConsole.Models
{
    /// <summary>
    /// Maps 1:1 to the "rowData" object built for the Customer table in the
    /// original Job Function (both the insert and update shape).
    /// </summary>
    public class CustomerRecord
    {
        // Only populated for updates (existingRow.ROWID / finalExisting.ROWID)
        public string ROWID { get; set; }

        public string hotelID { get; set; } = "";
        public string CustomerID { get; set; } = "";
        public string First_Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Last_Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Company_Name { get; set; } = "";
        public string Customer_Sub_Type { get; set; } = "Agent";
        public int Mobile { get; set; }
        public int Phone { get; set; }

        public string GST_Treatment { get; set; } = "business_gst";
        public string GST_NO { get; set; } = "";

        public string Place_Of_Supply { get; set; } = "";

        public string Billing_Country { get; set; } = "";
        public string Billing_State { get; set; } = "";
        public string Billing_City { get; set; } = "";
        public int Billing_Pincode { get; set; }

        public string Shipping_Country { get; set; } = "";
        public string Shipping_State { get; set; } = "";
        public string Shipping_City { get; set; } = "";
        public int Shipping_Pincode { get; set; }

        public string Status { get; set; } = "processed";

        // JSON.stringify({ status: "CREATED"/"UPDATED", message: "..." })
        public string Response { get; set; } = "";
    }
}
