using System.Text.Json.Serialization;

namespace AgentSyncConsole.Models;

/// <summary>Outbound payload sent to POST/PUT /books/v3/invoices, mirrors payloadObj in index.js.</summary>
public class BooksInvoicePayload
{
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = "";

    [JsonPropertyName("location_id")]
    public string LocationId { get; set; } = "";

    [JsonPropertyName("invoice_number")]
    public string InvoiceNumber { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("due_date")]
    public string DueDate { get; set; } = "";

    [JsonPropertyName("payment_terms_label")]
    public string PaymentTermsLabel { get; set; } = "";

    [JsonPropertyName("place_of_supply")]
    public string PlaceOfSupply { get; set; } = "";

    [JsonPropertyName("gst_treatment")]
    public string GstTreatment { get; set; } = "business_none";

    [JsonPropertyName("line_items")]
    public List<BooksLineItem> LineItems { get; set; } = new();

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "Thanks for your business.";

    [JsonPropertyName("terms")]
    public string Terms { get; set; } = "Payment due immediately.";

    [JsonPropertyName("reason")]
    public string reason { get; set; } = "Updating Invoice Details";
}

public class BooksLineItem
{
    internal string tax_type;
    internal string tax_exemption_id;

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("rate")]
    public string Rate { get; set; }

    [JsonPropertyName("quantity")]
    public string Quantity { get; set; }

    [JsonPropertyName("tax_id")]
    public string? TaxId { get; set; }

    [JsonPropertyName("hsn_or_sac")]
    public string? HsnOrSac { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "nos";

    [JsonPropertyName("account_id")]
    public string account_id { get; set; } = "";

    [JsonPropertyName("gst_treatment_code")]
    public string GstTreatmentCode { get; set; } = "";


}
