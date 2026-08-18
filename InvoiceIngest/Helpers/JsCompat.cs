using System.Text.Json;

namespace AgentSyncConsole.InvoiceIngest.Helpers;

/// <summary>
/// Centralizes small JavaScript-parity behaviors that appear
/// verbatim in the original function so every call site matches
/// exactly instead of re-implementing the quirk inline.
/// </summary>
public static class JsCompat
{
    /// <summary>
    /// Mirrors:
    ///   let parsed = JSON.parse(raw);
    ///   if (typeof parsed === 'string') { parsed = JSON.parse(parsed); }
    /// Some ThirdPartyData.invoice values are JSON-encoded twice
    /// (a JSON string containing another JSON string). This performs
    /// the same double-parse fallback.
    /// </summary>
    public static JsonElement ParseWithDoubleDecode(string raw)
    {
        using var firstDoc = JsonDocument.Parse(raw);
        var firstRoot = firstDoc.RootElement.Clone();

        if (firstRoot.ValueKind == JsonValueKind.String)
        {
            var inner = firstRoot.GetString() ?? string.Empty;
            using var secondDoc = JsonDocument.Parse(inner);
            return secondDoc.RootElement.Clone();
        }

        return firstRoot;
    }

    /// <summary>
    /// Mirrors `parseFloat(totalTax.toFixed(10))` — round to 10
    /// decimal places then parse back to a double, matching JS
    /// floating point display/rounding behavior as closely as
    /// double precision allows.
    /// </summary>
    public static double ToFixed10(double value)
    {
        return Math.Round(value, 10, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Mirrors the empty-invoice-field guard:
    ///   !raw || raw === '{}' || raw === '[]' || raw === 'null'
    /// </summary>
    public static bool IsEmptyInvoicePayload(string raw)
    {
        return string.IsNullOrEmpty(raw)
            || raw == Constants.SyncConstants.EmptyObjectLiteral
            || raw == Constants.SyncConstants.EmptyArrayLiteral
            || raw == Constants.SyncConstants.NullLiteral;
    }
}
