using System.Diagnostics;
using System.Text.Json;
using AgentSyncConsole.InvoiceIngest.Configuration;
using AgentSyncConsole.InvoiceIngest.Constants;
using AgentSyncConsole.InvoiceIngest.DTOs;
using AgentSyncConsole.InvoiceIngest.Enums;
using AgentSyncConsole.InvoiceIngest.Extensions;
using AgentSyncConsole.InvoiceIngest.Helpers;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.InvoiceIngest.Models;
using AgentSyncConsole.InvoiceIngest.Utilities;
using AgentSyncConsole.Interfaces.TransactionInterface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSyncConsole.InvoiceIngest.Services;

/// <summary>
/// Direct line-by-line port of the original Catalyst function body.
/// One call to RunOnceAsync() == one function execution == one page.
///
/// =========================================================
/// RESUMABLE BATCH ARCHITECTURE
///
/// ThirdPartyData now has ThirdParty_status, Books_Status, and
/// Response columns. Each ThirdPartyData row is marked:
///
///   - Processed / Success / "Invoice Sync Completed"
///     after its invoice + line items are fully written.
///
///   - Failed / Failed / <actual error message>
///     if JSON parsing, building, or the downstream invoice /
///     line item write failed for that row.
///
/// The next execution only re-reads rows where
/// ThirdParty_status IS NULL OR ThirdParty_status != 'Processed'.
///
/// All invoice mapping, line item mapping, batching, and helper
/// logic is unchanged. runBatches() has one small, additive
/// change: it now also returns the list of items that ultimately
/// failed (after per-row fallback), so status tracking can trace
/// a failure back to the ThirdPartyData row that produced it.
/// Nothing about how batches are written was altered.
///
/// Tax_Rate / HSN_SAC_Code change: InvoiceSyncService no longer parses
/// transaction JSON at all. Transaction Sync now owns that entirely and
/// writes each transaction's Transaction_ID, Tax_Value, and HSN_Code into
/// TransactionModule (Tax_Value is already halved by Transaction Sync
/// before it is stored — InvoiceSyncService must never divide it again).
/// Each line item's transId is collected during Pass 1 and used to fetch
/// the matching TransactionModule rows once per page via
/// ITransactionRepository, keyed by Transaction_ID. During Pass 2, for
/// each line item, TransactionModule.Tax_Value is copied verbatim into
/// InvoiceLineItem.Tax_Rate and TransactionModule.HSN_Code is copied
/// verbatim into InvoiceLineItem.HSN_SAC_Code — no calculation, no
/// taxBreakup summation, no transaction JSON parsing. If no matching
/// TransactionModule row is found, both values default to empty/zero.
/// This is the ONLY behavioral change in this revision — matching logic,
/// batching, status tracking, and all other mappings are untouched.
///
/// Invoice_Date change: Invoice_Date is no longer read from
/// lineItems[0].date. It is now read once per ThirdPartyData row
/// from hotelogix.datetime (e.g. "2026-07-25T04:38:42"), truncated
/// to just the date portion ("2026-07-25") by splitting on 'T'.
/// This value is computed once in ExtractInvoices() during Pass 1,
/// cached on ParsedCacheEntry.InvoiceDate, and read back during
/// Pass 2 when building each Invoice row. No DateTime.Parse() is
/// used — this is a plain string split, matching the requirement
/// to preserve the literal date text from the source payload.
/// =========================================================
/// </summary>
public sealed class InvoiceSyncService : IInvoiceSyncService
{
    private readonly IThirdPartyRepository _thirdPartyRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceLineItemRepository _lineItemRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ILogger<InvoiceSyncService> _logger;
    private readonly SyncSettings _settings;

