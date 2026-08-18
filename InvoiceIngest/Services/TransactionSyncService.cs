using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AgentSyncConsole.Interfaces.TransactionInterface;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.Models;
//using AgentSyncConsole.Models.JsonModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentSyncConsole.Services;

/// <summary>
/// Converted 1:1 from the Catalyst Hotelogix "Transaction Sync" function
/// (module.exports async (context, basicIO) => {...}). Every section below
/// corresponds to the equivalent section in the original source, in the same
/// order: runtime/sizing constants, STEP 1 (bounded page fetch), PASS 1
/// (parse + collect Transaction_IDs), page-specific lookup, PASS 2 (build
/// insert/update arrays with duplicate detection), batched writes with
/// per-row fallback, ThirdPartyData status resolution, status write-back, and
/// the final summary. Only the language and persistence layer changed
/// (Catalyst datastore/zcql -> SQL Server via ITransactionRepository and the
/// extended IThirdPartyDataRepository); nothing has been simplified.
/// </summary>
public class TransactionSyncService : ITransactionSyncService
{
    private readonly IThirdPartyDataRepository _thirdPartyDataRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ILogger<TransactionSyncService> _logger;

    // ---- Sizing (reused from TransactionSync:* in appsettings.json, with the
    // same fallback defaults as the original's hardcoded constants) ----
    private readonly int _pageSize;
    private readonly int _batchSize;
    private readonly int _inChunk;
    private readonly long _maxRuntimeMs;

    public TransactionSyncService(
        IThirdPartyDataRepository thirdPartyDataRepository,
        ITransactionRepository transactionRepository,
        IConfiguration configuration,
        ILogger<TransactionSyncService> logger)
    {
        _thirdPartyDataRepository = thirdPartyDataRepository;
        _transactionRepository = transactionRepository;
        _logger = logger;

        _pageSize = configuration.GetValue<int?>("TransactionSync:PageSize") ?? 300;
        _batchSize = configuration.GetValue<int?>("TransactionSync:BatchSize") ?? 10;
        _inChunk = configuration.GetValue<int?>("TransactionSync:InChunk") ?? 100;
        var maxRuntimeSeconds = configuration.GetValue<int?>("TransactionSync:MaxRuntimeSeconds") ?? 240;
        _maxRuntimeMs = maxRuntimeSeconds * 1000L;
    }

