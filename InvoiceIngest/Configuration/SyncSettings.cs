namespace AgentSyncConsole.InvoiceIngest.Configuration;

/// <summary>
/// Binds the "SyncSettings" section of appsettings.json.
/// Mirrors the top-of-file runtime constants from the original
/// Catalyst function (START_TIME / MAX_RUNTIME / PAGE_SIZE / BATCH_SIZE / IN_CHUNK).
/// </summary>
public sealed class SyncSettings
{
    public const string SectionName = "SyncSettings";

    /// <summary>MAX_RUNTIME in original — 240 seconds default.</summary>
    public int MaxRuntimeMs { get; set; } = 240000;

    /// <summary>PAGE_SIZE in original.</summary>
    public int PageSize { get; set; } = 300;

    /// <summary>BATCH_SIZE in original.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>IN_CHUNK in original — max IDs per SQL IN(...) clause.</summary>
    public int InChunk { get; set; } = 100;
}
