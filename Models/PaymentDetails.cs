using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models
{
    public class PaymentDetail
    {
        public string? Customer_Name { get; set; }

        public string? Location_Name { get; set; }

       
        public string Amount_Received { get; set; }

        public string? Payment_Date { get; set; }

        public string? Payment_No { get; set; }

        public string? Payment_Mode { get; set; }

        public string? Deposit_to { get; set; }

        public string? Tax_if_Applicable_COApaymentID { get; set; }

        public string? Hotel_ID { get; set; }

        public string? Books_ID { get; set; }

        public string? Books_Status { get; set; }

        public string? Details { get; set; }

        public string? Response { get; set; }
    }
}
