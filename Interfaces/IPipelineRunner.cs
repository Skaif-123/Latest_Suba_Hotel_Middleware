using System.Threading;
using System.Threading.Tasks;

namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Runs the full merged pipeline in the required order:
///   Agent Sync -> Corporate Sync -> Customer Books Sync -> Invoice JSON -> SQL
///   -> Books Invoice Sync.
/// Customer Books Sync always runs after Agent/Corporate Sync and before
/// Invoice JSON -> SQL, regardless of the Agent/Corporate outcome (independent
/// data set — same policy already applied between Agent/Corporate and
/// Invoice JSON -> SQL). Books Invoice Sync only runs if the Invoice JSON ->
/// SQL phase completed without an "error" status on any page.
/// </summary>
public interface IPipelineRunner
{
    Task<int> RunAsync(CancellationToken ct = default);
}
