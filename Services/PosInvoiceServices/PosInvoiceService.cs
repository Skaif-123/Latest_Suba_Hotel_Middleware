using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.InvoiceIngest.Configuration;
using AgentSyncConsole.InvoiceIngest.Constants;
using AgentSyncConsole.InvoiceIngest.Extensions;
using AgentSyncConsole.InvoiceIngest.Helpers;
using AgentSyncConsole.InvoiceIngest.Utilities;

using Dapper;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentSyncConsole.Interfaces.PosInvoiceInterface;
using AgentSyncConsole.DTOs;
using AgentSyncConsole.Models.PosInoviceModel;



namespace AgentSyncConsole.Services.PosInvoiceServices
{


    /// <summary>
    /// Flow 1 — Hotelogix JSON -> SQL for POS Invoices. Follows exactly the same
    /// architecture as AgentSyncConsole.InvoiceIngest.Services.InvoiceSyncService:
    /// page a bounded set of unprocessed ThirdPartyData rows, parse each row's
    /// JSON with the same JS-parity helpers (JsCompat / JsonElementExtensions /
    /// StringExtensions), route each header/line item into insert or update lists
    /// via a ROWID map, write in batches through the shared BatchRunner, and mark
    /// each ThirdPartyData row Processed/Failed exactly like Invoice does — the
    /// only differences are the JSON root path (hotelogix.response.data.posInvoice
    /// instead of ...data.invoices), the source ThirdPartyData column
    /// (posInvoice instead of invoice), and the target tables (PosInvoice /
    /// Posinvoice_LIneItem instead of Invoice / Invoice_LineItem).
    ///
    /// ThirdPartyData is read directly via SqlConnectionFactory + Dapper (the same
    /// shared connection factory every repository in the project uses) because
    /// the existing IThirdPartyRepository / IThirdPartyDataRepository
    /// implementations are hard-coded to the "invoice" and "transactions" columns
    /// respectively; POS Invoice reuses the exact same syncstatus / syncresponse /
    /// syncTime tracking columns on ThirdPartyData that those two already share,
    /// rather than introducing new tracking columns.
    /// </summary>
    public sealed class PosInvoiceService : IPosInvoiceService
    {
        private readonly SqlConnectionFactory _sqlFactory;
        private readonly IPosInvoiceRepository _invoiceRepository;
        private readonly IPosInvoiceLineItemRepository _lineItemRepository;
        private readonly ILogger<PosInvoiceService> _logger;
        private readonly SyncSettings _settings;

        public PosInvoiceService(
            SqlConnectionFactory sqlFactory,
            IPosInvoiceRepository invoiceRepository,
            IPosInvoiceLineItemRepository lineItemRepository,
            IOptions<SyncSettings> settings,
            ILogger<PosInvoiceService> logger)
        {
            _sqlFactory = sqlFactory;
            _invoiceRepository = invoiceRepository;
            _lineItemRepository = lineItemRepository;
            _settings = settings.Value;
            _logger = logger;
        }

        private sealed class PosThirdPartyRow
        {
            public int ROWID { get; set; }
            public string? PosInvoice { get; set; }
        }

