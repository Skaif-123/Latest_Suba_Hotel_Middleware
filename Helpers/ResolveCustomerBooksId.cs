using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Helpers
{
    public static class PaymentModeCustomerResolver
    {
        /// <summary>
        /// Resolves the Zoho Books Customer ID based on the incoming POS payment mode.
        /// </summary>
        /// <param name="paymentMode">Payment mode code from POS JSON (e.g., CC, CASH, UPI, OTH)</param>
        /// <returns>Configured Zoho Books Customer ID</returns>
        public static string ResolveCustomerBooksId(
                   string? paymentMode,
                   string cashId,
                   string upiId,
                   string creditCardId)
        {
            if (string.IsNullOrWhiteSpace(paymentMode))
            {
                return cashId;
            }

            // Normalize to handle mixed casing or extra spaces safely
            var mode = paymentMode.Trim().ToUpperInvariant();

            return mode switch
            {
                "CC" => !string.IsNullOrEmpty(creditCardId) ? creditCardId : cashId,
                "CASH" => cashId,
                "UPI" or "OTH" => !string.IsNullOrEmpty(upiId) ? upiId : cashId,
                _ => cashId // Default fallback for TTC, TTR, BANK, CQ, etc.
            };
        }
    }
}


    

