using System.Collections.Generic;
using System.Threading.Tasks;
using AgentSyncConsole.Models;
using AgentSyncConsole.Services;

namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Processes one page of ThirdPartyData.corporates rows: extract corporates,
/// validate, collect GST_Master candidates, and insert/update the Customer
/// row for each corporate found. One call == one page, identical to the
/// original per-execution behaviour of the Catalyst Job Function's corporate
/// track.
/// </summary>
public interface ICorporateSyncService
{
    Task<PageProcessResultsCorporate> ProcessPageAsync(
        List<ThirdPartyDataRecord> pageRows,
        Dictionary<string, string> placeOfSupplyMap);
}
