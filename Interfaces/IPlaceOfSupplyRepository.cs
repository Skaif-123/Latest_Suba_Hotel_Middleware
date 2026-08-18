using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentSyncConsole.Interfaces;

/// <summary>Loads the full Place_Of_Supply (Code -> Place_Of_Supply) map once per run.</summary>
public interface IPlaceOfSupplyRepository
{
    Task<Dictionary<string, string>> LoadAllAsync();
}
