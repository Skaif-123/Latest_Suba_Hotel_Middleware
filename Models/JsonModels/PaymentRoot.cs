using System.Text.Json.Serialization;
using AgentSyncConsole.Helpers;

namespace AgentSyncConsole.Models.jsonModels
{
    public class PaymentRoot
    {
        [JsonPropertyName("hotelogix")]
        public Hotelogix Hotelogix { get; set; } = new();
    }

    public class Hotelogix
    {
        [JsonPropertyName("msgId")]
        public string? MsgId { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("datetime")]
        public string? DateTime { get; set; }

        [JsonPropertyName("response")]
        public ResponseData Response { get; set; } = new();

        [JsonPropertyName("request")]
        public RequestData Request { get; set; } = new();
    }

    public class ResponseData
    {
        [JsonPropertyName("status")]
        public StatusData Status { get; set; } = new();

        [JsonPropertyName("hotelId")]
        public int HotelId { get; set; }

        [JsonPropertyName("data")]
        public PaymentData Data { get; set; } = new();
    }

    public class StatusData
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public class PaymentData
    {

        [JsonPropertyName("rsvId")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string? RsvId { get; set; }

        [JsonPropertyName("groupId")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string? GroupId { get; set; }

        [JsonPropertyName("payments")]
        public List<Payment> Payments { get; set; } = new();
    }

    public class Payment
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("rsvId")]
        public string? RsvId { get; set; }

        [JsonPropertyName("groupId")]
        public string? GroupId { get; set; }

        [JsonPropertyName("payTypeId")]
        public string? PayTypeId { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("details")]
        public string? Details { get; set; }

        [JsonPropertyName("chequeNo")]
        public string? ChequeNo { get; set; }

        [JsonPropertyName("receipt")]
        public string? Receipt { get; set; }

        [JsonPropertyName("rsvOrGrpNo")]
        public string? RsvOrGrpNo { get; set; }

        [JsonPropertyName("paymentMode")]
        public string? PaymentMode { get; set; }

        [JsonPropertyName("transactionNumber")]
        public string? TransactionNumber { get; set; }

        [JsonPropertyName("cardLastFour")]
        public string? CardLastFour { get; set; }
    }

    public class RequestData
    {
        [JsonPropertyName("hotelId")]
        public string? HotelId { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("extraDataVal1")]
        public string? ExtraDataVal1 { get; set; }

        [JsonPropertyName("extraDataVal2")]
        public string? ExtraDataVal2 { get; set; }

        [JsonPropertyName("methodName")]
        public string? MethodName { get; set; }

        [JsonPropertyName("optForDbArr")]
        public List<string> OptForDbArr { get; set; } = new();
    }
}