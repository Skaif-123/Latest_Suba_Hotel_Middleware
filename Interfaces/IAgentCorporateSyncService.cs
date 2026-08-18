using System.Threading;
using System.Threading.Tasks;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Orchestrates the full Agent + Corporate sync run: load Place_Of_Supply,
/// load the resumable offset, page through ThirdPartyData, process each page
/// through the agent and corporate tracks, save the offset after every page,
/// and build the final run summary. This is the .NET 8 / DI replacement for
/// the logic that used to live directly in Program.Main.
/// </summary>
public interface IAgentCorporateSyncService
{
    Task<SyncSummary> RunAsync(CancellationToken ct = default);
}
