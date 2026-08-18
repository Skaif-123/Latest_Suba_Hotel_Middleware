using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models.PosInoviceModel
{

    /// <summary>
    /// Maps 1:1 to the existing SQL Server "PosInvoice" table. Field-for-field the
    /// same style as AgentSyncConsole.InvoiceIngest.Models.Invoice (the Hotelogix
    /// Invoice header table) — no new table was created, only this model was added
    /// on top of the existing schema.
    ///
    /// Columns Hotel_ID / Payment_Term / BooksInvoiceID / Books_Status / Response
    /// are not part of the JSON mapping table supplied for POS Invoice, but exist
    /// on PosInvoice the same way their equivalents exist on Invoice, and are
    /// populated using exactly the same logic as the Invoice module:
    ///   - Hotel_ID       -> read once per ThirdPartyData row from the same
    ///                       hotelogix.response(.data).hotelId/hotelID field
    ///                       Invoice uses, so Location_Master lookups work
    ///                       identically for POS Invoices.
    ///   - Payment_Term   -> the first payments[] entry's title, exactly like
    ///                       Invoice.Payment_Term = firstPayment?.title.
    ///   - BooksInvoiceID / Books_Status / Response -> written by the SQL -> Books
    ///                       stage, same columns/semantics as Invoice.
    /// </summary>
    public class PosInvoice
    {
        public int ROWID { get; set; }

        // ---- JSON -> SQL mapping (per POS Invoice spec) ----
        public string Invoice_ID { get; set; } = string.Empty;      // id
        public string Invoice_Number { get; set; } = string.Empty;  // invoiceNumber
        public string Invoice_No { get; set; } = string.Empty;      // folioNo
        public string posPointId { get; set; } = string.Empty;      // posPointId
        public string posPointName { get; set; } = string.Empty;    // posPointName
        public string Invoice_status { get; set; } = string.Empty;  // status
        public string Owner_Type { get; set; } = string.Empty;      // ownerType
        public string GSTin_ID { get; set; } = string.Empty;        // gstinId
        public double Subtotal { get; set; }     // subtotal
        public double Tax { get; set; }           // tax
        public double NetTotal { get; set; }                        // netTotal
        public double Discount { get; set; }                       // discount
        public string CreatedOn { get; set; } = string.Empty;       // createdOn
        public string SettledOn { get; set; } = string.Empty;       // settledOn
        public string IsComplimentary { get; set; } = string.Empty; // isComplimentary
        public string IsRefund { get; set; } = string.Empty;        // isRefund
        public string GuestID { get; set; } = string.Empty;         // guestId
        public string InvoiceType { get; set; } = string.Empty;     // invoiceType

        // ---- Additional columns, populated the same way Invoice populates them ----
        public string HotelID { get; set; } = string.Empty;
        public string Payment_Term { get; set; } = string.Empty;
        public string PaymentMode { get; set; } = string.Empty;

        // ---- Books sync tracking (written by PosInvoiceBooksSyncService) ----
        public string? BooksInvoiceID { get; set; }
        public string? Books_Status { get; set; }
        public string? Response { get; set; }
    }
}
