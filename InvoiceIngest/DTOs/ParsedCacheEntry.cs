using System.Text.Json;
using AgentSyncConsole.InvoiceIngest.Models;

namespace AgentSyncConsole.InvoiceIngest.DTOs;

/// <summary>
/// Mirrors one entry pushed into parsedCache during Pass 1:
///   { row, skip, skipReason?, invoices?, hotelId?, transactionMap }
/// Exactly one of the "skip" branch or the "success" branch is
/// populated per the original's two push sites.
/// </summary>
public sealed class ParsedCacheEntry
{
    public required ThirdPartyDataRow Row { get; init; }

    public bool Skip { get; init; }

    public string? SkipReason { get; init; }

    public List<JsonElement> Invoices { get; init; } = new();

    public string HotelId { get; init; } = string.Empty;

    /// <summary>Keyed by transaction.id, built once per row by buildTransactionMap().</summary>
    public Dictionary<string, TransactionLookup> TransactionMap { get; init; } = new();

    /// <summary>
    /// Date-only portion (YYYY-MM-DD) of hotelogix.datetime for this row,
    /// e.g. "2026-07-25T04:38:42" -> "2026-07-25". Computed once in
    /// ExtractInvoices() during Pass 1, read back during Pass 2 when
    /// building each Invoice row's Invoice_Date.
    /// </summary>
    public string InvoiceDate { get; init; } = string.Empty;
}