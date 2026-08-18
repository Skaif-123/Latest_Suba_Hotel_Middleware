using System.Collections.Generic;
using System.Linq;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Services
{
    /// <summary>
    /// Preserves BOTH GST tracks from the original code, completely
    /// decoupled from each other exactly as before:
    ///
    /// 1) SelectActiveGstin - the ONLY logic used for Customer.GST_NO and,
    ///    historically, Place Of Supply. Picks isDefault === true, else the
    ///    first element, else null/empty. Untouched by GST_Master collection.
    ///
    /// 2) BuildGstMasterCandidates - additive collection: every entry of
    ///    gstinDetails (not just the active/default one) becomes one
    ///    GST_Master candidate row. Place_Of_Supply for GST_Master is looked
    ///    up independently, from the first two characters of the GSTIN
    ///    (state code) against the placeOfSupplyMap loaded once at startup;
    ///    an unmatched code falls back to empty string.
    /// </summary>
    public static class GSTService
    {
        public static GstinDetail SelectActiveGstin(List<GstinDetail> gstinDetails)
        {
            if (gstinDetails == null || gstinDetails.Count == 0)
            {
                return null;
            }

            var defaultGstin = gstinDetails.FirstOrDefault(g => g.IsDefault == true);

            return defaultGstin ?? gstinDetails[0];
        }

        public static List<GSTMasterRecord> BuildGstMasterCandidates(
            List<GstinDetail> gstinDetails,
            string customerId,
            Dictionary<string, string> placeOfSupplyMap)
        {
            var result = new List<GSTMasterRecord>();

            if (gstinDetails == null)
            {
                return result;
            }

            foreach (var gst in gstinDetails)
            {
                if (gst == null || string.IsNullOrEmpty(gst.Gstin))
                {
                    continue;
                }

                var gstStateCode = gst.Gstin.Length >= 2 ? gst.Gstin.Substring(0, 2) : gst.Gstin;

                var mappedPlaceOfSupply = "";
                if (placeOfSupplyMap != null && placeOfSupplyMap.TryGetValue(gstStateCode, out var pos))
                {
                    mappedPlaceOfSupply = pos ?? "";
                }

                result.Add(new GSTMasterRecord
                {
                    CustomerID = customerId,
                    GST_No = gst.Gstin,
                    Place_Of_Supply = mappedPlaceOfSupply,
                    Name = gst.Name ?? "",
                    IsDefault = gst.IsDefault == null ? "undefined" : (gst.IsDefault.Value ? "true" : "false"),
                    BooksID = ""
                });
            }

            return result;
        }
    }
}
