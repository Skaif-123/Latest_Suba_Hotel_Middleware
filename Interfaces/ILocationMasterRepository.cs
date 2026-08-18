using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
namespace AgentSyncConsole.Interfaces;

public interface ILocationMasterRepository
{
    Task<LocationMaster?> FindByHotelIdAsync(string hotelId, CancellationToken ct = default);
}
