using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Converted 1:1 from the Catalyst Hotelogix "Transaction Sync" function
/// (module.exports async (context, basicIO) => {...}). Preserves the resumable
/// batch architecture, runtime guard, page-specific lookups, duplicate
/// detection, insert/update routing, Tax_value calculation, and ThirdPartyData
/// status write-back exactly as in the original source.
/// </summary>
public interface ITransactionSyncService
{
    Task<TransactionSyncSummary> RunAsync(CancellationToken ct = default);
}
