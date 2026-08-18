namespace AgentSyncConsole.InvoiceIngest.Extensions;

/// <summary>
/// Small helpers mirroring exact JavaScript string operations used
/// in the original function, kept separate so call sites read the
/// same as the source (data.customFolioNo.replace(/ /g, ''), etc.).
/// </summary>
public static class StringExtensions
{
    /// <summary>Mirrors `str.replace(/ /g, '')` — strips ALL spaces, not just trim.</summary>
    public static string RemoveAllSpaces(this string value)
    {
        return value.Replace(" ", string.Empty);
    }

    /// <summary>
    /// Mirrors the escaping used when building ZCQL/SQL IN(...) lists:
    /// `String(id).replace(/'/g, "''")`.
    /// </summary>
    public static string EscapeSingleQuotes(this string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>Null/empty-safe trim mirroring `String(x || '').trim()`.</summary>
    public static string SafeTrim(this string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}
