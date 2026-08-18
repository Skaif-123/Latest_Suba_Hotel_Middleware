namespace AgentSyncConsole.Models;

/// <summary>Maps 1:1 to the Catalyst "Invoice_LineItem" datastore table.</summary>
public class InvoiceLineItem
{
    public string? InvoiceID { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? HSN_SAC_Code { get; set; }
    public string? Quality { get; set; }
    public string? Rate { get; set; }
    public string? Tax_Rate { get; set; }
    public string? Amount { get; set; }
}
