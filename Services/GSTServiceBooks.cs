using System;
using System.Threading;
using System.Threading.Tasks;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Utilites;

namespace AgentSyncConsole.Services
{
    /// <summary>
    /// Implements IGSTService. Deliberately separate from the static
    /// AgentSyncConsole.Services.GSTService helper class (Agent/Corporate
    /// GST_Master candidate building) — different responsibility, same
    /// general "GST" naming. Previously this dependency of
    /// BooksInvoiceSyncService was left unregistered because Books Invoice
    /// Sync wasn't wired into Main; now that it runs as step 4 of the merged
    /// pipeline, this had to be implemented for real.
    /// </summary>
    public class GSTServiceBooks : IGSTService
    {
        private readonly ITaxMasterRepository _taxMasterRepository;

        public GSTServiceBooks(ITaxMasterRepository taxMasterRepository)
        {
            _taxMasterRepository = taxMasterRepository;
        }

        public string DetermineGstType(string customerStateCode, string locationStateCode)
        {
            return string.Equals(customerStateCode, locationStateCode, StringComparison.OrdinalIgnoreCase)
                ? Constants.GstTypeGst
                : Constants.GstTypeIgst;
        }

        public async Task<string> ResolveTaxIdAsync(string gstType, string taxRate, CancellationToken ct = default)
        {
            var match = await _taxMasterRepository.FindByTypeAndRateAsync(gstType, taxRate, ct);

            if (match is not null && !string.IsNullOrEmpty(match.GST_ID))
            {
                return match.GST_ID!;
            }

            // Fallback: same gstType, 5% rate — matches the original index.js behavior exactly.
            var fallback = await _taxMasterRepository.FindByTypeAndRateAsync(gstType, "5", ct);

            if (fallback is not null && !string.IsNullOrEmpty(fallback.GST_ID))
            {
                return fallback.GST_ID!;
            }

            throw new InvalidOperationException(
                $"No Tax_Master GST_ID found for gstType={gstType}, taxRate={taxRate} (and no 5% fallback for this gstType).");
        }
    }
}