    public async Task<TransactionSyncSummary> RunAsync(CancellationToken ct = default)
    {
        //var stopwatch = Stopwatch.StartNew();

        var processedRows = 0;
        var totalTransactionInserted = 0;
        var totalTransactionUpdated = 0;
        var executionStoppedEarly = false;

        var processedThirdPartyRows = new List<long>();
        var failedThirdPartyRows = new List<(long RowId, string Error)>();

        // Declared here so they're visible to the final summary even if an
        // unhandled exception occurs mid-run (mirrors the outer try/catch in
        // the original, which still reports whatever counters were reached).
        var pageTransactionIds = new List<string>();
        var transactionMap = new Dictionary<string, long>();
        var transactionInsertRows = new List<Transaction>();
        var transactionUpdateRows = new List<Transaction>();

        try
        {
            // =========================================================
            // STEP 1: FETCH A BOUNDED PAGE OF UNPROCESSED ThirdPartyData ROWS
            // =========================================================

            var page = await _thirdPartyDataRepository.GetUnprocessedTransactionRowsAsync(_pageSize, ct);

            _logger.LogInformation("Rows fetched this execution: {Count}", page.Count);

            if (page.Count == 0)
            {
                _logger.LogInformation("No unprocessed rows with transaction data found — nothing to do");

                return new TransactionSyncSummary
                {
                    Status = "success",
                    ProcessedRows = 0,
                    TotalTransactionInserted = 0,
                    TotalTransactionUpdated = 0,
                    ExecutionStoppedEarly = false,
                    ProcessedThirdPartyRows = 0,
                    FailedThirdPartyRows = 0,
                    //ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // =========================================================
            // PASS 1: PARSE ALL ROWS AND COLLECT TRANSACTION IDS
            // =========================================================

            var parsedCache = new List<ParsedThirdPartyRow>();

            foreach (var row in page)
            {
                //if (stopwatch.ElapsedMilliseconds > _maxRuntimeMs)
                //{
                //    _logger.LogInformation("Runtime limit reached during parse pass");
                //    executionStoppedEarly = true;
                //    break;
                //}

                var raw = (row.transactions ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(raw) || raw == "{}" || raw == "[]" || raw == "null")
                {
                    parsedCache.Add(ParsedThirdPartyRow.Skipped(row, "empty transactions field"));
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(raw);
                    var rootElement = document.RootElement;

                    // Mirrors: if (typeof parsed === 'string') { parsed = JSON.parse(parsed); }
                    if (rootElement.ValueKind == JsonValueKind.String)
                    {
                        var inner = rootElement.GetString() ?? string.Empty;
                        using var innerDocument = JsonDocument.Parse(inner);
                        rootElement = innerDocument.RootElement.Clone();
                    }

                    var (transactions, _) = ExtractTransactions(rootElement);

                    if (transactions.Count == 0)
                    {
                        parsedCache.Add(ParsedThirdPartyRow.Skipped(row, "no transactions array found in payload"));
                        continue;
                    }

                    foreach (var txn in transactions)
                    {
                        var tId = txn.Id ?? string.Empty;
                        if (!string.IsNullOrEmpty(tId)) pageTransactionIds.Add(tId);
                    }

                    parsedCache.Add(ParsedThirdPartyRow.Parsed(row, transactions));
                }
                catch (Exception parseErr)
                {
                    _logger.LogWarning(parseErr, "Parse error ROWID {RowId}", row.ROWID);
                    parsedCache.Add(ParsedThirdPartyRow.Skipped(row, $"JSON parse error: {parseErr.Message}"));
                }
            }

            // ---- diagnostic: transaction extraction results ----
            _logger.LogInformation("Transactions Extracted => {Count}", pageTransactionIds.Count);
            _logger.LogInformation("Unique Transaction IDs => {Count}", pageTransactionIds.Distinct().Count());
            _logger.LogInformation("Transaction IDs => {Ids}", JsonHelper.Serialize(pageTransactionIds));

            // ---- diagnostic: per-ThirdPartyData-row transaction counts ----
            foreach (var cached in parsedCache)
            {
                _logger.LogInformation(
                    "ROWID => {RowId} | Transaction Count => {Count}",
                    cached.Row.ROWID, cached.Transactions?.Count ?? 0);
            }

            // =========================================================
            // STEP: PAGE-SPECIFIC LOOKUP
            // =========================================================

            if (pageTransactionIds.Count > 0)
            {
                transactionMap = await _transactionRepository.GetTransactionRowIdMapAsync(pageTransactionIds, _inChunk, ct);
            }

            _logger.LogInformation("Transaction Map Count => {Count}", transactionMap.Count);
            _logger.LogInformation("Transaction Map Keys => {Keys}", JsonHelper.Serialize(transactionMap.Keys.ToList()));

            // =========================================================
            // PER-EXECUTION ARRAYS
            // =========================================================

            var seenTransactionInsert = new HashSet<string>();
            var seenTransactionUpdate = new HashSet<string>();

            // ROWID -> set of Transaction_IDs it contributed, so write
            // failures can be traced back to the source ThirdPartyData row.
            var rowContributions = new Dictionary<long, HashSet<string>>();

            void TrackContribution(long rowId, string transactionId)
            {
                if (!rowContributions.TryGetValue(rowId, out var set))
                {
                    set = new HashSet<string>();
                    rowContributions[rowId] = set;
                }
                if (!string.IsNullOrEmpty(transactionId)) set.Add(transactionId);
            }

            // Mirrors transactionMap[transactionID] = 'pending' in the original:
            // a truthy placeholder set the first time a brand-new Transaction_ID
            // is queued for insert, so a duplicate of that same ID appearing
            // again later in THIS SAME execution page is routed to UPDATE
            // instead of a second INSERT. In the original, 'pending' is not a
            // real ROWID, so that update silently targets nothing. Preserved
            // here with -1 as the sentinel; TransactionRepository.UpdateRowsAsync
            // explicitly fails a row whose update doesn't affect exactly one
            // row, so this now surfaces as a tracked, logged failure instead of
            // a silent no-op — everything else about the routing is identical.
            const long PendingRowIdSentinel = -1;

            // =========================================================
            // PASS 2: BUILD INSERT / UPDATE ARRAYS FROM PARSED CACHE
            // =========================================================

            foreach (var cached in parsedCache)
            {
                //if (stopwatch.ElapsedMilliseconds > _maxRuntimeMs)
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
                        failedThirdPartyRows.Add((row.ROWID, cached.SkipReason ?? "unknown"));
                        continue;
                    }

                    foreach (var txn in cached.Transactions!)
                    {
                        var transactionId = txn.Id ?? string.Empty;
                        if (string.IsNullOrEmpty(transactionId)) continue;

                        var taxValue = CalculateTaxValue(txn.TaxBreakup);

                        var rowData = new Transaction
                        {
                            Transaction_ID = transactionId,
                            Reservation_ID = OrDefault(txn.RsvId, string.Empty),
                            Tax_value = taxValue,
                            HSN_Code = OrDefault(txn.HsnCode, string.Empty),
                            Product_Name = OrDefault(txn.ProdName, string.Empty),
                            Amount = OrDefault(txn.PriceBfDisc, "0"),
                            Rate = OrDefault(txn.NetTotal, "0")
                        };
                        Console.WriteLine($"row data {rowData}");
                        TrackContribution(row.ROWID, transactionId);

                        if (transactionMap.ContainsKey(transactionId))
                        {
                            _logger.LogInformation("Transaction Exists => {TransactionId}", transactionId);
                        }
                        else
                        {
                            _logger.LogInformation("Transaction Insert => {TransactionId}", transactionId);
                        }

                        // ---- ROUTE: INSERT or UPDATE ----
                        if (transactionMap.TryGetValue(transactionId, out var existingRowId))
                        {
                            if (!seenTransactionUpdate.Contains(transactionId))
                            {
                                rowData.ROWID = (int)existingRowId;
                                transactionUpdateRows.Add(rowData);
                                seenTransactionUpdate.Add(transactionId);
                            }
                            else
                            {
                                _logger.LogInformation("Duplicate Transaction Skipped => {TransactionId}", transactionId);
                            }
                        }
                        else if (!seenTransactionInsert.Contains(transactionId))
                        {
                            transactionInsertRows.Add(rowData);
                            seenTransactionInsert.Add(transactionId);
                            // Prevent duplicate INSERT if the same ID appears again later in this batch.
                            transactionMap[transactionId] = PendingRowIdSentinel;
                        }
                    }

                    processedRows++;
                }
                catch (Exception rowErr)
                {
                    _logger.LogWarning(rowErr, "Row error ROWID {RowId}", row.ROWID);
                    failedThirdPartyRows.Add((row.ROWID, rowErr.ToString()));
                }
            }

            _logger.LogInformation("{Summary}", JsonHelper.Serialize(new
            {
                transactionInsertRows = transactionInsertRows.Count,
                transactionUpdateRows = transactionUpdateRows.Count
            }));

            // =========================================================
            // BATCH WRITES
            // =========================================================

            var failedTransactionIds = new HashSet<string>();
            var writeErrorsByKey = new Dictionary<string, string>();

            if (transactionInsertRows.Count > 0)
            {
                var (confirmed, failed) = await RunBatchesAsync(
                    transactionInsertRows,
                    (batch, token) => _transactionRepository.InsertRowsAsync(batch, token),
                    "Transaction Insert", ct);

                totalTransactionInserted += confirmed;
                _logger.LogInformation("Transaction Insert Count => {Count}", confirmed);

                foreach (var f in failed)
                {
                    failedTransactionIds.Add(f.Row.Transaction_ID);
                    writeErrorsByKey[f.Row.Transaction_ID] = f.Error;
                }
            }

            if (transactionUpdateRows.Count > 0)
            {
                var (confirmed, failed) = await RunBatchesAsync(
                    transactionUpdateRows,
                    (batch, token) => _transactionRepository.UpdateRowsAsync(batch, token),
                    "Transaction Update", ct);

                totalTransactionUpdated += confirmed;
                _logger.LogInformation("Transaction Update Count => {Count}", confirmed);

                foreach (var f in failed)
                {
                    failedTransactionIds.Add(f.Row.Transaction_ID);
                    writeErrorsByKey[f.Row.Transaction_ID] = f.Error;
                }
            }

            // =========================================================
            // RESOLVE THIRDPARTYDATA STATUS PER ROW
            // =========================================================

            var alreadyFailedRowIds = new HashSet<long>(failedThirdPartyRows.Select(r => r.RowId));

            foreach (var cached in parsedCache)
            {
                var rowId = cached.Row.ROWID;

                if (cached.Skip || alreadyFailedRowIds.Contains(rowId)) continue;

                if (!rowContributions.TryGetValue(rowId, out var contribution))
                {
                    // Row had no Transaction_IDs to write — treat as processed, nothing failed.
                    processedThirdPartyRows.Add(rowId);
                    continue;
                }

                var rowFailed = false;
                var errorReason = string.Empty;

                foreach (var tId in contribution)
                {
                    if (failedTransactionIds.Contains(tId))
                    {
                        rowFailed = true;
                        errorReason = writeErrorsByKey.TryGetValue(tId, out var err) ? err : "Transaction write failed";
                        break;
                    }
                }

                if (rowFailed)
                {
                    failedThirdPartyRows.Add((rowId, errorReason));
                }
                else
                {
                    processedThirdPartyRows.Add(rowId);
                }
            }

            // =========================================================
            // WRITE STATUS BACK TO ThirdPartyData
            // =========================================================

            if (processedThirdPartyRows.Count > 0)
            {
                var successUpdates = processedThirdPartyRows
                    .Select(rowId => new ThirdPartyDataStatusUpdate
                    {
                        ROWID = rowId,
                        Status = "Processed",
                        Response = "Transaction Sync Completed"
                    })
                    .ToList();
                Console.WriteLine($"success state{successUpdates}");
                var (_, statusFailures) = await RunBatchesAsync(
                    successUpdates,
                    (batch, token) => _thirdPartyDataRepository.UpdateTransactionStatusBatchAsync(batch, token),
                    "ThirdPartyData Status Update (Processed)", ct);

                foreach (var rowId in processedThirdPartyRows)
                {
                    _logger.LogInformation("ThirdPartyData ROWID {RowId} marked Processed", rowId);
                }

                if (statusFailures.Count > 0)
                {
                    _logger.LogWarning(
                        "Failed to write Processed status for {Count} ThirdPartyData row(s)", statusFailures.Count);
                }
            }

            foreach (var (rowId, error) in failedThirdPartyRows)
            {
                try
                {
                    await _thirdPartyDataRepository.UpdateTransactionStatusAsync(
                        rowId, "Failed", string.IsNullOrEmpty(error) ? "Unknown error" : error, ct);

                    _logger.LogInformation("ThirdPartyData ROWID {RowId} marked Failed. Reason: {Reason}", rowId, error);
                }
                catch (Exception statusErr)
                {
                    _logger.LogWarning(statusErr, "Failed to write Failed status for ROWID {RowId}", rowId);
                }
            }

            // =========================================================
            // FINAL RESPONSE
            // =========================================================

            var summary = new TransactionSyncSummary
            {
                Status = "success",
                ProcessedRows = processedRows,
                TotalTransactionInserted = totalTransactionInserted,
                TotalTransactionUpdated = totalTransactionUpdated,
                ExecutionStoppedEarly = executionStoppedEarly,
                ProcessedThirdPartyRows = processedThirdPartyRows.Count,
                FailedThirdPartyRows = failedThirdPartyRows.Count,
                TransactionIdsFound = pageTransactionIds.Count,
                UniqueTransactionIds = pageTransactionIds.Distinct().Count(),
                TransactionMapCount = transactionMap.Count,
                TransactionInsertRowsCount = transactionInsertRows.Count,
                TransactionUpdateRowsCount = transactionUpdateRows.Count,
                //ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };

            //_logger.LogInformation("Execution Time (ms) => {Elapsed}", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("SUMMARY: {Summary}", JsonHelper.Serialize(summary));

            return summary;
        }
        catch (Exception err)
        {
            _logger.LogError(err, "MAIN ERROR");

            return new TransactionSyncSummary
            {
                Status = "error",
                Message = err.ToString(),
                ProcessedRows = processedRows,
                TotalTransactionInserted = totalTransactionInserted,
                TotalTransactionUpdated = totalTransactionUpdated,
                ExecutionStoppedEarly = executionStoppedEarly,
                ProcessedThirdPartyRows = processedThirdPartyRows.Count,
                FailedThirdPartyRows = failedThirdPartyRows.Count,
                //ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    // =========================================================
    // HELPER: RunBatchesAsync — mirrors the local runBatches() helper in the
    // original: slices rows into _batchSize chunks, tries a batch operation,
    // and on failure falls back to per-row calls so one bad batch never stops
    // the rest.
    // =========================================================
    private async Task<(int Confirmed, List<(T Row, string Error)> Failed)> RunBatchesAsync<T>(
        IReadOnlyList<T> rows,
        Func<IReadOnlyList<T>, CancellationToken, Task<int>> operation,
        string label,
        CancellationToken ct)
    {
        var confirmed = 0;
        var failed = new List<(T Row, string Error)>();

        for (var i = 0; i < rows.Count; i += _batchSize)
        {
            var batch = rows.Skip(i).Take(_batchSize).ToList();
            var batchNum = (i / _batchSize) + 1;

            try
            {
                _logger.LogInformation("{Label} — processing batch {BatchNum} ({Count} rows)", label, batchNum, batch.Count);
                var affected = await operation(batch, ct);
                confirmed += affected;
            }
            catch (Exception batchErr)
            {
                _logger.LogWarning(batchErr, "{Label} batch {BatchNum} failed — falling back to per-row", label, batchNum);

                foreach (var singleRow in batch)
                {
                    try
                    {
                        await operation(new List<T> { singleRow }, ct);
                        confirmed++;
                    }
                    catch (Exception rowErr)
                    {
                        _logger.LogWarning(rowErr, "{Label} per-row fallback failed", label);
                        failed.Add((singleRow, rowErr.ToString()));
                    }
                }
            }
        }

        return (confirmed, failed);
    }

    // =========================================================
    // HELPER: ExtractTransactions — mirrors extractTransactions(): supports
    // both known payload shapes ("response.data.transactions" and
    // "hotelogix.response.data.transactions"), checked in that order.
    // =========================================================
    private static (List<TransactionSyncItem> Transactions, string HotelId) ExtractTransactions(JsonElement parsed)
    {
        if (TryGetTransactionsArray(parsed, out var arr1, out var hotelId1))
        {
            return (DeserializeTransactionItems(arr1), hotelId1);
        }

        if (parsed.ValueKind == JsonValueKind.Object &&
            parsed.TryGetProperty("hotelogix", out var hotelogixEl) &&
            hotelogixEl.ValueKind == JsonValueKind.Object &&
            TryGetTransactionsArray(hotelogixEl, out var arr2, out var hotelId2))
        {
            return (DeserializeTransactionItems(arr2), hotelId2);
        }

        return (new List<TransactionSyncItem>(), string.Empty);
    }

    private static bool TryGetTransactionsArray(JsonElement container, out JsonElement transactionsArray, out string hotelId)
    {
        transactionsArray = default;
        hotelId = string.Empty;

        if (container.ValueKind != JsonValueKind.Object) return false;
        if (!container.TryGetProperty("response", out var responseEl) || responseEl.ValueKind != JsonValueKind.Object) return false;
        if (!responseEl.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object) return false;
        if (!dataEl.TryGetProperty("transactions", out var txnsEl) || txnsEl.ValueKind != JsonValueKind.Array) return false;

        transactionsArray = txnsEl;

        if (responseEl.TryGetProperty("hotelId", out var hotelIdEl))
        {
            hotelId = hotelIdEl.ValueKind switch
            {
                JsonValueKind.String => hotelIdEl.GetString() ?? string.Empty,
                JsonValueKind.Number => hotelIdEl.GetRawText(),
                _ => string.Empty
            };
        }

        return true;
    }

    private static List<TransactionSyncItem> DeserializeTransactionItems(JsonElement arr)
    {
        var list = JsonSerializer.Deserialize<List<TransactionSyncItem>>(arr.GetRawText(), JsonHelper.Options);
        return list ?? new List<TransactionSyncItem>();
    }

    // =========================================================
    // HELPER: CalculateTaxValue — mirrors calculateTaxValue(): sums all
    // taxBreakup[].amount values. Does NOT check taxName. Does NOT use
    // taxValue or transaction.tax. Returns 0 when taxBreakup is missing/empty.
    // =========================================================
    private decimal CalculateTaxValue(List<TransactionTaxBreakupItem>? taxBreakup)
    {
        if (taxBreakup is null || taxBreakup.Count == 0)
        {
            _logger.LogInformation("Tax Breakup empty — Tax_value = 0");
            return 0m;
        }

        _logger.LogInformation("Tax Breakup Count => {Count}", taxBreakup.Count);

        var totalTax = 0m;
        foreach (var entry in taxBreakup)
        {
            var amount = ParseDecimalOrZero(entry.Amount);
            _logger.LogInformation("Individual Tax Amount => {Amount}", amount);
            totalTax += amount;
        }

        var taxValue = Math.Round(totalTax, 10, MidpointRounding.AwayFromZero);
        _logger.LogInformation("Final Calculated Tax_value => {TaxValue}", taxValue);
        return taxValue;
    }

    private static decimal ParseDecimalOrZero(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    /// <summary>Mirrors JS's `value || fallback` for the string fields read off each transaction.</summary>
    private static string OrDefault(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

    /// <summary>Per-ThirdPartyData-row parse result, cached in Pass 1 so Pass 2 never re-parses.</summary>
    private sealed class ParsedThirdPartyRow
    {
        public required ThirdPartyData Row { get; init; }
        public bool Skip { get; init; }
        public string? SkipReason { get; init; }
        public List<TransactionSyncItem>? Transactions { get; init; }

        public static ParsedThirdPartyRow Skipped(ThirdPartyData row, string reason) =>
            new() { Row = row, Skip = true, SkipReason = reason };

        public static ParsedThirdPartyRow Parsed(ThirdPartyData row, List<TransactionSyncItem> transactions) =>
            new() { Row = row, Skip = false, Transactions = transactions };
    }
}
