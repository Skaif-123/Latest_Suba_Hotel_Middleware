
using System.Text.Json;
using AgentSyncConsole.InvoiceIngest.Extensions;
using AgentSyncConsole.InvoiceIngest.Helpers;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.InvoiceIngest.Models;
using Dapper;
using Microsoft.Extensions.Logging;



namespace AgentSyncConsole.Services
{

    /// <summary>
    /// Replaces the exported Catalyst function
    ///   module.exports = async (context, basicIO) => { ... }
    /// One call to RunAsync() == one Catalyst function execution ==
    /// basicIO.write(JSON.stringify(summary)).
    ///
    /// Catalyst -> C# mapping:
    ///   catalyst.initialize / app.zcql() / app.datastore()      -> IDbConnectionFactory + Dapper
    ///   zcql.executeZCQLQuery('SELECT ROWID, invoice FROM ThirdPartyData')
    ///                                                            -> reuses AgentSyncConsole.InvoiceIngest.Models.ThirdPartyDataRow
    ///   ds.table('Credit_Note')                                 -> Credit_Note table (raw parameterized SQL below)
    ///   ds.table('Credit_Note_LineItem')                        -> Credit_Note_LineItem table (raw parameterized SQL below)
    ///   basicIO.write(JSON.stringify(summary))                  -> CreditNoteSyncResult returned + written to Console.Out
    ///
    /// The dynamic/recursive folioType finder, both credit-note detection paths
    /// (direct data.folioType and the recursive search fallback), the folioType
    /// summary counters, the unknown-folioType diagnostic capture, the
    /// same-execution duplicate-prevention sets, the datastore duplicate checks
    /// (Credit_Note by Customer_Name + Credit_Note_No, Credit_Note_LineItem by
    /// InvoiceID + Item_Description + Amount + Rate), and all error handling are
    /// preserved exactly, in the same execution order as the original.
    /// </summary>
    public sealed class CreditNoteSyncService : ICreditNoteSyncService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<CreditNoteSyncService> _logger1;

        public CreditNoteSyncService(IDbConnectionFactory connectionFactory, ILogger<CreditNoteSyncService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger1 = logger;
        }

