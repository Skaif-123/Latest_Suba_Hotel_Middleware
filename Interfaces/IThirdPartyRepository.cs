using System.Collections.Generic;
using System.Threading.Tasks;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces;

/// <summary>Pages rows out of ThirdPartyData for the agent and corporate sync tracks.</summary>
public interface IThirdPartyRepository
{
    Task<List<ThirdPartyDataRecord>> GetPageAsync(int offset, int pageSize);

    Task<List<ThirdPartyDataRecord>> GetPageAsyncCorporate(int offset, int pageSize);

    /// <summary>Writes Agent_Status back for one ThirdPartyData row (e.g. "Processed" or "Failed").</summary>
    Task UpdateAgentStatusAsync(string rowId, string status);

    /// <summary>Writes Corporate_Status back for one ThirdPartyData row (e.g. "Processed" or "Failed").</summary>
    Task UpdateCorporateStatusAsync(string rowId, string status);
}