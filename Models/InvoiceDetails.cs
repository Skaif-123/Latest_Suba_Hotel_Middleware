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
        public string? Owner_Type { get; set; }
    }
}
