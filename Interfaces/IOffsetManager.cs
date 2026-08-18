using System.Threading.Tasks;

namespace AgentSyncConsole.Interfaces;

/// <summary>Persists/loads the resumable page offset for a named sync module in SyncOffset.</summary>
public interface IOffsetManager
{
    Task<int> LoadOffsetAsync();

    Task SaveOffsetAsync(int nextOffset, int currentOffsetForLog);
}
