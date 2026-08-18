using System.Text.Json;

namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors extractInvoices() return value: { invoices, hotelId }.
/// Invoices kept as raw JsonElement list since the downstream code
/// reads dynamic/optional properties off each invoice object
/// exactly like the original (data.id, data.ownerId, etc.).
/// </summary>
public sealed class InvoiceExtractionResult
{
    public List<JsonElement> Invoices { get; set; } = new();
    public string HotelId { get; set; } = string.Empty;
}