        public async Task<CreditNoteSyncResult> RunAsync(CancellationToken cancellationToken = default)
        {
            // =========================
            // 🔹 COUNTERS
            // =========================
            var totalThirdPartyRows = 0;
            var totalValidJSONRows = 0;
            var totalInvalidJSONRows = 0;
            var totalInvoicesFound = 0;
            var totalCreditNotesFound = 0;

            // =========================
            // 🔹 FOLIO TYPE SUMMARY
            // Counts every folioType value encountered across all processed
            // invoices, so it is immediately obvious whether any "CN" invoices
            // exist in the data at all.
            // =========================
            var folioTypeSummary = new Dictionary<string, int>();

            // =========================
            // 🔹 UNKNOWN FOLIO TYPE CAPTURE
            // Stores complete diagnostic information for every invoice whose
            // folioType could not be determined, so the root cause can be
            // inspected directly from the response.
            // =========================
            var unknownFolioInvoices = new List<UnknownFolioInvoiceEntry>();

            var totalInsertedCreditNotes = 0;
            var totalUpdatedCreditNotes = 0;
            var totalInsertedCreditNoteLineItems = 0;
            var totalUpdatedCreditNoteLineItems = 0;
            var duplicateCreditNoteLinesSkipped = 0;

            var insertedCreditNotes = new List<CreditNoteRow>();
            var updatedCreditNotes = new List<CreditNoteRow>();
            var insertedCreditNoteLineItems = new List<CreditNoteLineItemRow>();
            var updatedCreditNoteLineItems = new List<CreditNoteLineItemRow>();
            var logs = new List<object>();

            try
            {
                // =========================
                // 🔹 INIT
                // =========================
                using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

                // =========================
                // 🔹 GET THIRD PARTY DATA
                // =========================
                const string selectSql = "SELECT ROWID, invoice AS Invoice FROM ThirdPartyData where (invoice like'%CN %') AND(INVOICE IS NOT NULL)";

                var result = (await connection.QueryAsync<ThirdPartyDataRow>(
                    new CommandDefinition(selectSql, cancellationToken: cancellationToken))).ToList();

                totalThirdPartyRows = result.Count;

                Console.WriteLine(totalThirdPartyRows);

                // =========================
                // 🔹 SAME-EXECUTION DUPLICATE
                // PREVENTION SETS
                // =========================
                var processedCreditNotes = new HashSet<string>();
                var processedCreditNoteLines = new HashSet<string>();

                // =========================
                // 🔹 LOOP THIRD PARTY ROWS
                // =========================
                foreach (var row in result)
                {

                    try
                    {
                        // =========================
                        // 🔹 VERIFY THIRDPARTYDATA ROW
                        // =========================
                        _logger1.LogInformation("ROWID => {RowId}", row.ROWID ?? string.Empty);
                        _logger1.LogInformation("Invoice JSON Length => {Length}", row.Invoice?.Length ?? 0);

                        var rawInvoice = (row.Invoice ?? string.Empty).Trim();

                        // ── Guard: skip empty invoice field ──
                        if (string.IsNullOrEmpty(rawInvoice) || rawInvoice == "{}" || rawInvoice == "[]")
                        {
                            _logger1.LogInformation("SKIPPING EMPTY INVOICE FIELD - ROWID: {RowId}", row.ROWID ?? string.Empty);
                            continue;
                        }

                        // =========================
                        // 🔹 PARSE JSON
                        // =========================
                        JsonElement parsed;

                        try
                        {
                            parsed = JsCompat.ParseWithDoubleDecode(rawInvoice);
                            totalValidJSONRows++;
                        }
                        catch (Exception parseErr)
                        {
                            totalInvalidJSONRows++;

                            _logger1.LogInformation("INVALID JSON ROWID => {RowId}", row.ROWID ?? string.Empty);
                            _logger1.LogInformation("INVALID JSON DATA => {Data}", row.Invoice ?? string.Empty);
                            _logger1.LogInformation("COMPLETE PARSE ERROR => {Error}", JsonSerializer.Serialize(new
                            {
                                message = parseErr.Message,
                                stack = parseErr.StackTrace
                            }));

                            logs.Add(new
                            {
                                rowid = row.ROWID ?? string.Empty,
                                invoiceData = row.Invoice ?? string.Empty,
                                error = parseErr.ToJsString()
                            });

                            continue;
                        }

                        // =========================
                        // 🔹 EXTRACT INVOICES ARRAY
                        // =========================
                        var invoices = ExtractInvoices(parsed);

                        _logger1.LogInformation("INVOICE COUNT: {Count}", invoices.Count);

                        totalInvoicesFound += invoices.Count;

                        // =========================
                        // 🔹 LOOP INVOICES
                        // =========================
                        for (var invoiceIndex = 0; invoiceIndex < invoices.Count; invoiceIndex++)
                        {
                            var data = invoices[invoiceIndex];

                            var invoiceID = data.TryGetPropertySafe("id").AsJsString();

                            if (string.IsNullOrEmpty(invoiceID))
                            {
                                continue;
                            }

                            // -----------------------------------------
                            // HOTEL_ID FROM HOTELOGIX RESPONSE
                            // Tries both known JSON path variants. Debug logs expose
                            // the actual structure so the correct path can be
                            // confirmed.
                            // -----------------------------------------
                            var hotelID = string.Empty;

                            try
                            {
                                //_logger.LogInformation("FULL JSON => {Json}", parsed.GetRawText());

                                var hxHotelIdEl = GetNested(parsed, "hotelogix", "response", "hotelId");
                                var respHotelIdEl = GetNested(parsed, "response", "hotelId");

                                _logger1.LogInformation("HOTEL ID FROM HOTELOGIX => {Val}", hxHotelIdEl?.GetRawText() ?? "null");
                                _logger1.LogInformation("HOTEL ID FROM RESPONSE => {Val}", respHotelIdEl?.GetRawText() ?? "null");

                                var hxHotelId = hxHotelIdEl.AsJsString();
                                var respHotelId = respHotelIdEl.AsJsString();

                                hotelID = !string.IsNullOrEmpty(hxHotelId)
                                    ? hxHotelId
                                    : (!string.IsNullOrEmpty(respHotelId) ? respHotelId : string.Empty);

                                _logger1.LogInformation("FINAL HOTEL ID => {HotelId}", hotelID);
                            }
                            catch (Exception hotelErr)
                            {
                                _logger1.LogInformation("Hotel ID Extraction Error => {Error}", hotelErr.ToJsString());
                            }

                            _logger1.LogInformation("INVOICE ID => {InvoiceId}", invoiceID);
                            _logger1.LogInformation("FINAL HOTEL ID => {HotelId}", hotelID);

                            // =========================
                            // 🔹 CREDIT NOTE DETECTION —
                            // DEBUG LOGGING
                            // Logged for every invoice, before the folioType check, so
                            // it is clear whether the field exists, what its value is,
                            // and whether it is nested elsewhere.
                            // =========================
                            _logger1.LogInformation("Invoice ID => {InvoiceId}", invoiceID);
                            _logger1.LogInformation("Folio Type => {FolioType}", data.TryGetPropertySafe("folioType").AsJsString());
                            _logger1.LogInformation("Complete Invoice => {Invoice}", data.GetRawText());

                            // =========================
                            // 🔹 CREDIT NOTE DETECTION
                            // Primary path: data.folioType. If that is empty, the
                            // complete invoice object is searched recursively
                            // (data.invoice.folioType, or any other nesting) instead
                            // of hardcoding one location.
                            // title.includes("credit") is no longer used anywhere.
                            // =========================
                            var rawFolioTypeEl = data.TryGetPropertySafe("folioType");
                            JsonElement? discoveredFolioTypeEl = null;

                            var rawFolioTypeMissing = rawFolioTypeEl is null
                                || rawFolioTypeEl.Value.ValueKind == JsonValueKind.Null
                                || rawFolioTypeEl.AsJsString().Trim() == string.Empty;

                            if (rawFolioTypeMissing)
                            {
                                _logger1.LogInformation("folioType missing for invoice {InvoiceId}", invoiceID);
                                _logger1.LogInformation("Complete Invoice JSON (folioType search) => {Invoice}", data.GetRawText());

                                discoveredFolioTypeEl = FindFolioType(data);

                                if (discoveredFolioTypeEl is not null)
                                {
                                    _logger1.LogInformation(
                                        "folioType located via dynamic search for invoice {InvoiceId} => {Value}",
                                        invoiceID, discoveredFolioTypeEl.Value.GetRawText());

                                    rawFolioTypeEl = discoveredFolioTypeEl;
                                }
                            }

                            var folioType = rawFolioTypeEl.AsJsString().ToUpperInvariant();

                            // ── Track every folioType value seen ──
                            var folioTypeKey = string.IsNullOrEmpty(folioType) ? "UNKNOWN" : folioType;

                            folioTypeSummary[folioTypeKey] = folioTypeSummary.GetValueOrDefault(folioTypeKey, 0) + 1;

                            if (folioTypeKey == "UNKNOWN")
                            {
                                _logger1.LogInformation("UNKNOWN FOLIO TYPE FOUND");
                                _logger1.LogInformation("ROWID => {RowId}", row.ROWID ?? string.Empty);
                                _logger1.LogInformation("Invoice Index => {Index}", invoiceIndex);
                                _logger1.LogInformation("Invoice ID => {InvoiceId}", invoiceID);
                                _logger1.LogInformation("Raw folioType => {Raw}", rawFolioTypeEl.AsJsString());
                                _logger1.LogInformation("Discovered folioType => {Discovered}", discoveredFolioTypeEl?.GetRawText() ?? "null");
                                _logger1.LogInformation("Invoice JSON => {Invoice}", data.GetRawText());
                                _logger1.LogInformation("Complete Parsed JSON => {Parsed}", parsed.GetRawText());

                                unknownFolioInvoices.Add(new UnknownFolioInvoiceEntry
                                {
                                    RowID = row.ROWID ?? string.Empty,
                                    InvoiceID = invoiceID,
                                    InvoiceIndex = invoiceIndex,
                                    RawFolioType = rawFolioTypeEl,
                                    DiscoveredFolioType = discoveredFolioTypeEl,
                                    Invoice = data,
                                    ParsedJSON = parsed
                                });
                            }

                            if (folioType != "CN")
                            {
                                _logger1.LogInformation(
                                    "Skipping Invoice {InvoiceId} because folioType = {FolioType}", invoiceID, folioType);
                                continue;
                            }

                            _logger1.LogInformation(
                                "CREDIT NOTE INVOICE FOUND | InvoiceID: {InvoiceId} | folioType: {FolioType}",
                                invoiceID, folioType);

                            totalCreditNotesFound++;

                            var lineItemsProp = data.TryGetPropertySafe("lineItems");
                            var lineItems = lineItemsProp.IsArray()
                                ? lineItemsProp!.Value.EnumerateArray().ToList()
                                : new List<JsonElement>();

                            // =========================
                            // 🔹 UNIQUE KEYS
                            // =========================
                            var customFolioNo = data.TryGetPropertySafe("customFolioNo").AsJsString();
                            var folioNo = data.TryGetPropertySafe("folioNo").AsJsString();

                            var creditNoteNo = !string.IsNullOrEmpty(customFolioNo)
                                ? customFolioNo
                                : (!string.IsNullOrEmpty(folioNo) ? folioNo : string.Empty);

                            if (string.IsNullOrEmpty(creditNoteNo))
                            {
                                continue;
                            }

                            var ownerId = data.TryGetPropertySafe("ownerId").AsJsString();
                            var creditNoteUniqueKey = data.TryGetPropertySafe("id").AsJsString();

                            // =========================
                            // 🔹 FIRST LINE ITEM
                            // Used only for the Credit Note header's Reason and
                            // Credit_Note_Date. Does not depend on the line item
                            // loop.
                            // =========================
                            var firstLine = lineItems.Count > 0 ? lineItems[0] : (JsonElement?)null;

                            // =========================
                            // 🔹 SAME-EXECUTION DUPLICATE
                            // CHECK — CREDIT NOTE
                            // =========================
                            _logger1.LogInformation("Processing Credit Note Key: {Key}", creditNoteUniqueKey);

                            if (processedCreditNotes.Contains(creditNoteUniqueKey))
                            {
                                _logger1.LogInformation(
                                    "Skipping Duplicate Credit Note In Same Execution: {Key}", creditNoteUniqueKey);
                                // Still need to process the line items so do not skip
                                // the entire iteration — only skip the header block.
                            }
                            else
                            {
                                processedCreditNotes.Add(creditNoteUniqueKey);

                                // =========================
                                // 🔹 BUILD CREDIT NOTE HEADER
                                // =========================
                                var creditNoteHeader = new CreditNoteRow
                                {
                                    Customer_Name = ownerId,
                                    Location_Name = data.TryGetPropertySafe("type").AsJsString(),
                                    Reason = firstLine?.TryGetPropertySafe("title").AsJsString() ?? string.Empty,
                                    Credit_Note_No = creditNoteNo,
                                    Credit_Note_Date = firstLine?.TryGetPropertySafe("date").AsJsString() ?? string.Empty,
                                    InvoiceID = invoiceID,
                                    Hotel_ID = hotelID,
                                    BooksID = string.Empty,
                                    Response = data.GetRawText(),
                                    ThirdpartyStatus = "processed",
                                    BooksStatus = "pending"
                                };

                                _logger1.LogInformation("Credit Note Header: {Header}", JsonSerializer.Serialize(creditNoteHeader));

                                // =========================
                                // 🔹 DUPLICATE CHECK —
                                // CREDIT NOTE IN DATASTORE
                                // =========================
                                string? existingCreditNoteRowID = null;

                                try
                                {
                                    const string cnCheckSql = """
                                    SELECT TOP (1) ROWID
                                    FROM CreditNote
                                    WHERE Customer_Name = @Customer_Name
                                    AND Credit_Note_No = @Credit_Note_No
                                   
                                    """;

                                    var cnCheckResult = await connection.QueryFirstOrDefaultAsync<string>(
                                        new CommandDefinition(
                                            cnCheckSql,
                                            new { Customer_Name = ownerId, Credit_Note_No = creditNoteNo },
                                            cancellationToken: cancellationToken));

                                    if (!string.IsNullOrEmpty(cnCheckResult))
                                    {
                                        existingCreditNoteRowID = cnCheckResult;
                                    }
                                }
                                catch (Exception cnCheckErr)
                                {
                                    _logger1.LogInformation("Credit Note Check Error: {Error}", cnCheckErr.ToJsString());
                                }

                                // =========================
                                // 🔹 INSERT OR UPDATE —
                                // CREDIT NOTE
                                // =========================
                                if (!string.IsNullOrEmpty(existingCreditNoteRowID))
                                {
                                    // ── UPDATE ──
                                    _logger1.LogInformation("Credit Note Exists - Updating");

                                    try
                                    {
                                        creditNoteHeader.ROWID = existingCreditNoteRowID;

                                        const string cnUpdateSql = """
                                        UPDATE CreditNote
                                        SET Customer_Name = @Customer_Name,
                                            Location_Name = @Location_Name,
                                            Reason = @Reason,
                                            Credit_Note_No = @Credit_Note_No,
                                            Credit_Note_Date = @Credit_Note_Date,
                                            InvoiceID = @InvoiceID,
                                            Hotel_ID = @Hotel_ID,
                                            BooksID = @BooksID,
                                            Response = @Response,
                                            ThirdpartyStatus = @ThirdpartyStatus,
                                            BooksStatus = @BooksStatus
                                        WHERE ROWID = @ROWID
                                        """;

                                        await connection.ExecuteAsync(new CommandDefinition(
                                            cnUpdateSql, creditNoteHeader, cancellationToken: cancellationToken));

                                        updatedCreditNotes.Add(creditNoteHeader);
                                        totalUpdatedCreditNotes++;
                                    }
                                    catch (Exception cnUpdateErr)
                                    {
                                        _logger1.LogInformation("Credit Note Update Error: {Error}", cnUpdateErr.ToJsString());
                                        logs.Add(new
                                        {
                                            creditNoteUniqueKey,
                                            error = "Credit Note update failed: " + cnUpdateErr.ToJsString()
                                        });
                                    }
                                }
                                else
                                {
                                    // ── INSERT ──
                                    _logger1.LogInformation("Credit Note Not Found - Inserting");

                                    try
                                    {
                                        const string cnInsertSql = """
                                        INSERT INTO CreditNote
                                            (Customer_Name, Location_Name, Reason, Credit_Note_No, Credit_Note_Date,
                                             InvoiceID, Hotel_ID, BooksID, Response, ThirdpartyStatus, BooksStatus)
                                        VALUES
                                            (@Customer_Name, @Location_Name, @Reason, @Credit_Note_No, @Credit_Note_Date,
                                             @InvoiceID, @Hotel_ID, @BooksID, @Response, @ThirdpartyStatus, @BooksStatus)
                                        """;

                                        var newRowId = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                                            cnInsertSql, creditNoteHeader, cancellationToken: cancellationToken));

                                        creditNoteHeader.ROWID = newRowId ?? string.Empty;

                                        insertedCreditNotes.Add(creditNoteHeader);
                                        totalInsertedCreditNotes++;
                                    }
                                    catch (Exception cnInsertErr)
                                    {
                                        _logger1.LogInformation("Credit Note Insert Error: {Error}", cnInsertErr.ToJsString());
                                        logs.Add(new
                                        {
                                            creditNoteUniqueKey,
                                            error = "Credit Note insert failed: " + cnInsertErr.ToJsString()
                                        });
                                    }
                                }
                            }

                            // =========================
                            // 🔹 LOOP LINE ITEMS
                            // Every line item on a CN invoice belongs to the Credit
                            // Note. No filtering is applied.
                            // =========================
                            foreach (var lineItem in lineItems)
                            {
                                // =========================
                                // 🔹 UNIQUE KEY — LINE ITEM
                                // =========================
                                var transId = lineItem.TryGetPropertySafe("transId").AsJsString();
                                var lineUniqueKey = invoiceID + "_" + transId;

                                // =========================
                                // 🔹 SAME-EXECUTION DUPLICATE
                                // CHECK — CREDIT NOTE LINE
                                // =========================
                                _logger1.LogInformation("Processing Credit Note Line Key: {Key}", lineUniqueKey);

                                if (processedCreditNoteLines.Contains(lineUniqueKey))
                                {
                                    _logger1.LogInformation(
                                        "Skipping Duplicate Credit Note Line In Same Execution: {Key}", lineUniqueKey);
                                    continue;
                                }

                                processedCreditNoteLines.Add(lineUniqueKey);

                                // =========================
                                // 🔹 BUILD CREDIT NOTE LINE ITEM
                                // =========================
                                var creditNoteLineItem = new CreditNoteLineItemRow
                                {
                                    InvoiceID = invoiceID,
                                    Item_Description = lineItem.TryGetPropertySafe("title").AsJsString(),
                                    SAC_HSN_Code = transId,
                                    Quantity = "1",
                                    Rate = lineItem.TryGetPropertySafe("netTotal").AsJsString(),
                                    Intra_Inter_TAX_Rates = lineItem.TryGetPropertySafe("tax").AsJsString(),
                                    Amount = lineItem.TryGetPropertySafe("totalPrice").AsJsString()
                                };

                                _logger1.LogInformation("Credit Note Line Item: {LineItem}", JsonSerializer.Serialize(creditNoteLineItem));

                                // =========================
                                // 🔹 DUPLICATE CHECK —
                                // CREDIT NOTE LINE IN DATASTORE
                                //
                                // Uses business key:
                                //   InvoiceID + Item_Description
                                //   + Amount + Rate
                                // so that a re-sent Hotelogix record with a new
                                // transId does not create a duplicate row.
                                // =========================
                                _logger1.LogInformation("DUPLICATE CHECK => {Check}", JsonSerializer.Serialize(new
                                {
                                    invoiceID,
                                    description = creditNoteLineItem.Item_Description,
                                    amount = creditNoteLineItem.Amount,
                                    rate = creditNoteLineItem.Rate
                                }));

                                string? existingLineRowID = null;

                                try
                                {
                                    const string cnliCheckSql = """
                                    SELECT TOP (1) ROWID
                                    FROM CreditNote_LineItem
                                    WHERE InvoiceID = @InvoiceID
                                    AND Item_Description = @Item_Description
                                    AND Amount = @Amount
                                    AND Rate = @Rate
                                    """;

                                    var cnliCheckResult = await connection.QueryFirstOrDefaultAsync<string>(
                                        new CommandDefinition(
                                            cnliCheckSql,
                                            new
                                            {
                                                InvoiceID = invoiceID,
                                                creditNoteLineItem.Item_Description,
                                                creditNoteLineItem.Amount,
                                                creditNoteLineItem.Rate
                                            },
                                            cancellationToken: cancellationToken));

                                    if (!string.IsNullOrEmpty(cnliCheckResult))
                                    {
                                        existingLineRowID = cnliCheckResult;

                                        _logger1.LogInformation("DUPLICATE CREDIT NOTE LINE FOUND");
                                    }
                                }
                                catch (Exception cnliCheckErr)
                                {
                                    _logger1.LogInformation("Credit Note Line Check Error: {Error}", cnliCheckErr.ToJsString());
                                }

                                // =========================
                                // 🔹 INSERT OR UPDATE —
                                // CREDIT NOTE LINE ITEM
                                // =========================
                                if (!string.IsNullOrEmpty(existingLineRowID))
                                {
                                    // ── UPDATE (duplicate business key found) ──
                                    duplicateCreditNoteLinesSkipped++;

                                    _logger1.LogInformation("Credit Note Line Exists - Updating (duplicate suppressed)");

                                    try
                                    {
                                        creditNoteLineItem.ROWID = existingLineRowID;

                                        const string cnliUpdateSql = """
                                        UPDATE CreditNote_LineItem
                                        SET InvoiceID = @InvoiceID,
                                            Item_Description = @Item_Description,
                                            SAC_HSN_Code = @SAC_HSN_Code,
                                            Quantity = @Quantity,
                                            Rate = @Rate,
                                            Intra_Inter_TAX_Rates = @Intra_Inter_TAX_Rates,
                                            Amount = @Amount
                                        WHERE ROWID = @ROWID
                                        """;

                                        await connection.ExecuteAsync(new CommandDefinition(
                                            cnliUpdateSql, creditNoteLineItem, cancellationToken: cancellationToken));

                                        updatedCreditNoteLineItems.Add(creditNoteLineItem);
                                        totalUpdatedCreditNoteLineItems++;
                                    }
                                    catch (Exception cnliUpdateErr)
                                    {
                                        _logger1.LogInformation("Credit Note Line Update Error: {Error}", cnliUpdateErr.ToJsString());
                                        logs.Add(new
                                        {
                                            lineUniqueKey,
                                            error = "Credit Note Line update failed: " + cnliUpdateErr.ToJsString()
                                        });
                                    }
                                }
                                else
                                {
                                    // ── INSERT ──
                                    _logger1.LogInformation("Credit Note Line Not Found - Inserting");

                                    try
                                    {
                                        const string cnliInsertSql = """
                                        INSERT INTO CreditNote_LineItem
                                            (InvoiceID, Item_Description, SAC_HSN_Code, Quantity, Rate, Intra_Inter_TAX_Rates, Amount)
                                        OUTPUT INSERTED.ROWID
                                        VALUES
                                            (@InvoiceID, @Item_Description, @SAC_HSN_Code, @Quantity, @Rate, @Intra_Inter_TAX_Rates, @Amount)
                                        """;

                                        var newLineRowId = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                                            cnliInsertSql, creditNoteLineItem, cancellationToken: cancellationToken));

                                        creditNoteLineItem.ROWID = newLineRowId ?? string.Empty;

                                        insertedCreditNoteLineItems.Add(creditNoteLineItem);
                                        totalInsertedCreditNoteLineItems++;
                                    }
                                    catch (Exception cnliInsertErr)
                                    {
                                        _logger1.LogInformation("Credit Note Line Insert Error: {Error}", cnliInsertErr.ToJsString());
                                        logs.Add(new
                                        {
                                            lineUniqueKey,
                                            error = "Credit Note Line insert failed: " + cnliInsertErr.ToJsString()
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger1.LogInformation("ROW ERROR: {Error}", e.ToJsString());
                        logs.Add(new { error = e.ToJsString() });
                    }
                }

                // =========================
                // 🔹 RESPONSE
                // =========================
                var summary = new CreditNoteSyncResult
                {
                    Status = "success",
                    TotalThirdPartyRows = totalThirdPartyRows,
                    TotalValidJSONRows = totalValidJSONRows,
                    TotalInvalidJSONRows = totalInvalidJSONRows,
                    TotalInvoicesFound = totalInvoicesFound,
                    TotalCreditNotesFound = totalCreditNotesFound,
                    FolioTypeSummary = folioTypeSummary,
                    UnknownFolioInvoices = unknownFolioInvoices,
                    TotalInsertedCreditNotes = totalInsertedCreditNotes,
                    TotalUpdatedCreditNotes = totalUpdatedCreditNotes,
                    TotalInsertedCreditNoteLineItems = totalInsertedCreditNoteLineItems,
                    TotalUpdatedCreditNoteLineItems = totalUpdatedCreditNoteLineItems,
                    DuplicateCreditNoteLinesSkipped = duplicateCreditNoteLinesSkipped,
                    InsertedCreditNotes = insertedCreditNotes,
                    UpdatedCreditNotes = updatedCreditNotes,
                    InsertedCreditNoteLineItems = insertedCreditNoteLineItems,
                    UpdatedCreditNoteLineItems = updatedCreditNoteLineItems,
                    Logs = logs
                };

                Console.WriteLine(JsonSerializer.Serialize(summary));

                return summary;
            }
            catch (Exception err)
            {
                _logger1.LogError("MAIN ERROR: {Error}", err.ToJsString());

                var errorResult = new CreditNoteSyncResult
                {
                    Status = "error",
                    Message = err.ToJsString()
                };

                Console.WriteLine(JsonSerializer.Serialize(errorResult));

                return errorResult;
            }
        }

        // =========================================================
        // 🔹 HELPER: ExtractInvoices — supports both known payload shapes:
        // response.data.invoices and hotelogix.response.data.invoices.
        // =========================================================
        private static List<JsonElement> ExtractInvoices(JsonElement parsed)
        {
            var respInvoices = GetNested(parsed, "response", "data", "invoices");

            if (respInvoices.IsArray())
            {
                return respInvoices!.Value.EnumerateArray().ToList();
            }

            var hxInvoices = GetNested(parsed, "hotelogix", "response", "data", "invoices");

            if (hxInvoices.IsArray())
            {
                return hxInvoices!.Value.EnumerateArray().ToList();
            }

            return new List<JsonElement>();
        }

        // =========================================================
        // 🔹 HELPER: GetNested — mirrors JS optional chaining (a?.b?.c?.d)
        // by walking JsonElementExtensions.TryGetPropertySafe one segment
        // at a time, short-circuiting to null the moment any segment is
        // missing or not an object.
        // =========================================================
        private static JsonElement? GetNested(JsonElement root, params string[] path)
        {
            JsonElement? current = root;

            foreach (var segment in path)
            {
                if (current is null)
                {
                    return null;
                }

                current = current.Value.TryGetPropertySafe(segment);
            }

            return current;
        }

        // =========================================================
        // 🔹 DYNAMIC folioType FINDER
        // Recursively searches an object/array for any key matching
        // "folioType" (case-insensitive) at any nesting depth and returns
        // the first value found. Does not hardcode a single path —
        // supports data.folioType, data.invoice.folioType, or any other
        // nesting present in the payload. Mirrors the original's
        // per-level search order: all direct keys of the current level
        // are checked for a match before descending into any nested
        // object/array.
        // =========================================================
        private static JsonElement? FindFolioType(JsonElement obj, int depth = 0)
        {
            if (depth > 8)
            {
                return null;
            }

            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "foliotype", StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value;
                    }
                }

                foreach (var prop in obj.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var found = FindFolioType(prop.Value, depth + 1);

                        if (found is not null)
                        {
                            return found;
                        }
                    }
                }
            }
            else if (obj.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in obj.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        var found = FindFolioType(item, depth + 1);

                        if (found is not null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Replaces the exported Catalyst function contract for this module.
    /// </summary>
    public interface ICreditNoteSyncService
    {
        Task<CreditNoteSyncResult> RunAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Maps to the SQL Server "Credit_Note" table (was Catalyst datastore
    /// table 'Credit_Note'). Columns match exactly what the original
    /// function reads/writes in creditNoteHeader.
    /// </summary>
    public sealed class CreditNoteRow
    {
        public string ROWID { get; set; } = string.Empty;
        public string Customer_Name { get; set; } = string.Empty;
        public string Location_Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Credit_Note_No { get; set; } = string.Empty;
        public string Credit_Note_Date { get; set; } = string.Empty;
        public string InvoiceID { get; set; } = string.Empty;
        public string Hotel_ID { get; set; } = string.Empty;
        public string BooksID { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string ThirdpartyStatus { get; set; } = string.Empty;
        public string BooksStatus { get; set; } = string.Empty;
    }

    /// <summary>
    /// Maps to the SQL Server "Credit_Note_LineItem" table (was Catalyst
    /// datastore table 'Credit_Note_LineItem'). Columns match exactly
    /// what the original function reads/writes in creditNoteLineItem.
    /// </summary>
    public sealed class CreditNoteLineItemRow
    {
        public string ROWID { get; set; } = string.Empty;
        public string InvoiceID { get; set; } = string.Empty;
        public string Item_Description { get; set; } = string.Empty;
        public string SAC_HSN_Code { get; set; } = string.Empty;
        public string Quantity { get; set; } = "1";
        public string Rate { get; set; } = string.Empty;
        public string Intra_Inter_TAX_Rates { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mirrors one entry pushed into unknownFolioInvoices:
    ///   { rowID, invoiceID, invoiceIndex, rawFolioType, discoveredFolioType, invoice, parsedJSON }
    /// </summary>
    public sealed class UnknownFolioInvoiceEntry
    {
        public string RowID { get; set; } = string.Empty;
        public string InvoiceID { get; set; } = string.Empty;
        public int InvoiceIndex { get; set; }
        public JsonElement? RawFolioType { get; set; }
        public JsonElement? DiscoveredFolioType { get; set; }
        public JsonElement Invoice { get; set; }
        public JsonElement ParsedJSON { get; set; }
    }

    /// <summary>
    /// Mirrors the `summary` object built at the end of the original
    /// function and passed to basicIO.write(JSON.stringify(summary)),
    /// plus the equivalent shape used in the catch-all error response.
    /// </summary>
    public sealed class CreditNoteSyncResult
    {
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }

        public int TotalThirdPartyRows { get; set; }
        public int TotalValidJSONRows { get; set; }
        public int TotalInvalidJSONRows { get; set; }

        public int TotalInvoicesFound { get; set; }
        public int TotalCreditNotesFound { get; set; }
        public Dictionary<string, int> FolioTypeSummary { get; set; } = new();
        public List<UnknownFolioInvoiceEntry> UnknownFolioInvoices { get; set; } = new();

        public int TotalInsertedCreditNotes { get; set; }
        public int TotalUpdatedCreditNotes { get; set; }

        public int TotalInsertedCreditNoteLineItems { get; set; }
        public int TotalUpdatedCreditNoteLineItems { get; set; }
        public int DuplicateCreditNoteLinesSkipped { get; set; }

        public List<CreditNoteRow> InsertedCreditNotes { get; set; } = new();
        public List<CreditNoteRow> UpdatedCreditNotes { get; set; } = new();

        public List<CreditNoteLineItemRow> InsertedCreditNoteLineItems { get; set; } = new();
        public List<CreditNoteLineItemRow> UpdatedCreditNoteLineItems { get; set; } = new();

        public List<object> Logs { get; set; } = new();
    }
}

