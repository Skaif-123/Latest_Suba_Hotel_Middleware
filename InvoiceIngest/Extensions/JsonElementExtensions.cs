using System.Text.Json;

namespace AgentSyncConsole.InvoiceIngest.Extensions;

/// <summary>
/// Extension methods that reproduce JavaScript's forgiving property
/// access (optional chaining `?.`, missing-property-returns-undefined,
/// truthy/falsy string coercion via String(x || '')) on top of
/// System.Text.Json's JsonElement.
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Mirrors `obj?.prop` — returns null (not throw) if the element
    /// is not an object or the property is absent.
    /// </summary>
    public static JsonElement? TryGetPropertySafe(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }

    /// <summary>
    /// Mirrors `Array.isArray(x)` combined with a null-safe check —
    /// used everywhere the original does `Array.isArray(parsed.x.y)`.
    /// </summary>
    public static bool IsArray(this JsonElement? element)
    {
        return element is { ValueKind: JsonValueKind.Array };
    }

    /// <summary>
    /// Mirrors `String(value || '')` — treats JSON null, missing
    /// property, empty string, and non-existent element identically
    /// as JavaScript falsy, returning "".
    /// </summary>
    public static string AsJsString(this JsonElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var e = element.Value;

        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? string.Empty,
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Mirrors `parseFloat(entry.amount) || 0`.
    /// </summary>
    public static double AsJsNumber(this JsonElement? element)
    {
        if (element is null)
        {
            return 0d;
        }

        var e = element.Value;

        return e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(e.GetString(), out var d) => d,
            _ => 0d
        };
    }

    /// <summary>
    /// Mirrors iterating an array property that may be absent —
    /// returns an empty enumeration instead of throwing.
    /// </summary>
    public static IEnumerable<JsonElement> EnumerateArraySafe(this JsonElement? element)
    {
        if (element is { ValueKind: JsonValueKind.Array } e)
        {
            foreach (var item in e.EnumerateArray())
            {
                yield return item;
            }
        }
    }
}