        public async Task<PosInvoiceSyncSummary> RunOnceAsync(CancellationToken ct = default)
        {
            var summary = new PosInvoiceSyncSummary();

            try
            {
                var pageSize = _settings.PageSize;
                var batchSize = _settings.BatchSize;
                var inChunk = _settings.InChunk;

                // =========================
                // STEP 1:extracting json using select , from thirdparty
                // =========================
                var rows = await FetchUnprocessedPosInvoiceRowsAsync(pageSize, ct);
                _logger.LogInformation("POS Invoice rows fetched this execution: {Count}", rows.Count);

                if (rows.Count == 0)
                {
                    summary.Status = SyncConstants.SummaryStatusSuccess;
                    return summary;
                }

                var invoiceInsertRows = new List<PosInvoice>();
                var invoiceUpdateRows = new List<PosInvoice>();
                var lineItemInsertRows = new List<PosInvoiceLineItem>();
                var lineItemUpdateRows = new List<PosInvoiceLineItem>();

                var seenInvoiceInsert = new HashSet<string>();
                var seenLineItemInsert = new HashSet<string>();

                var processedRowIds = new List<PosThirdPartyRowOutcome>();
                var failedRowIds = new List<PosThirdPartyRowOutcome>();

                // ---- PASS 1: parse rows, collect invoice ids for the map lookups ----
                var parsed = new List<(PosThirdPartyRow Row, List<JsonElement> Invoices, string HotelId, bool Skip, string? SkipReason)>();
                var pageInvoiceIds = new List<string>();

                foreach (var row in rows)
                {
                    var raw = (row.PosInvoice ?? string.Empty).Trim();

                    if (JsCompat.IsEmptyInvoicePayload(raw))
                    {
                        parsed.Add((row, new List<JsonElement>(), string.Empty, true, "empty posInvoice field"));
                        continue;
                    }

                    try
                    {
                        var root = JsCompat.ParseWithDoubleDecode(raw);
                        var (invoices, hotelId) = ExtractPosInvoices(root);

                        foreach (var inv in invoices)
                        {
                            var id = inv.TryGetPropertySafe("id").AsJsString();
                            if (!string.IsNullOrEmpty(id)) pageInvoiceIds.Add(id);
                        }

                        parsed.Add((row, invoices, hotelId, false, null));
                    }
                    catch (Exception parseErr)
                    {
                        _logger.LogInformation("POS Invoice parse error ROWID {RowId}: {Error}", row.ROWID, parseErr.ToJsString());
                        parsed.Add((row, new List<JsonElement>(), string.Empty, true, "JSON parse error: " + parseErr.ToJsString()));
                    }
                }

                var invoiceMap = pageInvoiceIds.Count > 0
                    ? await _invoiceRepository.QueryInvoiceMapAsync(pageInvoiceIds, inChunk, ct)
                    : new Dictionary<string, string>();
                var lineItemMap = pageInvoiceIds.Count > 0
                    ? await _lineItemRepository.QueryLineItemMapAsync(pageInvoiceIds, inChunk, ct)
                    : new Dictionary<string, string>();

                var rowContributions = new Dictionary<int, PosRowContribution>();
                void TrackContribution(int rowId, string? invoiceId, string? lineItemKey)
                {
                    if (!rowContributions.TryGetValue(rowId, out var c))
                    {
                        c = new PosRowContribution();
                        rowContributions[rowId] = c;
                    }
                    if (!string.IsNullOrEmpty(invoiceId)) c.InvoiceIds.Add(invoiceId);
                    if (!string.IsNullOrEmpty(lineItemKey)) c.LineItemKeys.Add(lineItemKey);
                }

                // ---- PASS 2: build insert/update rows ----
                var processedRows = 0;

                foreach (var entry in parsed)
                {
                    if (entry.Skip)
                    {
                        failedRowIds.Add(new PosThirdPartyRowOutcome { ROWID = entry.Row.ROWID, Error = entry.SkipReason });
                        continue;
                    }

                    try
                    {
                        foreach (var data in entry.Invoices)
                        {
                            var invoiceID = data.TryGetPropertySafe("id").AsJsString();
                            if (string.IsNullOrEmpty(invoiceID)) continue;

                            var paymentsProp = data.TryGetPropertySafe("payments");
                            var firstPayment = paymentsProp.IsArray()
                                ? paymentsProp!.Value.EnumerateArray().FirstOrDefault()
                                : (JsonElement?)null;

                            var posInvoiceRow = new PosInvoice
                            {
                                Invoice_ID = invoiceID,
                                Invoice_Number = data.TryGetPropertySafe("invoiceNumber").AsJsString(),
                                Invoice_No = data.TryGetPropertySafe("folioNo").AsJsString(),
                                posPointId = data.TryGetPropertySafe("posPointId").AsJsString(),
                                posPointName = data.TryGetPropertySafe("posPointName").AsJsString(),
                                Invoice_status = data.TryGetPropertySafe("status").AsJsString(),
                                Owner_Type = data.TryGetPropertySafe("ownerType").AsJsString(),
                                GSTin_ID = data.TryGetPropertySafe("gstinId").AsJsString(),
                                Subtotal = data.TryGetPropertySafe("subtotal").AsJsNumber(),
                                Tax = data.TryGetPropertySafe("tax").AsJsNumber(),
                                NetTotal = data.TryGetPropertySafe("netTotal").AsJsNumber(),
                                Discount = data.TryGetPropertySafe("discount").AsJsNumber(),
                                CreatedOn = data.TryGetPropertySafe("createdOn").AsJsString(),
                                SettledOn = data.TryGetPropertySafe("settledOn").AsJsString(),
                                IsComplimentary = data.TryGetPropertySafe("isComplimentary").AsJsString(),
                                IsRefund = data.TryGetPropertySafe("isRefund").AsJsString(),
                                GuestID = data.TryGetPropertySafe("guestId").AsJsString(),
                                InvoiceType = data.TryGetPropertySafe("invoiceType").AsJsString(),
                                HotelID = entry.HotelId,
                                Payment_Term = 
                                    firstPayment?.TryGetPropertySafe("paymentMode").AsJsString()
                                    ?? string.Empty
                            };

                            TrackContribution(entry.Row.ROWID, invoiceID, null);
                            Console.WriteLine(posInvoiceRow.Payment_Term);

                            if (invoiceMap.TryGetValue(invoiceID, out var existingRowId))
                            {
                                posInvoiceRow.ROWID = int.Parse(existingRowId);
                                invoiceUpdateRows.Add(posInvoiceRow);
                            }
                            else if (seenInvoiceInsert.Add(invoiceID))
                            {
                                invoiceInsertRows.Add(posInvoiceRow);
                                invoiceMap[invoiceID] = SyncConstants.PendingSentinel;
                            }

                            // ---- LINE ITEMS ----
                            var lineItemsProp = data.TryGetPropertySafe("lineItems");
                            if (!lineItemsProp.IsArray()) continue;

                            foreach (var li in lineItemsProp!.Value.EnumerateArray())
                            {
                                var productName = li.TryGetPropertySafe("productName").AsJsString();
                                var hsnCode = li.TryGetPropertySafe("hsnCode").AsJsString();
                                var totalPrice = li.TryGetPropertySafe("totalPrice").AsJsNumber();

                                var (gstType, taxRate) = SummarizeTaxBreakup(li.TryGetPropertySafe("taxBreakup"));

                                var liKey = $"{invoiceID}_{productName}_{hsnCode}_{totalPrice}";
                                TrackContribution(entry.Row.ROWID, null, liKey);
                                double taxValue = 0;

                                if (li.TryGetProperty("taxBreakup", out JsonElement taxBreakup) &&
                                    taxBreakup.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var tax in taxBreakup.EnumerateArray())
                                    {
                                        if (tax.TryGetProperty("taxValue", out JsonElement taxValueElement))
                                        {
                                            if (double.TryParse(taxValueElement.GetString(), out double value))
                                            {
                                                taxValue += value;
                                            }
                                        }
                                    }
                                }
                                var lineItemRow = new PosInvoiceLineItem
                                {
                                    Invoice_ID = invoiceID,
                                    Product_Name = productName,
                                    hsnCode = hsnCode,
                                    Quantity = li.TryGetPropertySafe("quantity").AsJsString(),
                                    Unit_Price = li.TryGetPropertySafe("unitPrice").AsJsNumber(),
                                    Total_Price = totalPrice,
                                    TaxValue = taxValue,
                                    NetTotal = li.TryGetPropertySafe("netTotal").AsJsNumber(),
                                    Tax_Rate = taxRate,
                                    GST_Type = gstType
                                };

                                if (lineItemMap.TryGetValue(liKey, out var existingLiRowId))
                                {
                                    lineItemRow.ROWID = int.Parse(existingLiRowId);
                                    lineItemUpdateRows.Add(lineItemRow);
                                }
                                else if (seenLineItemInsert.Add(liKey))
                                {
                                    lineItemInsertRows.Add(lineItemRow);
                                    lineItemMap[liKey] = SyncConstants.PendingSentinel;
                                }
                            }
                        }

                        processedRows++;
                    }
                    catch (Exception rowErr)
                    {
                        _logger.LogInformation("POS Invoice row error ROWID {RowId}: {Error}", entry.Row.ROWID, rowErr.ToJsString());
                        failedRowIds.Add(new PosThirdPartyRowOutcome { ROWID = entry.Row.ROWID, Error = rowErr.ToJsString() });
                    }
                }

                // =========================
                // BATCH WRITES (shared BatchRunner — same retry/fallback semantics as Invoice)
                // =========================
                var failedInvoiceIds = new HashSet<string>();
                var failedLineItemKeys = new HashSet<string>();

                //Console.WriteLine("We are going to insert the posinvoice");
                if (invoiceInsertRows.Count > 0)
                {
                    var res = await BatchRunner.RunBatchesAsync(invoiceInsertRows,
                        batch => _invoiceRepository.InsertRowsAsync(batch, ct), "PosInvoice Insert", batchSize, _logger, ct);
                    summary.TotalInvoicesInserted += res.Confirmed;
                    foreach (var f in res.Failed) failedInvoiceIds.Add(f.Row.Invoice_ID);
                }

                Console.WriteLine("We are updating posInvoice");
                if (invoiceUpdateRows.Count > 0)
                {
                    var res = await BatchRunner.RunBatchesAsync(invoiceUpdateRows,
                        batch => _invoiceRepository.UpdateRowsAsync(batch, ct), "PosInvoice Update", batchSize, _logger, ct);
                    summary.TotalInvoicesUpdated += res.Confirmed;
                    foreach (var f in res.Failed) failedInvoiceIds.Add(f.Row.Invoice_ID);
                }


                Console.WriteLine("We are now inserting in the posInvopice LineItem");
                if (lineItemInsertRows.Count > 0)
                {
                    var res = await BatchRunner.RunBatchesAsync(lineItemInsertRows,
                        batch => _lineItemRepository.InsertRowsAsync(batch, ct), "PosInvoiceLineItem Insert", batchSize, _logger, ct);
                    summary.TotalLineItemsInserted += res.Confirmed;
                    foreach (var f in res.Failed) failedLineItemKeys.Add($"{f.Row.Invoice_ID}_{f.Row.Product_Name}_{f.Row.hsnCode}_{f.Row.Total_Price}");
                }


                if (lineItemUpdateRows.Count > 0)
                {
                    var res = await BatchRunner.RunBatchesAsync(lineItemUpdateRows,
                        batch => _lineItemRepository.UpdateRowsAsync(batch, ct), "PosInvoiceLineItem Update", batchSize, _logger, ct);
                    summary.TotalLineItemsUpdated += res.Confirmed;
                    foreach (var f in res.Failed) failedLineItemKeys.Add($"{f.Row.Invoice_ID}_{f.Row.Product_Name}_{f.Row.hsnCode}_{f.Row.Total_Price}");
                }

                // =========================
                // RESOLVE + WRITE BACK ThirdPartyData STATUS
                // =========================
                var alreadyFailed = failedRowIds.Select(r => r.ROWID).ToHashSet();

                foreach (var entry in parsed)
                {
                    var rowId = entry.Row.ROWID;
                    if (entry.Skip || alreadyFailed.Contains(rowId)) continue;

                    var failed = false;

                    if (rowContributions.TryGetValue(rowId, out var contribution))
                    {
                        failed = contribution.InvoiceIds.Any(failedInvoiceIds.Contains)
                            || contribution.LineItemKeys.Any(failedLineItemKeys.Contains);
                    }

                    if (failed)
                    {
                        failedRowIds.Add(new PosThirdPartyRowOutcome { ROWID = rowId, Error = "POS Invoice write failed" });
                    }
                    else
                    {
                        processedRowIds.Add(new PosThirdPartyRowOutcome { ROWID = rowId });
                    }
                }

                foreach (var r in processedRowIds)
                {
                    await UpdateThirdPartyStatusAsync(r.ROWID, SyncConstants.StatusProcessed, "POS Invoice Sync Completed", ct);
                }

                foreach (var r in failedRowIds)
                {
                    await UpdateThirdPartyStatusAsync(r.ROWID, SyncConstants.StatusFailed, r.Error ?? SyncConstants.ResponseUnknownError, ct);
                }

                summary.Status = SyncConstants.SummaryStatusSuccess;
                summary.ProcessedRows = processedRows;
                summary.ProcessedThirdPartyRows = processedRowIds.Count;
                summary.FailedThirdPartyRows = failedRowIds.Count;

                _logger.LogInformation("POS Invoice sync summary: {Summary}", JsonHelper.Serialize(summary));
                return summary;
            }
            catch (Exception err)
            {
                _logger.LogError(err, "PosInvoiceService.RunOnceAsync failed");
                summary.Status = SyncConstants.SummaryStatusError;
                summary.Message = err.ToJsString();
                return summary;
            }
        }

