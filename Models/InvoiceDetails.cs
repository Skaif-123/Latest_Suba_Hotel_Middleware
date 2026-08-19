using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models
{
    public class InvoiceDetails
    {
        public string? InvoiceID { get; set; }
        public string? Reservation_ID { get; set; }

        public string? Customer_Name { get; set; }

        public string? BooksInvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }

        public string? Transaction_ID { get; set; } = null;
        public string? Tax_value { get; set; } = null;
        public string? TransactionAmount { get; set; } = null;
        public string? TransactionRate { get; set; } = null;
        public string? Owner_Type { get; set; }
    }
}
