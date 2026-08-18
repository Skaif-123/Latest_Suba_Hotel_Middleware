using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models.jsonModels
{
    public class InvoiceAllocation
    {
        public string? invoice_id { get; set; }

        public decimal amount_applied { get; set; }
    }
}
