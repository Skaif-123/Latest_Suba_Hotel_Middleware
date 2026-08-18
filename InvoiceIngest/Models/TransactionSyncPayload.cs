using System.Text.Json.Serialization;

namespace AgentSyncConsole.Models
{
    /// <summary>
    /// Strongly typed shape for a single entry in transactions[]/taxBreakup[],
    /// used once TransactionSyncService has located the array with the loose
    /// JsonElement navigation in ExtractTransactions (which mirrors the two
    /// payload shapes handled by extractTransactions() in the original Catalyst
    /// index.js: "response.data.transactions" and "hotelogix.response.data.transactions").
    /// New models — no equivalent existed in this project (PaymentRoot.cs covers
    /// the payments[] shape only).
    /// </summary>
    public class TransactionSyncItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("rsvId")]
        public string? RsvId { get; set; }

        [JsonPropertyName("hsnCode")]
        public string? HsnCode { get; set; }

        [JsonPropertyName("prodName")]
        public string? ProdName { get; set; }

        [JsonPropertyName("priceBfDisc")]
        public string? PriceBfDisc { get; set; }

        [JsonPropertyName("netTotal")]
        public string? NetTotal { get; set; }

        [JsonPropertyName("taxBreakup")]
        public List<TransactionTaxBreakupItem>? TaxBreakup { get; set; }
    }

    public class TransactionTaxBreakupItem
    {
        [JsonPropertyName("amount")]
        public string? Amount { get; set; }
    }
}
