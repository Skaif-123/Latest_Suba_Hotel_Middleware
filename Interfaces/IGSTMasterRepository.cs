using System.Collections.Generic;
using System.Threading.Tasks;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces;

/// <summary>Data access for the GST_Master table (duplicate check + bulk insert).</summary>
public interface IGSTMasterRepository
{
    Task<bool> ExistsAsync(string customerId, string gstNo);

    Task BulkInsertAsync(List<GSTMasterRecord> rows);
}
