using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models
{
    public class PaymentInvoice
    {
       
        // SQL SELECT column mappings
        public string? InvoiceNumber { get; set; }  // I.InvoiceNumber
        public string? BooksInvoiceID { get; set; }  // I.BooksInvoiceID
        public string? InvoiceID { get; set; }       // IL.InvoiceID
        public string? TransactionID { get; set; }   // IL.TransactionID
        public string? Transaction_ID { get; set; }  // T.Transaction_ID
        public string? TransactionAmount { get; set; } // T.Amount
        public string? Tax_value { get; set; }       // T.Tax_value
        public string? TransactionRate { get; set; } // T.Rate
    }
}