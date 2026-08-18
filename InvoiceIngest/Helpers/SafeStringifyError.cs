namespace AgentSyncConsole.InvoiceIngest.Helpers;

/// <summary>
/// Mirrors JavaScript's `err.toString()` used consistently in the
/// original for logging and for writing error text into the
/// ThirdPartyData.response column.
/// </summary>
public static class SafeStringifyError
{
    public static string ToJsString(this Exception ex)
    {
        // JS Error.toString() => "<Name>: <Message>"
        return $"{ex.GetType().Name}: {ex.Message}";
    }
}
