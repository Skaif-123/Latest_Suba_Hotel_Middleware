using System.Text.Json;

namespace AgentSyncConsole.Helpers;

/// <summary>Centralized JSON serialization matching the plain JSON.stringify/JSON.parse semantics used in index.js.</summary>
public static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static string Serialize(object? value) =>
        JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Best-effort parse: on failure returns null rather than throwing, mirroring the
    /// try/catch { resolve({ _rawBody: body }) } fallback in the Catalyst booksRequest helper.</summary>
    public static T? TryDeserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }
}
