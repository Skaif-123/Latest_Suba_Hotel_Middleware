namespace AgentSyncConsole.Models;

/// <summary>
/// Maps to the Catalyst "accesToken" datastore table (name preserved verbatim,
/// including the original typo, to match the source system's schema).
/// </summary>
public class AccessTokenRecord
{
    public int ROWID { get; set; }
    public string? application { get; set; }
    public string? accessToken { get; set; }
    
    public string? refreshToken { get; set; }
    public DateTime? expiresAt { get; set; }
    public DateTime CREATEDTIME { get; set; }
    public DateTime? MODIFIEDTIME { get; set; }
}
