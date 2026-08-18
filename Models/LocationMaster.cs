namespace AgentSyncConsole.Models;

/// <summary>Maps 1:1 to the Catalyst "Location_Master" datastore table.</summary>
public class LocationMaster
{
    public string? hotelID { get; set; }
    public string? locationID { get; set; }
    public string? locationName { get; set; }
    public string? stateCode { get; set; }
    public string? gstNo { get; set; }
}
