using AgentSyncConsole.Models;
using System.Threading;
namespace AgentSyncConsole.Interfaces;

using System.Threading.Tasks;
using System.Threading;

public interface IAccessTokenRepository
{
    Task<AccessTokenRecord?> GetLatestAsync(string application, CancellationToken ct = default);
    Task SaveAsync(AccessTokenRecord token, CancellationToken ct = default);
}
