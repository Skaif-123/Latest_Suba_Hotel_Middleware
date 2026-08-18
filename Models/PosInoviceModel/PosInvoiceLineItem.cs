using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models.PosInoviceModel
{
    public class PosInvoiceLineItem
    {
        public int ROWID { get; set; }

        // ---- JSON -> SQL mapping (per POS Invoice spec) ----
        public string Invoice_ID { get; set; } = string.Empty;   // posInvoice.id
        public string Product_Name { get; set; } = string.Empty; // productName
        public string hsnCode { get; set; } = string.Empty;      // hsnCode
        public string Quantity { get; set; } = string.Empty;     // quantity
        public double Unit_Price { get; set; }              // unitPrice
        public double Total_Price { get; set; }            // totalPrice
        public double TaxValue { get; set; }    // tax
        public double NetTotal { get; set; }      // netTotal


        // ---- Derived from lineItems[].taxBreakup[], same role Tax_Rate/HSN_SAC_Code
        //      play on Invoice_LineItem ----
        /// <summary>Combined GST percentage for this line (CGST% + SGST%, or IGST%).</summary>
        public decimal Tax_Rate { get; set; }

        /// <summary>"GST" (intra-state, CGST+SGST) or "IGST" (inter-state) — same values IGSTService.DetermineGstType/Constants.GstTypeGst/GstTypeIgst already use.</summary>
        public string GST_Type { get; set; } = string.Empty;
    }
}
