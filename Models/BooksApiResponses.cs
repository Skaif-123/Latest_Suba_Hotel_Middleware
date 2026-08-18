using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentSyncConsole.Models;

/// <summary>Generic Zoho Books envelope: {code, message, ...}. Extra fields land in Extra.</summary>
public class BooksApiResponse
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("contact")]
    public JsonElement? Contact { get; set; }

    [JsonPropertyName("item")]
    public JsonElement? Item { get; set; }

    [JsonPropertyName("invoice")]
    public BooksInvoiceResult? Invoice { get; set; }

    /// <summary>Raw JSON body, kept for logging/Response column persistence, same as index.js.</summary>
    [JsonIgnore]
    public string? RawBody { get; set; }
}

public class BooksInvoiceResult
{
    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}