        // =========================
        // ThirdPartyData ACCESS (posInvoice column; shared syncstatus/syncresponse/syncTime
        // columns, same as AgentSyncConsole.InvoiceIngest.Repositories.ThirdPartyRepository
        // and AgentSyncConsole.Repositories.ThirdPartyDataRepository already use)
        // =========================

        private async Task<List<PosThirdPartyRow>> FetchUnprocessedPosInvoiceRowsAsync(int pageSize, CancellationToken ct)
        {
            const string sql = @"
            SELECT  ROWID, posInvoice AS PosInvoice
            FROM ThirdPartyData
            WHERE posInvoice IS NOT NULL
            AND ISNULL(syncstatus,'') = ''
            ORDER BY ROWID ASC";

            using var conn = await _sqlFactory.CreateOpenConnectionAsync(ct);
            var rows = await conn.QueryAsync<PosThirdPartyRow>(
                new CommandDefinition(sql, new { PageSize = pageSize }, cancellationToken: ct));
            return rows.AsList();
        }

        private async Task UpdateThirdPartyStatusAsync(int rowId, string status, string response, CancellationToken ct)
        {
            const string sql = @"
            UPDATE ThirdPartyData
            SET syncstatus = @Status, syncresponse = @Response, syncTime = SYSDATETIME()
            WHERE ROWID = @ROWID";

            using var conn = await _sqlFactory.CreateOpenConnectionAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(
                sql, new { Status = status, Response = response, ROWID = rowId }, cancellationToken: ct));
        }

        // =========================
        // JSON HELPERSpa
        // =========================

        /// <summary>
        /// Root path per spec: hotelogix.response.data.posInvoice. Also accepts
        /// response.data.posInvoice (no hotelogix wrapper) for the same defensive
        /// reason InvoiceSyncService.ExtractInvoices() supports two shapes.
        /// </summary>
        private static (List<JsonElement> Invoices, string HotelId) ExtractPosInvoices(JsonElement parsed)
        {
            if (parsed.TryGetProperty("hotelogix", out var hotelogix) &&
                hotelogix.TryGetProperty("response", out var response) &&
                response.TryGetProperty("data", out var data) &&
                data.TryGetProperty("posInvoice", out var posInvoice))
            {
                string hotelId = response.TryGetProperty("hotelId", out var hid)
                    ? hid.ToString()
                    : response.TryGetProperty("hotelID", out var hid2)
                        ? hid2.ToString()
                        : string.Empty;

                if (posInvoice.ValueKind == JsonValueKind.Array)
                {
                    return (posInvoice.EnumerateArray().ToList(), hotelId);
                }

                if (posInvoice.ValueKind == JsonValueKind.Object)
                {
                    return (new List<JsonElement> { posInvoice }, hotelId);
                }
            }

            return (new List<JsonElement>(), string.Empty);
        }

        /// <summary>
        /// Determines GST Type ("GST" for CGST+SGST, "IGST" for IGST) and the
        /// combined tax percentage directly from lineItems[].taxBreakup[], per
        /// spec — no Transaction JSON/table is read. Field names are matched
        /// defensively (type/name/taxType, percentage/rate/percent) since the
        /// sample payload is illustrative only, not the full contract.
        /// </summary>
        private static (string GstType, decimal TaxRate) SummarizeTaxBreakup(JsonElement? taxBreakup)
        {
            if (!taxBreakup.IsArray()) return (string.Empty, 0m);

            decimal cgst = 0m, sgst = 0m, igst = 0m;
            var sawAny = false;

            foreach (var entry in taxBreakup!.Value.EnumerateArray())
            {
                var type = (entry.TryGetPropertySafe("type").AsJsString()
                    is { Length: > 0 } t ? t
                    : entry.TryGetPropertySafe("name").AsJsString() is { Length: > 0 } n ? n
                    : entry.TryGetPropertySafe("taxType").AsJsString()).ToUpperInvariant();

                var percentageStr = entry.TryGetPropertySafe("percentage").AsJsString();
                if (string.IsNullOrEmpty(percentageStr)) percentageStr = entry.TryGetPropertySafe("rate").AsJsString();
                if (string.IsNullOrEmpty(percentageStr)) percentageStr = entry.TryGetPropertySafe("percent").AsJsString();

                if (!decimal.TryParse(percentageStr, out var pct)) continue;

                sawAny = true;

                if (type.Contains("IGST")) igst += pct;
                else if (type.Contains("CGST")) cgst += pct;
                else if (type.Contains("SGST")) sgst += pct;
            }

            if (!sawAny) return (string.Empty, 0m);

            return igst > 0m ? ("IGST", igst) : ("GST", cgst + sgst);
        }

     
    }

}
