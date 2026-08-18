using System.Threading.Tasks;
using System.Threading;
using System;


namespace AgentSyncConsole.Interfaces;

/// <summary>GST/IGST determination and Tax_Master GST_ID resolution.</summary>
public interface IGSTService
{
    /// <summary>GST if customer state code equals location state code, otherwise IGST.</summary>
    string DetermineGstType(string customerStateCode, string locationStateCode);

    /// <summary>
    /// Resolves the Tax_Master.GST_ID for the given gstType + taxRate, falling back to the
    /// 5% record for the same gstType, and throwing if neither exists (matches index.js exactly).
    /// </summary>
    Task<string> ResolveTaxIdAsync(string gstType, string taxRate, CancellationToken ct = default);
}