    public InvoiceSyncService(
        IThirdPartyRepository thirdPartyRepository,
        IInvoiceRepository invoiceRepository,
        IInvoiceLineItemRepository lineItemRepository,
        ITransactionRepository transactionRepository,
        IOptions<SyncSettings> settings,
        ILogger<InvoiceSyncService> logger)
    {
        _thirdPartyRepository = thirdPartyRepository;
        _invoiceRepository = invoiceRepository;
        _lineItemRepository = lineItemRepository;
        _transactionRepository = transactionRepository;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SyncSummaryResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        // ---- Runtime safety ----
        var startTime = Stopwatch.StartNew();
        var maxRuntimeMs = _settings.MaxRuntimeMs;

        // ---- Sizing ----
        var pageSize = _settings.PageSize;
        var batchSize = _settings.BatchSize;
        var inChunk = _settings.InChunk;

        // ---- Run-level counters ----
        var processedRows = 0;
        var totalInvoicesInserted = 0;
        var totalInvoicesUpdated = 0;
        var totalLineItemsInserted = 0;
        var totalLineItemsUpdated = 0;
        var executionStoppedEarly = false;

        // ---- ThirdPartyData status tracking ----
        var processedThirdPartyRows = new List<ThirdPartyRowOutcome>();
        var failedThirdPartyRows = new List<ThirdPartyRowOutcome>();

        try
        {
            // =========================
            // 🔹 STEP 1: FETCH A BOUNDED PAGE OF UNPROCESSED ThirdPartyData ROWS
            // =========================

            var result = await _thirdPartyRepository.FetchUnprocessedPageAsync(pageSize, cancellationToken);
            Console.WriteLine($"Invoice data: {result}");
            _logger.LogInformation("Rows fetched this execution: {Count}", result.Count);

            if (result.Count == 0)
            {
                _logger.LogInformation("No unprocessed rows with invoice data found — nothing to do");

                return new SyncSummaryResult
                {
                    Status = SyncConstants.SummaryStatusSuccess,
                    ProcessedRows = 0,
                    TotalInvoicesInserted = 0,
                    TotalInvoicesUpdated = 0,
                    TotalLineItemsInserted = 0,
                    TotalLineItemsUpdated = 0,
                    ExecutionStoppedEarly = false,
                    ProcessedThirdPartyRows = 0,
                    FailedThirdPartyRows = 0
                };
            }

            // =========================================================
            // PASS 1: PARSE ALL ROWS AND COLLECT INVOICE IDS
            // =========================================================

            var parsedCache = new List<ParsedCacheEntry>();
            var pageInvoiceIds = new List<string>();
            var pageTransactionIds = new List<string>();

            foreach (var row in result)
            {
                //// ---- Runtime guard ----
                //if (startTime.ElapsedMilliseconds > maxRuntimeMs)
                //{
                //    _logger.LogInformation("Runtime limit reached during parse pass");
                //    executionStoppedEarly = true;
                //    break;
                //}

                try
                {
                    var raw = (row.Invoice ?? string.Empty).Trim();

                    if (JsCompat.IsEmptyInvoicePayload(raw))
                    {
                        parsedCache.Add(new ParsedCacheEntry
                        {
                            Row = row,
                            Skip = true,
                            SkipReason = "empty invoice field"
                        });
                        continue;
                    }

                    var parsed = JsCompat.ParseWithDoubleDecode(raw);
                    Console.WriteLine($"Parsed invoice data: {parsed}");
                    var (invoices, hotelId, rowInvoiceDate) = ExtractInvoices(parsed);
                    Console.WriteLine($"Invoices extracted: {invoices.Count}, Hotel ID: {hotelId}");
                    foreach (var data in invoices)
                    {
                        var iID = data.TryGetPropertySafe("id").AsJsString();
                        var iFolioType = data.TryGetPropertySafe("folioType").AsJsString().Trim().ToUpperInvariant();
                        if (!string.IsNullOrEmpty(iID) && iFolioType == "INV")
                        {
                            pageInvoiceIds.Add(iID);
                            Console.WriteLine($"Invoice ID: {iID}");
                        }

                        var lineItemsForTransIds = data.TryGetPropertySafe("lineItems");
                        if (lineItemsForTransIds.IsArray())
                        {
                            foreach (var li in lineItemsForTransIds.EnumerateArraySafe())
                            {
                                var liTransId = li.TryGetPropertySafe("transId").AsJsString();
                                if (!string.IsNullOrEmpty(liTransId))
                                {
                                    pageTransactionIds.Add(liTransId);
                                }
                            }
                        }
                    }

                    parsedCache.Add(new ParsedCacheEntry
                    {
                        Row = row,
                        Skip = false,
                        Invoices = invoices,
                        HotelId = hotelId,
                        InvoiceDate = rowInvoiceDate
                    });
                }
                catch (Exception parseErr)
                {
                    _logger.LogInformation("Parse error ROWID {RowId}: {Error}", row.ROWID, parseErr.ToJsString());

                    parsedCache.Add(new ParsedCacheEntry
                    {
                        Row = row,
                        Skip = true,
                        SkipReason = "JSON parse error: " + parseErr.ToJsString()
                    });
                }
            }

            // ---- diagnostics ----
            _logger.LogInformation("Invoices Extracted => {Count}", pageInvoiceIds.Count);
            _logger.LogInformation("Unique Invoice IDs => {Count}", pageInvoiceIds.Distinct().Count());
            _logger.LogInformation("Invoice IDs => {Ids}", JsonSerializer.Serialize(pageInvoiceIds));

            foreach (var cached in parsedCache)
            {
                _logger.LogInformation(
                    "ROWID => {RowId} | Invoice Count => {Count}",
                    cached.Row.ROWID, cached.Invoices.Count);
            }

            // =========================================================
            // STEP: PAGE-SPECIFIC LOOKUPS
            // =========================================================

            var invoiceMap = new Dictionary<string, string>();
            var lineItemMap = new Dictionary<string, string>();
            var lineItemKeyMode = LineItemKeyMode.TransId;
            Console.WriteLine($"Invoice Map: {JsonSerializer.Serialize(invoiceMap)}");
            if (pageInvoiceIds.Count > 0)
            {
                invoiceMap = await _invoiceRepository.QueryInvoiceMapAsync(pageInvoiceIds, inChunk, cancellationToken);
                _logger.LogInformation("Invoice Map Count = {Count}", invoiceMap.Count);

                foreach (var kv in invoiceMap)
                {
                    _logger.LogInformation("InvoiceMap => {InvoiceId} -> {RowId}", kv.Key, kv.Value);
                    Console.WriteLine($"InvoiceMap => {kv.Key} -> {kv.Value}");
                }

                var liResult = await _lineItemRepository.QueryLineItemMapAsync(pageInvoiceIds, inChunk, cancellationToken);
                lineItemMap = liResult.Map;
                lineItemKeyMode = liResult.KeyMode;
            }

            // ---- TransactionModule lookup: Transaction_ID -> { Tax_Value, HSN_Code } ----
            var transactionModuleMap = new Dictionary<string, AgentSyncConsole.Models.Transaction>();
            if (pageTransactionIds.Count > 0)
            {
                transactionModuleMap = await _transactionRepository.GetTransactionsByIdsAsync(pageTransactionIds, inChunk, cancellationToken);
                _logger.LogInformation("TransactionModule Map Count => {Count}", transactionModuleMap.Count);
            }

            _logger.LogInformation("Invoice Map Count => {Count}", invoiceMap.Count);
            _logger.LogInformation("Invoice Map Keys => {Keys}", JsonSerializer.Serialize(invoiceMap.Keys));
            _logger.LogInformation("Line Item Map Count => {Count}", lineItemMap.Count);

            // =========================================================
            // PER-EXECUTION ARRAYS
            // =========================================================

            var invoiceInsertRows = new List<Invoice>();
            var invoiceUpdateRows = new List<Invoice>();
            var lineItemInsertRows = new List<InvoiceLineItem>();
            var lineItemUpdateRows = new List<InvoiceLineItem>();

            var seenInvoiceInsert = new HashSet<string>();
            var seenInvoiceUpdate = new HashSet<string>();
            var seenLineItemInsert = new HashSet<string>();
            var seenLineItemUpdate = new HashSet<string>();

            var rowContributions = new Dictionary<string, RowContribution>();

            void TrackContribution(string rowId, string? invoiceId, string? lineItemKey)
            {
                if (!rowContributions.TryGetValue(rowId, out var contribution))
                {
                    contribution = new RowContribution();
                    rowContributions[rowId] = contribution;
                }

                if (!string.IsNullOrEmpty(invoiceId)) contribution.InvoiceIds.Add(invoiceId);
                if (!string.IsNullOrEmpty(lineItemKey)) contribution.LineItemKeys.Add(lineItemKey);
            }

            // =========================================================
            // PASS 2: BUILD INSERT / UPDATE ARRAYS FROM PARSED CACHE
            // =========================================================

            foreach (var cached in parsedCache)
            {
                //if (startTime.ElapsedMilliseconds > maxRuntimeMs)
                //{
                //    _logger.LogInformation("Runtime limit reached during build pass");
                //    executionStoppedEarly = true;
                //    break;
                //}

                var row = cached.Row;

                try
                {
                    if (cached.Skip)
                    {
                        _logger.LogInformation("Skipping ROWID {RowId}: {Reason}", row.ROWID, cached.SkipReason);
                        failedThirdPartyRows.Add(new ThirdPartyRowOutcome { ROWID = row.ROWID, Error = cached.SkipReason });
                        continue;
                    }

                    var invoices = cached.Invoices;
                    var hotelId = cached.HotelId;
                    var cachedInvoiceDate = cached.InvoiceDate;

                    // =============================================
                    // 🔹 LOOP INVOICES
                    // =============================================

                    foreach (var data in invoices)
                    {
                        var invoiceID = data.TryGetPropertySafe("id").AsJsString();
                        if (string.IsNullOrEmpty(invoiceID)) continue;

                        var folioType = data.TryGetPropertySafe("folioType").AsJsString().Trim().ToUpperInvariant();
                        if (folioType != "INV")
                        {
                            _logger.LogInformation(
                                "Invoice skipped because folioType is not INV => InvoiceID={InvoiceId} | FolioType={FolioType}",
                                invoiceID,
                                folioType);
                            continue;
                        }

                        // -----------------------------------------
                        // 🔹 CREDIT NOTE DETECTION — preserved exactly.
                        // -----------------------------------------

                        var customFolioNo = data.TryGetPropertySafe("customFolioNo").AsJsString();
                        var folioNoRaw = string.IsNullOrEmpty(customFolioNo)
                            ? data.TryGetPropertySafe("folioNo").AsJsString()
                            : customFolioNo;
                        var folioNo = folioNoRaw.ToUpperInvariant();
                        Console.WriteLine($"Folio No: {folioNo}");
                        var isCreditNote = false;

                        // Rule 1 — Folio Number
                        if (folioNo.StartsWith(SyncConstants.CreditNotePrefix, StringComparison.Ordinal) ||
                            folioNo.Contains(SyncConstants.CreditNoteContains, StringComparison.Ordinal))
                        {
                            isCreditNote = true;
                        }

                        // Rule 2 — Line Item Titles
                        var lineItemsProp = data.TryGetPropertySafe("lineItems");
                        if (!isCreditNote && lineItemsProp.IsArray())
                        {
                            foreach (var li in lineItemsProp.EnumerateArraySafe())
                            {
                                var title = li.TryGetPropertySafe("title").AsJsString().ToUpperInvariant();
                                if (title.Contains(SyncConstants.CreditNoteLineItemTitleMarker, StringComparison.Ordinal))
                                {
                                    isCreditNote = true;
                                    break;
                                }
                            }
                        }

                        if (isCreditNote)
                        {
                            _logger.LogInformation("Credit note skipped: {InvoiceId} folio: {Folio}", invoiceID, folioNo);
                            continue;
                        }

                        // -----------------------------------------
                        // End credit note detection — safe to proceed.
                        // -----------------------------------------

                        var lineItems = lineItemsProp.IsArray()
                            ? lineItemsProp!.Value.EnumerateArray().ToList()
                            : new List<JsonElement>();

                        var paymentsProp = data.TryGetPropertySafe("payments");
                        var payments = paymentsProp.IsArray()
                            ? paymentsProp!.Value.EnumerateArray().ToList()
                            : new List<JsonElement>();

                        var firstPayment = payments.Count > 0 ? payments[0] : (JsonElement?)null;
                        var firstLineItem = lineItems.Count > 0 ? lineItems[0] : (JsonElement?)null;

                        // =============================================
                        // 🔹 INVOICE HEADER — field mapping preserved exactly.
                        // =============================================
                        var invoiceNumberSource = string.IsNullOrEmpty(customFolioNo)
                            ? data.TryGetPropertySafe("folioNo").AsJsString()
                            : customFolioNo;
                        Console.WriteLine("rowdata", invoiceNumberSource);
                        var rowData = new Invoice
                        {
                            Hotel_ID = hotelId,
                            Customer_Name = data.TryGetPropertySafe("ownerId").AsJsString(),
                            Location_Name = data.TryGetPropertySafe("type").AsJsString(),
                            Invoice_Number = invoiceNumberSource.RemoveAllSpaces(),
                            Invoice_Date = cachedInvoiceDate,
                            Owner_Type = data.TryGetPropertySafe("ownerType").AsJsString(),
                            Payment_Term = firstPayment?.TryGetPropertySafe("title").AsJsString() ?? string.Empty,
                            Due_Date = cachedInvoiceDate,
                            InvoiceID = invoiceID,
                            Reservation_ID = data.TryGetPropertySafe("rsvId").AsJsString()
                        };
                        Console.WriteLine($"rowdata: {rowData}");
                        TrackContribution(row.ROWID, invoiceID, null);

                        if (invoiceMap.ContainsKey(invoiceID))
                        {
                            _logger.LogInformation("Invoice Exists => {InvoiceId}", invoiceID);
                        }
                        else
                        {
                            _logger.LogInformation("Invoice Insert => {InvoiceId}", invoiceID);
                        }

                        // ---- ROUTE: INSERT or UPDATE ----
                        if (invoiceMap.TryGetValue(invoiceID, out var existingRowId))
                        {
                            if (!seenInvoiceUpdate.Contains(invoiceID))
                            {
                                if (existingRowId != SyncConstants.PendingSentinel)
                                {
                                    rowData.ROWID = int.Parse(existingRowId);
                                    invoiceUpdateRows.Add(rowData);
                                    seenInvoiceUpdate.Add(invoiceID);
                                }
                                else
                                {
                                    _logger.LogInformation("Invoice already marked for insert. Skipping update. InvoiceID={InvoiceID}", invoiceID);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Duplicate Invoice Skipped => {InvoiceId}", invoiceID);
                            }
                        }
                        else if (!seenInvoiceInsert.Contains(invoiceID))
                        {
                            invoiceInsertRows.Add(rowData);
                            seenInvoiceInsert.Add(invoiceID);
                            // Prevent duplicate INSERT if the same ID appears again in this batch.
                            invoiceMap[invoiceID] = SyncConstants.PendingSentinel;
                        }
                        Console.WriteLine(invoiceMap);
                        // =============================================
                        // 🔹 LINE ITEMS
                        // =============================================

                        foreach (var lineItem in lineItems)
                        {
                            var transId = lineItem.TryGetPropertySafe("transId").AsJsString();
                            if (string.IsNullOrEmpty(transId)) continue;

                            var itemName = lineItem.TryGetPropertySafe("title").AsJsString();
                            var itemAmount = lineItem.TryGetPropertySafe("priceAftDisc").AsJsString();

                            // ---- Tax_Rate + HSN_SAC_Code: copied directly from TransactionModule ----
                            var lineItemTaxRate = 0d;
                            var lineItemHsnCode = string.Empty;

                            if (transactionModuleMap.TryGetValue(transId, out var transactionModuleRow))
                            {
                                lineItemTaxRate = (double)transactionModuleRow.Tax_value;
                                lineItemHsnCode = transactionModuleRow.HSN_Code;
                            }

                            Console.WriteLine($"tax value, {lineItemTaxRate}");
                            var liKey = lineItemKeyMode == LineItemKeyMode.TransId
                                ? $"{invoiceID}_{transId}"
                                : $"{invoiceID}_{transId}_{lineItemHsnCode}_{itemName}_{itemAmount}";

                            _logger.LogInformation("LineItem Key => {Key}", liKey);

                            TrackContribution(row.ROWID, null, liKey);

                            var lineItemData = new InvoiceLineItem
                            {
                                InvoiceID = invoiceID,
                                Name = itemName,
                                Description = lineItem.TryGetPropertySafe("type").AsJsString(),
                                HSN_SAC_Code = lineItemHsnCode,
                                Quality = string.IsNullOrEmpty(lineItem.TryGetPropertySafe("quantity").AsJsString())
                                    ? "1"
                                    : lineItem.TryGetPropertySafe("quantity").AsJsString(),
                                Rate = lineItem.TryGetPropertySafe("netTotal").AsJsString(),
                                Tax_Rate = lineItemTaxRate,
                                TransactionID = lineItem.TryGetPropertySafe("transId").AsJsString(),
                                Amount = itemAmount
                            };

                            // Only include column when schema has it.
                            if (lineItemKeyMode == LineItemKeyMode.TransId)
                            {
                                lineItemData.Hotelogix_Trans_ID = transId;
                            }

                            // ---- ROUTE LINE ITEM: INSERT or UPDATE ----
                            if (lineItemMap.TryGetValue(liKey, out var existingLiRowId))
                            {
                                if (!seenLineItemUpdate.Contains(liKey))
                                {
                                    lineItemData.ROWID = existingLiRowId;
                                    lineItemUpdateRows.Add(lineItemData);
                                    seenLineItemUpdate.Add(liKey);
                                }
                                else
                                {
                                    _logger.LogInformation("Duplicate LineItem Skipped => {Key}", liKey);
                                }
                            }
                            else if (!seenLineItemInsert.Contains(liKey))
                            {
                                lineItemInsertRows.Add(lineItemData);
                                seenLineItemInsert.Add(liKey);
                                lineItemMap[liKey] = SyncConstants.PendingSentinel;
                            }
                        }
                    }

                    processedRows++;
                }
                catch (Exception rowErr)
                {
                    _logger.LogInformation("Row error ROWID {RowId}: {Error}", row.ROWID, rowErr.ToJsString());
                    failedThirdPartyRows.Add(new ThirdPartyRowOutcome { ROWID = row.ROWID, Error = rowErr.ToJsString() });
                }
            }

            _logger.LogInformation("{Summary}", JsonSerializer.Serialize(new
            {
                invoiceInsertRows = invoiceInsertRows.Count,
                invoiceUpdateRows = invoiceUpdateRows.Count,
                lineItemInsertRows = lineItemInsertRows.Count,
                lineItemUpdateRows = lineItemUpdateRows.Count
            }));

            // =========================================================
            // 🔹 BATCH WRITES
            // =========================================================

            var failedInvoiceIds = new HashSet<string>();
            var failedLineItemKeys = new HashSet<string>();
            var writeErrorsByKey = new Dictionary<string, string>();

            // ---- INSERT INVOICES ----
            if (invoiceInsertRows.Count > 0)
            {
                var batchResult = await BatchRunner.RunBatchesAsync(
                    invoiceInsertRows,
                    batch => _invoiceRepository.InsertRowsAsync(batch, cancellationToken),
                    "Invoice Insert", batchSize, _logger, cancellationToken);

                totalInvoicesInserted += batchResult.Confirmed;
                foreach (var f in batchResult.Failed)
                {
                    failedInvoiceIds.Add(f.Row.InvoiceID);
                    writeErrorsByKey[f.Row.InvoiceID] = f.Error;
                }
            }

            // ---- UPDATE INVOICES ----
            if (invoiceUpdateRows.Count > 0)
            {
                var batchResult = await BatchRunner.RunBatchesAsync(
                    invoiceUpdateRows,
                    batch => _invoiceRepository.UpdateRowsAsync(batch, cancellationToken),
                    "Invoice Update", batchSize, _logger, cancellationToken);

                totalInvoicesUpdated += batchResult.Confirmed;
                foreach (var f in batchResult.Failed)
                {
                    failedInvoiceIds.Add(f.Row.InvoiceID);
                    writeErrorsByKey[f.Row.InvoiceID] = f.Error;
                }
            }

            // ---- INSERT LINE ITEMS ----
            if (lineItemInsertRows.Count > 0)
            {
                var batchResult = await BatchRunner.RunBatchesAsync(
                    lineItemInsertRows,
                    batch => _lineItemRepository.InsertRowsAsync(batch, cancellationToken),
                    "LineItem Insert", batchSize, _logger, cancellationToken);

                totalLineItemsInserted += batchResult.Confirmed;
                foreach (var f in batchResult.Failed)
                {
                    var key = lineItemKeyMode == LineItemKeyMode.TransId
                        ? $"{f.Row.InvoiceID}_{f.Row.Hotelogix_Trans_ID}"
                        : $"{f.Row.InvoiceID}_{f.Row.HSN_SAC_Code}_{f.Row.Name}_{f.Row.Amount}";
                    failedLineItemKeys.Add(key);
                    writeErrorsByKey[key] = f.Error;
                }
            }

            // ---- UPDATE LINE ITEMS ----
            if (lineItemUpdateRows.Count > 0)
            {
                var batchResult = await BatchRunner.RunBatchesAsync(
                    lineItemUpdateRows,
                    batch => _lineItemRepository.UpdateRowsAsync(batch, cancellationToken),
                    "LineItem Update", batchSize, _logger, cancellationToken);

                totalLineItemsUpdated += batchResult.Confirmed;
                foreach (var f in batchResult.Failed)
                {
                    var key = lineItemKeyMode == LineItemKeyMode.TransId
                        ? $"{f.Row.InvoiceID}_{f.Row.Hotelogix_Trans_ID}"
                        : $"{f.Row.InvoiceID}_{f.Row.HSN_SAC_Code}_{f.Row.Name}_{f.Row.Amount}";
                    failedLineItemKeys.Add(key);
                    writeErrorsByKey[key] = f.Error;
                }
            }

            // =========================================================
            // 🔹 RESOLVE THIRDPARTYDATA STATUS PER ROW
            // =========================================================

            var alreadyFailedRowIds = failedThirdPartyRows.Select(r => r.ROWID).ToHashSet();

            foreach (var cached in parsedCache)
            {
                var rowID = cached.Row.ROWID;

                if (cached.Skip || alreadyFailedRowIds.Contains(rowID)) continue;

                if (!rowContributions.TryGetValue(rowID, out var contribution))
                {
                    // Row had no invoices/line items to write (e.g. all credit notes) — treat as processed.
                    processedThirdPartyRows.Add(new ThirdPartyRowOutcome { ROWID = rowID });
                    continue;
                }

                var rowFailed = false;
                var errorReason = string.Empty;

                foreach (var iID in contribution.InvoiceIds)
                {
                    if (failedInvoiceIds.Contains(iID))
                    {
                        rowFailed = true;
                        errorReason = writeErrorsByKey.GetValueOrDefault(iID, "Invoice write failed");
                        break;
                    }
                }

                if (!rowFailed)
                {
                    foreach (var liKey in contribution.LineItemKeys)
                    {
                        if (failedLineItemKeys.Contains(liKey))
                        {
                            rowFailed = true;
                            errorReason = writeErrorsByKey.GetValueOrDefault(liKey, "Line item write failed");
                            break;
                        }
                    }
                }

                if (rowFailed)
                {
                    failedThirdPartyRows.Add(new ThirdPartyRowOutcome { ROWID = rowID, Error = errorReason });
                }
                else
                {
                    processedThirdPartyRows.Add(new ThirdPartyRowOutcome { ROWID = rowID });
                }
            }

            // =========================================================
            // 🔹 WRITE STATUS BACK TO ThirdPartyData
            // =========================================================

            if (processedThirdPartyRows.Count > 0)
            {
                var successUpdates = processedThirdPartyRows.Select(r => new ThirdPartyDataRow
                {
                    ROWID = r.ROWID,
                    Status = SyncConstants.StatusProcessed,
                    Response = SyncConstants.ResponseInvoiceSyncCompleted
                }).ToList();

                var statusResult = await BatchRunner.RunBatchesAsync(
                    successUpdates,
                    batch => _thirdPartyRepository.UpdateRowsAsync(batch, cancellationToken),
                    "ThirdPartyData Status Update (Processed)", batchSize, _logger, cancellationToken);

                foreach (var r in processedThirdPartyRows)
                {
                    _logger.LogInformation("ThirdPartyData ROWID {RowId} marked Processed", r.ROWID);
                }

                if (statusResult.Failed.Count > 0)
                {
                    _logger.LogInformation(
                        "Failed to write Processed status for {Count} ThirdPartyData row(s)",
                        statusResult.Failed.Count);
                }
            }

            foreach (var r in failedThirdPartyRows)
            {
                try
                {
                    await _thirdPartyRepository.UpdateRowAsync(new ThirdPartyDataRow
                    {
                        ROWID = r.ROWID,
                        Status = SyncConstants.StatusFailed,
                        Response = string.IsNullOrEmpty(r.Error) ? SyncConstants.ResponseUnknownError : r.Error
                    }, cancellationToken);

                    _logger.LogInformation(
                        "ThirdPartyData ROWID {RowId} marked Failed\nReason:\n{Error}", r.ROWID, r.Error);
                }
                catch (Exception statusErr)
                {
                    _logger.LogInformation(
                        "Failed to write Failed status for ROWID {RowId}: {Error}", r.ROWID, statusErr.ToJsString());
                }
            }

            // =========================================================
            // 🔹 FINAL RESPONSE
            // =========================================================

            var summary = new SyncSummaryResult
            {
                Status = SyncConstants.SummaryStatusSuccess,
                ProcessedRows = processedRows,
                TotalInvoicesInserted = totalInvoicesInserted,
                TotalInvoicesUpdated = totalInvoicesUpdated,
                TotalLineItemsInserted = totalLineItemsInserted,
                TotalLineItemsUpdated = totalLineItemsUpdated,
                ExecutionStoppedEarly = executionStoppedEarly,
                ProcessedThirdPartyRows = processedThirdPartyRows.Count,
                FailedThirdPartyRows = failedThirdPartyRows.Count,
                InvoiceIdsFound = pageInvoiceIds.Count,
                UniqueInvoiceIds = pageInvoiceIds.Distinct().Count(),
                InvoiceMapCount = invoiceMap.Count,
                InvoiceInsertRows = invoiceInsertRows.Count,
                InvoiceUpdateRows = invoiceUpdateRows.Count,
                LineItemInsertRows = lineItemInsertRows.Count,
                LineItemUpdateRows = lineItemUpdateRows.Count
            };

            _logger.LogInformation("SUMMARY: {Summary}", JsonSerializer.Serialize(summary));

            return summary;
        }
        catch (Exception err)
        {
            _logger.LogError("MAIN ERROR: {Error}", err.ToJsString());

            return new SyncSummaryResult
            {
                Status = SyncConstants.SummaryStatusError,
                Message = err.ToJsString(),
                ProcessedRows = processedRows,
                TotalInvoicesInserted = totalInvoicesInserted,
                TotalInvoicesUpdated = totalInvoicesUpdated,
                TotalLineItemsInserted = totalLineItemsInserted,
                TotalLineItemsUpdated = totalLineItemsUpdated,
                ExecutionStoppedEarly = executionStoppedEarly,
                ProcessedThirdPartyRows = processedThirdPartyRows.Count,
                FailedThirdPartyRows = failedThirdPartyRows.Count
            };
        }
    }

    // =========================================================
    // 🔹 HELPER: ExtractInvoices — supports both known Hotelogix payload shapes.
    // Also extracts the invoice date (YYYY-MM-DD only) from hotelogix.datetime,
    // e.g. "2026-07-25T04:38:42" -> "2026-07-25". No DateTime.Parse() — this is
    // a plain split on 'T'. If the field is missing, InvoiceDate is "".
    // =========================================================
    private static (List<JsonElement> Invoices, string HotelId, string InvoiceDate) ExtractInvoices(JsonElement parsed)
    {
        // Shape 1: response.data.invoices
        Console.WriteLine($"parsed outside the if loop: {parsed}");

        if (parsed.TryGetProperty("response", out JsonElement response) &&
            response.TryGetProperty("data", out JsonElement respData))
        {
            Console.WriteLine("Entered Shape 1");

            Console.WriteLine($"respData: {respData}");

            if (respData.TryGetProperty("invoices", out JsonElement respTransactions) &&
                respTransactions.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine("Entered Shape 1 invoices");

                Console.WriteLine($"respTransactions: {respTransactions}");

                var list = respTransactions.EnumerateArray().ToList();

                var hotelId = response.TryGetProperty("hotelId", out var hid)
                    ? hid.ToString()
                    : string.Empty;

                // Shape 1 has no "hotelogix" wrapper around "response", so it must
                // be looked up fresh from the root of the payload (if present at all).
                var invoiceDate = string.Empty;

                if (parsed.TryGetProperty("hotelogix", out JsonElement shape1Hotelogix) &&
                    shape1Hotelogix.TryGetProperty("datetime", out var dt))
                {
                    var fullDate = dt.GetString() ?? string.Empty;

                    invoiceDate = fullDate.Contains('T')
                        ? fullDate.Split('T')[0]
                        : fullDate;
                }

                if (string.IsNullOrEmpty(hotelId) &&
                    response.TryGetProperty("hotelID", out var hid2))
                {
                    Console.WriteLine("hotelID fallback");
                    Console.WriteLine($"hid2: {hid2}");

                    hotelId = hid2.ToString();
                }

                return (list, hotelId, invoiceDate);
            }
        }

        // Shape 2: hotelogix.response.data.invoices
        if (parsed.TryGetProperty("hotelogix", out JsonElement hotelogix) &&
            hotelogix.TryGetProperty("response", out JsonElement hxResponse) &&
            hxResponse.TryGetProperty("data", out JsonElement hxData))
        {
            Console.WriteLine("Entered Shape 2");

            Console.WriteLine($"hxData: {hxData}");

            if (hxData.TryGetProperty("invoices", out JsonElement hxTransactions) &&
                hxTransactions.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine("Entered Shape 2 invoices");

                Console.WriteLine($"hxTransactions: {hxTransactions}");

                var list = hxTransactions.EnumerateArray().ToList();

                var hotelId = hxResponse.TryGetProperty("hotelId", out var hid)
                    ? hid.ToString()
                    : string.Empty;

                if (string.IsNullOrEmpty(hotelId) &&
                    hxResponse.TryGetProperty("hotelID", out var hid2))
                {
                    hotelId = hid2.ToString();
                }

                // Shape 2 already has "hotelogix" in scope at the root of this payload.
                var invoiceDate = string.Empty;

                if (hotelogix.TryGetProperty("datetime", out var dt))
                {
                    var fullDate = dt.GetString() ?? string.Empty;

                    invoiceDate = fullDate.Contains('T')
                        ? fullDate.Split('T')[0]
                        : fullDate;
                }

                return (list, hotelId, invoiceDate);
            }
        }

        Console.WriteLine("No invoices found.");

        return (new List<JsonElement>(), string.Empty, string.Empty);
    }
    public Task RunOnceAsync(object invoiceDate, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}