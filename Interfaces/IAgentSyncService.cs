using System.Collections.Generic;
using System.Threading.Tasks;
using AgentSyncConsole.Models;
using AgentSyncConsole.Services;

namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Processes one page of ThirdPartyData.agent rows: extract agents, validate,
/// collect GST_Master candidates, and insert/update the Customer row for each
/// agent found. One call == one page, identical to the original per-execution
/// behaviour of the Catalyst Job Function's agent track.
/// </summary>
public interface IAgentSyncService
{
    Task<PageProcessResult> ProcessPageAsync(
        List<ThirdPartyDataRecord> pageRows,
        Dictionary<string, string> placeOfSupplyMap);
}
