namespace AgentSyncConsole.Models;

/// <summary>Execution summary, mirrors the "success" basicIO.write payload in index.js.</summary>
public class InvoiceSyncSummary
{
    public string Status { get; set; } = "success";
    public AccessTokenRecord? LatestTokenRow { get; set; }
    public int TotalCreated { get; set; }
    public int TotalUpdated { get; set; }
    public int TotalSkipped { get; set; }
    public List<InvoiceSyncRecord> CreatedInvoices { get; set; } = new();
    public List<InvoiceSyncRecord> UpdatedInvoices { get; set; } = new();
    public List<object> SkippedInvoices { get; set; } = new();
}

public class InvoiceSyncRecord
{
    public string? InvoiceID { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? BooksInvoiceID { get; set; }
    public object? Response { get; set; }
}
