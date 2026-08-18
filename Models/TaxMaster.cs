namespace AgentSyncConsole.Models;

/// <summary>Maps 1:1 to the Catalyst "Tax_Master" datastore table.</summary>
public class TaxMaster
{
    public string? GST_Type { get; set; }
    public decimal Rate { get; set; }
    public string? GST_ID { get; set; }
}
