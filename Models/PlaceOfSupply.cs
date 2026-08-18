namespace AgentSyncConsole.Models
{
    /// <summary>
    /// One row of the Place_Of_Supply table, loaded once at startup into a
    /// Dictionary&lt;string,string&gt; keyed by Code - exactly like the original
    /// placeOfSupplyMap built from SELECT Code, Place_Of_Supply FROM Place_Of_Supply.
    /// </summary>
    public class PlaceOfSupply
    {
        public string Code { get; set; } = "";
        public string Place_Of_Supply { get; set; } = "";
    }
}
