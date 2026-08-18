namespace AgentSyncConsole.Models;

/// <summary>
/// Maps 1:1 to the Catalyst "Invoice" datastore table.
/// Column names are locked to match the original schema exactly.
/// </summary>
public class Invoice
{
    public long ROWID { get; set; }
    public string? InvoiceID { get; set; }
    public string? Customer_Name { get; set; }
    public string? Hotel_ID { get; set; }
    public string? Invoice_Number { get; set; }
    public string? Invoice_Date { get; set; }
    public string? Payment_Term { get; set; }
    public string? Due_Date { get; set; }
    public string? BooksInvoiceID { get; set; }
    public string? Books_Status { get; set; }
    public string? Response { get; set; }
    public string? ThirdParty_status { get; set; }
    public string? Location_Name { get; set; }
    public string? Owner_Type { get; set; }
}