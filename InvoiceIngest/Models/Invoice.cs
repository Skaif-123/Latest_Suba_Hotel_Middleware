namespace AgentSyncConsole.InvoiceIngest.Models;

/// <summary>
/// Maps to the SQL Server "Invoice" table (was Catalyst datastore
/// table 'Invoice'). Columns match exactly what the original
/// function reads/writes: ROWID, InvoiceID, plus every field in
/// rowData built in Pass 2.
/// </summary>
public sealed class Invoice
{
    public int ROWID { get; set; }
    public string Hotel_ID { get; set; } = string.Empty;
    public string Customer_Name { get; set; } = string.Empty;
    public string Location_Name { get; set; } = string.Empty;
    public string Invoice_Number { get; set; } = string.Empty;
    public string Invoice_Date { get; set; } = string.Empty;
    public string Owner_Type { get; set; } = string.Empty;
    public string Payment_Term { get; set; } = string.Empty;
    public string Due_Date { get; set; } = string.Empty;
    public string InvoiceID { get; set; } = string.Empty;
    public string Reservation_ID { get; set; } = string.Empty;
}
