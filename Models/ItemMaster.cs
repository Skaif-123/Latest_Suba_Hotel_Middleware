namespace AgentSyncConsole.Models;

/// <summary>Maps 1:1 to the Catalyst "Item_Master" datastore table.</summary>
public class ItemMaster
{
    public string? Product_Name { get; set; }
    public string? Categories { get; set; }
    public string? COA { get; set; }
    public string? Amount { get; set; }
    public string? GST { get; set; }
    public string? HSN_Or_SAC { get; set; }
    public string? BooksID { get; set; }
    public string? TaxID { get; set; }
    public string? COA_Id { get; set; }


}
