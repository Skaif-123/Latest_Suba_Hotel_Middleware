namespace AgentSyncConsole.Helpers;

/// <summary>Date formatting/defaulting helpers used for Invoice_Date / Due_Date.</summary>
public static class DateHelper
{
    public static string OrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
