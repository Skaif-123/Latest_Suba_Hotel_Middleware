namespace AgentSyncConsole.InvoiceIngest.Constants;

/// <summary>
/// Static constants matching the original JavaScript top-level
/// constants exactly. Runtime-configurable values (PAGE_SIZE,
/// BATCH_SIZE, MAX_RUNTIME, IN_CHUNK) are ALSO available via
/// SyncSettings (appsettings.json) — these consts document the
/// original hard-coded defaults and are used as fallback values.
/// </summary>
public static class SyncConstants
{
    // ---- Runtime safety ----
    public const int MaxRuntimeMs = 240000; // MAX_RUNTIME — 240 seconds

    // ---- Sizing ----
    public const int PageSize = 300;   // PAGE_SIZE
    public const int BatchSize = 10;   // BATCH_SIZE
    public const int InChunk = 100;    // IN_CHUNK — max IDs per SQL IN(...) clause

    // ---- ThirdPartyData status values ----
    public const string StatusProcessed = "Processed";
    public const string StatusFailed = "Failed";

    // ---- ThirdPartyData response messages ----
    public const string ResponseInvoiceSyncCompleted = "Invoice Sync Completed";
    public const string ResponseUnknownError = "Unknown error";

    // ---- Books_Status / ThirdParty_status values (per header comment) ----
    public const string BooksStatusSuccess = "Success";

    // ---- Line item key modes ----
    public const string KeyModeTransId = "transId";
    public const string KeyModeComposite = "composite";

    // ---- Credit note detection ----
    public const string CreditNotePrefix = "CN";
    public const string CreditNoteContains = "CREDIT";
    public const string CreditNoteLineItemTitleMarker = "CREDIT NOTE";

    // ---- Default / empty invoice-field sentinels ----
    public const string EmptyObjectLiteral = "{}";
    public const string EmptyArrayLiteral = "[]";
    public const string NullLiteral = "null";

    // ---- Pending sentinel used to prevent duplicate inserts within one execution ----
    public const string PendingSentinel = "pending";

    // ---- Final summary status ----
    public const string SummaryStatusSuccess = "success";
    public const string SummaryStatusError = "error";
}
