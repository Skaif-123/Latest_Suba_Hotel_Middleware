namespace AgentSyncConsole.Models;

/// <summary>Maps 1:1 to the Catalyst "Customer" datastore table.</summary>
public class Customer
{
    public string? CustomerID { get; set; }
    public string? booksID { get; set; }
    public string? Place_Of_Supply { get; set; }
    public string? GST_No { get; internal set; }
}
