using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace AgentSyncConsole.Models.jsonModels
{

    public class ZohoPaymentResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("payment")]
        public PaymentData1? Payment { get; set; }
    }

    public class PaymentData1    {
        [JsonPropertyName("payment_id")]
        public string? PaymentId { get; set; }
    }
}
