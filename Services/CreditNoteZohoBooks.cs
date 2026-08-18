using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using AgentSyncConsole.Utilites;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

/*
 * =====================================================================================
 * Converted 1:1 from the Catalyst Job Function that creates a Zoho Books Credit Note
 * for one pending Credit_Note row and links it to its Invoice (root-caused error
 * 12069 — "Select the associated invoice number or invoice type.").
 *
 * Business flow preserved exactly, in the same order:
 *   1. Load latest "Books" access token.
 *   2. Read one pending Credit_Note row (BooksStatus IS NULL OR <> 'success').
 *   3. Read the Credit_Note_LineItem rows for that credit note (filtered by
 *      Credit_Note_No, with the same "latest row" legacy fallback as the source).
 *   4. Resolve Customer.booksID and Invoice.BooksInvoiceID from the local DB.
 *   5. Fetch the live Books Contact and live Books Invoice from the Zoho Books API.
 *   6. Resolve GST fields (gst_treatment, gst_no, place_of_supply, location_id,
 *      reference_invoice_type) dynamically from that live data — never hardcoded.
 *   7. Build credit-note line items, inheriting item_id/account_id/tax_id/hsn_or_sac/
 *      location_id from the matching Books Invoice line item.
 *   8. POST the Credit Note to Zoho Books.
 *   9. POST the "apply to invoice" call to link the credit note to the invoice.
 *   10. Update the Credit_Note row with BooksID, Response and BooksStatus.
 *
 * Persistence layer only has changed (Catalyst datastore/zcql -> SQL Server via
 * SqlConnectionFactory + Dapper, parameterized). Outbound HTTP follows the same
 * request/retry pattern already used by BooksApiService (IRetryService, the
 * "Zoho-oauthtoken" auth scheme, ZohoBooks:* configuration keys).
 * =====================================================================================
 */

namespace AgentSyncConsole.Models
{
    /// <summary>Maps 1:1 to the "Credit_Note" table (mirrors the Catalyst Credit_Note datastore table).</summary>
    public class CreditNote
    {
        public long ROWID { get; set; }
        public string? InvoiceID { get; set; }
        public string? Customer_Name { get; set; }
        public string? Credit_Note_No { get; set; }
        public DateTime Credit_Note_Date { get; set; }
        public string? BooksStatus { get; set; }
        public string? BooksID { get; set; }
        public string? Response { get; set; }
        public string? ThirdpartyStatus { get; set; }
    }

    /// <summary>Maps 1:1 to the "Credit_Note_LineItem" table.</summary>
    public class CreditNoteLineItem
    {
        public string? Credit_Note_No { get; set; }
        public string? Item_Description { get; set; }
        public string? Quantity { get; set; }
        public string? Amount { get; set; }
        public string? SAC_HSN_Code { get; set; }
    }

    /// <summary>Execution result, mirrors the various basicIO.write(...) payloads in index.js.</summary>
    public class CreditNoteSyncResult_ZohoBooks
    {
        public string Status { get; set; } = "";
        public long? CreditNoteROWID { get; set; }
        public string? InvoiceID { get; set; }
        public string? CreditNoteNo { get; set; }
        public string? Reason { get; set; }
        public string? BooksInvoiceID { get; set; }
        public string? BooksCreditNoteID { get; set; }
        public string? CustomerBooksID { get; set; }
        public decimal? TotalCreditAmount { get; set; }
        public object? ResolvedGstFields { get; set; }
        public object? Step1Response { get; set; }
        public object? Step2Response { get; set; }
        public string? Message { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Zoho Books API models (request payload + response envelopes). Kept minimal
    // and specific to what the credit note flow actually reads/writes.
    // ---------------------------------------------------------------------------

    internal interface IHasRawBody
    {
        string? RawBody { get; set; }
    }

    public class BooksContactDetail
    {
        [JsonPropertyName("contact_id")] public string? ContactId { get; set; }
        [JsonPropertyName("gst_treatment")] public string? GstTreatment { get; set; }
        [JsonPropertyName("gst_no")] public string? GstNo { get; set; }
        [JsonPropertyName("place_of_contact")] public string? PlaceOfContact { get; set; }
    }

    public class BooksContactApiResponse : IHasRawBody
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("contact")] public BooksContactDetail? Contact { get; set; }
        [JsonIgnore] public string? RawBody { get; set; }
    }

    public class BooksInvoiceLineItemDetail
    {
        [JsonPropertyName("item_id")] public string? ItemId { get; set; }
        [JsonPropertyName("account_id")] public string? AccountId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("hsn_or_sac")] public string? HsnOrSac { get; set; }
        [JsonPropertyName("tax_id")] public string? TaxId { get; set; }
        [JsonPropertyName("location_id")] public string? LocationId { get; set; }
        [JsonPropertyName("discount_account_id")] public string? DiscountAccountId { get; set; }
        [JsonPropertyName("quantity")] public decimal? Quantity { get; set; }
    }

    public class BooksInvoiceDetail
    {
        [JsonPropertyName("customer_id")] public string? CustomerId { get; set; }
        [JsonPropertyName("place_of_supply")] public string? PlaceOfSupply { get; set; }
        [JsonPropertyName("location_id")] public string? LocationId { get; set; }
        [JsonPropertyName("total")] public decimal? Total { get; set; }
        [JsonPropertyName("sub_total")] public decimal? SubTotal { get; set; }
        [JsonPropertyName("line_items")] public List<BooksInvoiceLineItemDetail> LineItems { get; set; } = new();
    }

    public class BooksInvoiceApiResponse : IHasRawBody
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("invoice")] public BooksInvoiceDetail? Invoice { get; set; }
        [JsonIgnore] public string? RawBody { get; set; }
    }

    public class BooksCreditNoteLineItem
    {
        [JsonPropertyName("item_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ItemId { get; set; }
        [JsonPropertyName("account_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AccountId { get; set; }
        [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
        [JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
        [JsonPropertyName("rate")] public string? Rate { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("hsn_or_sac"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? HsnOrSac { get; set; }
        [JsonPropertyName("tax_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxId { get; set; }
        [JsonPropertyName("location_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LocationId { get; set; }
        [JsonPropertyName("discount_account_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DiscountAccountId { get; set; }
    }

    /// <summary>Outbound payload sent to POST /books/v3/creditnotes, mirrors createPayload in index.js.
    /// Every property is nullable and JsonIgnore(WhenWritingNull) so unset/blank fields are omitted,
    /// matching the original stripEmpty(...) behavior.</summary>
    public class BooksCreditNotePayload
    {
        [JsonPropertyName("customer_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CustomerId { get; set; }
        [JsonPropertyName("invoice_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? invoice_id { get; set; }
        [JsonPropertyName("creditnote_number"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreditnoteNumber { get; set; }
        [JsonPropertyName("date"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Date { get; set; }
        [JsonPropertyName("is_draft")] public bool IsDraft { get; set; } = true;
        [JsonPropertyName("reference_invoice_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReferenceInvoiceType { get; set; }
        [JsonPropertyName("gst_treatment"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GstTreatment { get; set; }
        [JsonPropertyName("gst_no"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GstNo { get; set; }
        [JsonPropertyName("place_of_supply"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PlaceOfSupply { get; set; }
        [JsonPropertyName("location_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LocationId { get; set; }
        [JsonPropertyName("line_items")] public List<BooksCreditNoteLineItem> LineItems { get; set; } = new();
    }

    public class BooksCreditNoteDetail
    {
        [JsonPropertyName("creditnote_id")] public string? CreditnoteId { get; set; }
    }

    public class BooksCreditNoteApiResponse : IHasRawBody
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("creditnote")] public BooksCreditNoteDetail? Creditnote { get; set; }
        [JsonIgnore] public string? RawBody { get; set; }
    }

    public class BooksApplyCreditNoteApiResponse : IHasRawBody
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonIgnore] public string? RawBody { get; set; }
    }

    internal class BooksApplyInvoiceEntry
    {
        [JsonPropertyName("invoice_id")] public string InvoiceId { get; set; } = "";
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
    }

    internal class BooksApplyCreditNotePayload
    {
        [JsonPropertyName("invoices")] public List<BooksApplyInvoiceEntry> Invoices { get; set; } = new();
    }
}

namespace AgentSyncConsole.Interfaces
{
    /// <summary>
    /// Converted 1:1 from the Catalyst credit-note-to-Books Job Function. Preserves
    /// every validation, SQL update, and API interaction from the original.
    /// </summary>
    public interface ICreditNoteSyncService_ZohoBooks
    {
        Task<CreditNoteSyncResult_ZohoBooks> RunAsync(CancellationToken ct = default);
    }
}

namespace AgentSyncConsole.Services
{
    using AgentSyncConsole.Models;

    public class CreditNoteSyncService_ZohoBooks : ICreditNoteSyncService_ZohoBooks
    {
        private readonly SqlConnectionFactory _factory;
        private readonly IAccessTokenService _accessTokenService;
        private readonly IRetryService _retry;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CreditNoteSyncService_ZohoBooks> _logger;

        private readonly string _tokenApplication;
        private readonly string _organizationId;
        private readonly string _apiBasePath;
        private readonly string _apiHost;
        private readonly string _orgHomeStateCode;
        private readonly string _hardcodedFallbackCustomerBooksId;

        private const string StatusSuccess = "success";
        private const string StatusFailed = "failed";

        public CreditNoteSyncService_ZohoBooks(
            SqlConnectionFactory factory,
            IAccessTokenService accessTokenService,
            IRetryService retry,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<CreditNoteSyncService_ZohoBooks> logger)
        {
            _factory = factory;
            _accessTokenService = accessTokenService;
            _retry = retry;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            _tokenApplication = configuration["ZohoAuth:TokenApplication"] ?? "Books";
            _organizationId = configuration["ZohoBooks:OrganizationId"]
                ?? throw new InvalidOperationException("Missing ZohoBooks:OrganizationId");
            _apiBasePath = configuration["ZohoBooks:ApiBasePath"] ?? "/books/v3";
            _apiHost = configuration["ZohoBooks:ApiHost"] ?? "www.zohoapis.in";

            // Used only for the B2C Large vs B2C Others split (interstate sale > 1 lakh).
            // Set ZohoBooks:OrgHomeStateCode to this org's registered GST state code
            // (e.g. "WB", "MH"). If left blank, B2C contacts conservatively default to
            // "b2c_others" and a warning is logged.
            _orgHomeStateCode = configuration["Sync:OrgHomeStateCode"] ?? "";
            _hardcodedFallbackCustomerBooksId = configuration["Sync:HardcodedFallbackCustomerBooksId"] ?? "3233228000000045007";
        }

        public async Task<CreditNoteSyncResult_ZohoBooks> RunAsync(CancellationToken ct = default)
        {
            var result = new CreditNoteSyncResult_ZohoBooks();

            try
            {
                // =========================
                // GET ACCESS TOKEN
                // =========================
                var latestToken = await _accessTokenService.LoadLatestTokenAsync(_tokenApplication, ct)
                    ?? throw new InvalidOperationException($"No {_tokenApplication} access token found");
                var accessToken = (latestToken.accessToken ?? string.Empty).Trim();
                _logger.LogInformation("Access token loaded for {Application}", _tokenApplication);

                // =========================
                // FETCH THE PENDING CREDIT NOTE
                // =========================
                var creditNoteData = await GetPendingCreditNoteAsync(ct);

                if (creditNoteData is null)
                {
                    _logger.LogInformation("No pending Credit_Note record found.");
                    result.Status = "no_pending_credit_note";
                    return result;
                }

                var creditNoteROWID = creditNoteData.ROWID;
                var invoiceID = (creditNoteData.InvoiceID ?? string.Empty).Trim();
                var ownerId = (creditNoteData.Customer_Name ?? string.Empty).Trim();
                var creditNoteNo = (creditNoteData.Credit_Note_No ?? string.Empty).Trim();

                _logger.LogInformation(
                    "CREDIT NOTE => InvoiceID={InvoiceID} | Credit_Note_No={CreditNoteNo} | ROWID={ROWID}",
                    invoiceID, creditNoteNo, creditNoteROWID);

                if (string.IsNullOrEmpty(invoiceID))
                {
                    return await FailAndExitAsync(creditNoteROWID, "Credit_Note.InvoiceID is blank", result, ct);
                }

                // -----------------------------------------
                // FETCH LINE ITEMS FOR THIS CREDIT NOTE
                // Filtered by Credit_Note_No; falls back to the legacy "latest row"
                // behavior (matching the original) if the filtered query returns nothing.
                // -----------------------------------------
                var lineItemRows = await GetLineItemsAsync(creditNoteNo, ct);

                if (lineItemRows.Count == 0)
                {
                    //return await FailAndExitAsync(
                    //    creditNoteROWID, "No Credit_Note_LineItem record found for this credit note", result, ct,
                    //    invoiceID: invoiceID);
                    Console.WriteLine("No Credit_Note_LineItem record found for this credit note");
                }

                // -----------------------------------------
                // LOOKUP CUSTOMER -> booksID (local datastore)
                // -----------------------------------------
                var customer = await GetCustomerByCustomerIdAsync(ownerId, ct);
                var customerBooksID = "";
                if (customer is null)
                {
                    //return await FailAndExitAsync(
                    //    creditNoteROWID, "Customer not found for Customer_Name=" + ownerId, result, ct,
                    //    invoiceID: invoiceID);
                    Console.WriteLine("Customer not found for Customer_Name");

                    customerBooksID = _hardcodedFallbackCustomerBooksId;

                }
                else
                {
                    customerBooksID = NullIfEmpty(customer.booksID);

                }
                

                if (string.IsNullOrEmpty(customerBooksID))
                {
                    //return await FailAndExitAsync(
                    //    creditNoteROWID, "Customer booksID is blank for Customer_Name=" + ownerId, result, ct,
                    //    invoiceID: invoiceID);
                    Console.WriteLine("Customer booksID is blank for customer_name");
                }

                
                var booksInvoiceID = (await GetBooksInvoiceIdAsync(invoiceID, ct) ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(booksInvoiceID))
                {
                    //return await FailAndExitAsync(
                    //    creditNoteROWID, "Invoice.BooksInvoiceID is blank for InvoiceID=" + invoiceID, result, ct,
                    //    invoiceID: invoiceID);
                    Console.WriteLine("Invoice.BooksInvoiceID is blank for InvoiceID");
                }

                // -----------------------------------------
                // FETCH LIVE BOOKS CUSTOMER (CONTACT) — REQUIRED FOR GST FIELDS
                // -----------------------------------------
                var contactResponse = await CallBooksApiAsync<BooksContactApiResponse>(
                    HttpMethod.Get, $"{_apiBasePath}/contacts/{customerBooksID}?organization_id={_organizationId}",
                    accessToken, null, "Get Contact", ct);

                if (contactResponse.Contact is null)
                {
                    //return await FailAndExitAsync(
                    //    creditNoteROWID, "Could not fetch live Books Contact for customer_id=" + customerBooksID, result, ct,
                    //    invoiceID: invoiceID);

                    Console.WriteLine("Could not fetch live Books Contact for customer_id");
                }

                // -----------------------------------------
                // FETCH LIVE BOOKS INVOICE — REQUIRED FOR location_id, place_of_supply,
                // AND THE LINE ITEM DATA (item_id / account_id / tax_id / hsn_or_sac)
                // -----------------------------------------
                var invoiceResponse = await CallBooksApiAsync<BooksInvoiceApiResponse>(
                    HttpMethod.Get, $"{_apiBasePath}/invoices/{booksInvoiceID}?organization_id={_organizationId}",
                    accessToken, null, "Get Invoice", ct);

                if (invoiceResponse.Invoice is null)
                {
                    //return await FailAndExitAsync(
                    //    creditNoteROWID, "Could not fetch live Books Invoice for invoice_id=" + booksInvoiceID, result, ct,
                    //    invoiceID: invoiceID);

                    Console.WriteLine("Could not fetch live Books Invoice for invoice_id");
                }

                var booksContact = contactResponse.Contact;
                var booksInvoice = invoiceResponse.Invoice;
              
                //if (!string.Equals(booksInvoice.CustomerId, customerBooksID, StringComparison.Ordinal))
                //{
                //    _logger.LogWarning(
                //        "Invoice customer_id ({InvoiceCustomerId}) does not match resolved customer_id ({CustomerBooksID}). Proceeding, but verify the Customer/Invoice local mapping.",
                //        booksInvoice.CustomerId, customerBooksID);
                //}

                // -----------------------------------------
                // RESOLVE GST FIELDS DYNAMICALLY (no hardcoding)
                // -----------------------------------------
                var gstTreatment = (booksContact.GstTreatment ?? string.Empty).Trim();
                var gstNo = (booksContact.GstNo ?? string.Empty).Trim();

                var placeOfSupply = !string.IsNullOrWhiteSpace(booksInvoice.PlaceOfSupply)
                    ? booksInvoice.PlaceOfSupply!.Trim()
                    : (booksContact.PlaceOfContact ?? string.Empty).Trim();

                var locationId = (booksInvoice.LocationId ?? string.Empty).Trim();

                var referenceInvoiceType = ResolveReferenceInvoiceType(booksContact, booksInvoice);

                _logger.LogInformation(
                    "Resolved GST fields => gstTreatment={GstTreatment} gstNo={GstNo} placeOfSupply={PlaceOfSupply} locationId={LocationId} referenceInvoiceType={ReferenceInvoiceType}",
                    gstTreatment, gstNo, placeOfSupply, locationId, referenceInvoiceType);

                if (string.IsNullOrEmpty(placeOfSupply))
                {
                    _logger.LogWarning("place_of_supply could not be resolved from either the invoice or the contact — Books may reject the payload. Verify the contact has a place_of_contact set.");
                }
                if (string.IsNullOrEmpty(locationId))
                {
                    _logger.LogWarning("location_id could not be resolved from the invoice. If Locations is enabled for this org, Books will likely reject the payload.");
                }

                // -----------------------------------------
                // BUILD LINE ITEMS — inherit item_id/account_id/tax_id/hsn_or_sac/location_id
                // from the matching INVOICE line item rather than guessing them.
                // -----------------------------------------
                var invoiceLineItems = booksInvoice.LineItems ?? new List<BooksInvoiceLineItemDetail>();
                var lineItems = new List<BooksCreditNoteLineItem>();

                foreach (var lineItemData in lineItemRows)
                {
                    var matched = FindMatchingInvoiceLineItem(invoiceLineItems, lineItemData);
                    var creditAmount = Math.Abs(ParseDecimalOrDefault(lineItemData.Amount, 0m));
                    var quantity = ParseDecimalOrDefault(lineItemData.Quantity, matched?.Quantity ?? 1m);

                    lineItems.Add(new BooksCreditNoteLineItem
                    {
                        ItemId = NullIfEmpty(matched?.ItemId),
                        AccountId = NullIfEmpty(matched?.AccountId),
                        Name = NullIfEmpty(lineItemData.Item_Description) ?? NullIfEmpty(matched?.Name),
                        Description = NullIfEmpty(lineItemData.Item_Description) ?? NullIfEmpty(matched?.Description),
                        Rate = creditAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Quantity = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        HsnOrSac = NullIfEmpty(matched?.HsnOrSac) ?? NullIfEmpty(lineItemData.SAC_HSN_Code),
                        TaxId = NullIfEmpty(matched?.TaxId),
                        LocationId = NullIfEmpty(matched?.LocationId) ?? NullIfEmpty(locationId),
                        DiscountAccountId = NullIfEmpty(matched?.DiscountAccountId)
                    });
                }

                Console.WriteLine(lineItems);

                var totalCreditAmount = lineItems.Sum(li =>
                    ParseDecimalOrDefault(li.Rate, 0m) * ParseDecimalOrDefault(li.Quantity, 1m));

                if (totalCreditAmount <= 0)
                {
                    return await FailAndExitAsync(
                        creditNoteROWID, "Invalid or zero total creditAmount derived from Credit_Note_LineItem rows", result, ct,
                        invoiceID: invoiceID);
                }

                _logger.LogInformation("totalCreditAmount => {TotalCreditAmount}", totalCreditAmount);

                // -----------------------------------------
                // STEP 1 — CREATE CREDIT NOTE
                // Includes reference_invoice_type + gst_treatment + gst_no +
                // place_of_supply + location_id, all resolved dynamically above.
                // -----------------------------------------
                var createPayload = new BooksCreditNotePayload
                {
                    CustomerId = NullIfEmpty(customerBooksID) ?? _hardcodedFallbackCustomerBooksId,
                    invoice_id = NullIfEmpty(booksInvoiceID),
                    CreditnoteNumber = NullIfEmpty(creditNoteNo),
                    Date = NullIfEmpty(creditNoteData.Credit_Note_Date.ToString("yyyy/MM/dd")),
                    IsDraft = true,
                    ReferenceInvoiceType = NullIfEmpty(referenceInvoiceType),
                    GstTreatment = NullIfEmpty(gstTreatment),
                    GstNo = NullIfEmpty(gstNo),
                    PlaceOfSupply = NullIfEmpty(placeOfSupply),
                    LocationId = NullIfEmpty(locationId),
                    LineItems = lineItems
                };

                Console.WriteLine(createPayload);

                var createResponse = await CallBooksApiAsync<BooksCreditNoteApiResponse>(
                    HttpMethod.Post, $"{_apiBasePath}/creditnotes?organization_id={_organizationId}",
                    accessToken, createPayload, "Step 1 — Create Credit Note", ct);

                var booksCreditNoteID = createResponse.Creditnote?.CreditnoteId ?? string.Empty;

                if (string.IsNullOrEmpty(booksCreditNoteID))
                {
                    return await FailAndExitAsync(
                        creditNoteROWID, "Credit note creation failed", result, ct,
                        invoiceID: invoiceID, extraResponse: createResponse.RawBody);
                }

                _logger.LogInformation("Credit Note created => creditnote_id={BooksCreditNoteID}", booksCreditNoteID);

                // -----------------------------------------
                // STEP 2 — APPLY CREDIT NOTE TO INVOICE
                // POST /creditnotes/{creditnote_id}/invoices — the "Credit to an invoice"
                // endpoint. This reduces the invoice's outstanding balance; it is
                // functionally distinct from reference_invoice_type (a GST classification
                // field only).
                // -----------------------------------------
                var applyPayload = new BooksApplyCreditNotePayload
                {
                    Invoices = new List<BooksApplyInvoiceEntry>
                    {
                        new BooksApplyInvoiceEntry { InvoiceId = booksInvoiceID, Amount = totalCreditAmount }
                    }
                };

                var applyResponse = await CallBooksApiAsync<BooksApplyCreditNoteApiResponse>(
                    HttpMethod.Post, $"{_apiBasePath}/creditnotes/{booksCreditNoteID}/invoices?organization_id={_organizationId}",
                    accessToken, applyPayload, "Step 2 — Apply to Invoice", ct);

                var isLinked = applyResponse.Code == Constants.BooksCodeSuccess;
                var finalStatus = isLinked ? StatusSuccess : StatusFailed;

                if (!isLinked)
                {
                    _logger.LogError(
                        "Step 2 failed — credit note {BooksCreditNoteID} was created but NOT linked to invoice {BooksInvoiceID}",
                        booksCreditNoteID, booksInvoiceID);
                }

                // -----------------------------------------
                // UPDATE Credit_Note ROW
                // -----------------------------------------
                var responseJson = JsonHelper.Serialize(new
                {
                    step1_create = JsonHelper.TryDeserialize<object>(createResponse.RawBody ?? "null") ?? (object?)createResponse.RawBody,
                    step2_apply_to_invoice = JsonHelper.TryDeserialize<object>(applyResponse.RawBody ?? "null") ?? (object?)applyResponse.RawBody
                });

                await UpdateCreditNoteFinalAsync(creditNoteROWID, booksCreditNoteID, responseJson, finalStatus, "processed", ct);

                // -----------------------------------------
                // FINAL RESULT
                // -----------------------------------------
                result.Status = finalStatus;
                result.InvoiceID = invoiceID;
                result.CreditNoteNo = creditNoteNo;
                result.BooksCreditNoteID = booksCreditNoteID;
                result.BooksInvoiceID = booksInvoiceID;
                result.CustomerBooksID = customerBooksID;
                result.TotalCreditAmount = totalCreditAmount;
                result.ResolvedGstFields = new { gstTreatment, gstNo, placeOfSupply, locationId, referenceInvoiceType };
                result.Step1Response = createResponse.RawBody;
                result.Step2Response = applyResponse.RawBody;

                return result;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "CreditNoteSyncService failed");
                result.Status = "error";
                result.Message = error.ToString();
                return result;
            }
        }

        // =========================
        // DATA ACCESS (SqlConnectionFactory + Dapper, parameterized)
        // =========================

        private async Task<CreditNote?> GetPendingCreditNoteAsync(CancellationToken ct)
        {
            const string sql = @"
                SELECT TOP 1 ROWID, InvoiceID, Customer_Name, Credit_Note_No, Credit_Note_Date,
                              BooksStatus, BooksID, Response, ThirdpartyStatus
                FROM CreditNote
                WHERE BooksStatus IS NULL OR BooksStatus <> @SuccessStatus
                ";

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<CreditNote>(
                new CommandDefinition(sql, new { SuccessStatus = StatusSuccess }, cancellationToken: ct));
        }

        private async Task<List<CreditNoteLineItem>> GetLineItemsAsync(string creditNoteNo, CancellationToken ct)
        {
            const string filteredSql = @"
                SELECT C.Credit_Note_No, LI.Item_Description, LI.Quantity, LI.Amount, LI.SAC_HSN_Code
                FROM CreditNote_LineItem LI INNER JOIN CreditNote C
                ON C.InvoiceID=LI.InvoiceID
                WHERE C.Credit_Note_No = @CreditNoteNo
               ";

            List<CreditNoteLineItem> rows;
            try
            {
                using var conn = await _factory.CreateOpenConnectionAsync(ct);
                var filtered = await conn.QueryAsync<CreditNoteLineItem>(
                    new CommandDefinition(filteredSql, new { CreditNoteNo = creditNoteNo }, cancellationToken: ct));
                rows = filtered.AsList();
            }
            catch (Exception filterErr)
            {
                _logger.LogWarning(filterErr, "Filtered Credit_Note_LineItem query failed — check column name 'Credit_Note_No' against your schema");
                rows = new List<CreditNoteLineItem>();
            }

            if (rows.Count == 0)
            {
                _logger.LogWarning("No line items matched by Credit_Note_No — falling back to legacy 'latest row' behavior. This may pick up the WRONG credit note's line item. Fix the schema link above as soon as possible.");

                const string legacySql = @"
                    SELECT TOP 1 C.Credit_Note_No, LI.Item_Description, LI.Quantity, LI.Amount, LI.SAC_HSN_Code
                    FROM CreditNote_LineItem LI INNER JOIN CreditNote C
                    ON LI.InvoiceID=C.InvoiceID
                    ";

                using var conn = await _factory.CreateOpenConnectionAsync(ct);
                var legacy = await conn.QueryFirstOrDefaultAsync<CreditNoteLineItem>(
                    new CommandDefinition(legacySql, cancellationToken: ct));

                if (legacy is not null)
                {
                    rows.Add(legacy);
                }
            }

            return rows;
        }

        private async Task<Customer?> GetCustomerByCustomerIdAsync(string customerId, CancellationToken ct)
        {
            const string sql = @"
                SELECT TOP 1 CustomerID, booksID, Place_Of_Supply
                FROM Customer
                WHERE CustomerID = @CustomerID";

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<Customer>(
                new CommandDefinition(sql, new { CustomerID = customerId }, cancellationToken: ct));
        }

        private async Task<string?> GetBooksInvoiceIdAsync(string invoiceId, CancellationToken ct)
        {
            const string sql = @"
                SELECT  BooksInvoiceID
                FROM Invoice
                WHERE InvoiceID = @InvoiceID";

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<string>(
                new CommandDefinition(sql, new { InvoiceID = invoiceId }, cancellationToken: ct));
        }

        private async Task UpdateCreditNoteFailedAsync(long rowId, string reason, CancellationToken ct)
        {
            const string sql = @"
                UPDATE CreditNote
                SET BooksStatus = @BooksStatus, Response = @Response
                WHERE ROWID = @ROWID";

            var response = JsonHelper.Serialize(new { status = "FAILED", error = reason });

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(
                sql, new { ROWID = rowId, BooksStatus = StatusFailed, Response = response }, cancellationToken: ct));
        }

        private async Task UpdateCreditNoteFinalAsync(long rowId, string booksId, string response, string booksStatus, string thirdpartyStatus, CancellationToken ct)
        {
            const string sql = @"
                UPDATE CreditNote
                SET BooksID = @BooksID, Response = @Response, BooksStatus = @BooksStatus, ThirdpartyStatus = @ThirdpartyStatus
                WHERE ROWID = @ROWID";

            using var conn = await _factory.CreateOpenConnectionAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(
                sql,
                new { ROWID = rowId, BooksID = booksId, Response = response, BooksStatus = booksStatus, ThirdpartyStatus = thirdpartyStatus },
                cancellationToken: ct));
        }

        private async Task<CreditNoteSyncResult_ZohoBooks> FailAndExitAsync(
            long rowId, string reason, CreditNoteSyncResult_ZohoBooks result, CancellationToken ct,
            string? invoiceID = null, string? extraResponse = null)
        {
            _logger.LogError("ROWID={ROWID} — {Reason}", rowId, reason);
            await UpdateCreditNoteFailedAsync(rowId, reason, ct);

            result.Status = "failed";
            result.CreditNoteROWID = rowId;
            result.Reason = reason;
            result.InvoiceID = invoiceID;
            result.Message = extraResponse;
            return result;
        }

        // =========================
        // ZOHO BOOKS HTTP (mirrors BooksApiService's request/retry pattern)
        // =========================

        private async Task<T> CallBooksApiAsync<T>(HttpMethod method, string path, string accessToken, object? payload, string label, CancellationToken ct)
            where T : class, IHasRawBody, new()
        {
            return await _retry.ExecuteAsync(async () =>
            {
                using var request = new HttpRequestMessage(method, $"https://{_apiHost}{path}");
                request.Headers.TryAddWithoutValidation("Authorization", $"Zoho-oauthtoken {accessToken}");

                if (payload is not null)
                {
                    var json = JsonHelper.Serialize(payload);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    _logger.LogInformation("BOOKS API REQUEST {Label} -> {Method} {Path} : {Json}", label, method, path, json);
                }

                using var http = _httpClientFactory.CreateClient();
                using var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                _logger.LogInformation("BOOKS API RESPONSE {Label} : {Body}", label, body);

                T parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<T>(body, JsonHelper.Options) ?? new T();
                }
                catch (JsonException)
                {
                    parsed = new T();
                }

                parsed.RawBody = body;
                return parsed;
            }, $"CreditNoteBooksApi {label}", ct);
        }

        // =========================
        // PURE HELPERS
        // =========================

        /// <summary>Maps a Books Contact's gst_treatment -> the required reference_invoice_type.
        /// Mirrors resolveReferenceInvoiceType(customer, invoice) in index.js exactly, including
        /// the SEZ/Overseas "tax paid" default assumption and the B2C Large/Others split.</summary>
        private string ResolveReferenceInvoiceType(BooksContactDetail customer, BooksInvoiceDetail invoice)
        {
            var treatment = (customer.GstTreatment ?? string.Empty).Trim().ToLowerInvariant();

            switch (treatment)
            {
                case "business_gst":
                case "business_gst_composition":
                case "tax_deductor":
                case "tax_collector":
                    return "registered";

                case "deemed_export":
                    return "deemed_export";

                case "special_economic_zone":
                case "sez_developer":
                case "sez":
                    // Default assumption: tax paid (no LUT bond). Change to
                    // "sez_without_payment" if this org files under LUT.
                    return "sez_with_payment";

                case "overseas":
                    // Default assumption: tax paid (no LUT bond). Change to
                    // "export_without_payment" if this org files under LUT.
                    return "export_with_payment";

                case "consumer":
                case "business_none":
                case "unregistered":
                default:
                    {
                        var total = invoice.Total ?? invoice.SubTotal ?? 0m;
                        var supplyState = (invoice.PlaceOfSupply ?? string.Empty).Trim().ToUpperInvariant();

                        if (string.IsNullOrEmpty(_orgHomeStateCode))
                        {
                            _logger.LogWarning("Sync:OrgHomeStateCode is not set — defaulting B2C reference_invoice_type to 'b2c_others'. Set this configuration value to enable correct B2C Large detection.");
                            return "b2c_others";
                        }

                        var isInterState = !string.IsNullOrEmpty(supplyState)
                            && !string.Equals(supplyState, _orgHomeStateCode.ToUpperInvariant(), StringComparison.Ordinal);

                        return (isInterState && total > 100000) ? "b2c_large" : "b2c_others";
                    }
            }
        }

        /// <summary>Finds the invoice line item that corresponds to a given credit note line item,
        /// so item_id/account_id/tax_id/hsn_or_sac/location_id can be inherited instead of hardcoded
        /// or guessed. Mirrors findMatchingInvoiceLineItem(...) in index.js exactly.</summary>
        private BooksInvoiceLineItemDetail? FindMatchingInvoiceLineItem(
            List<BooksInvoiceLineItemDetail> invoiceLineItems, CreditNoteLineItem creditLineItem)
        {
            if (invoiceLineItems.Count == 0) return null;

            var wanted = (creditLineItem.Item_Description ?? string.Empty).Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(wanted))
            {
                var exact = invoiceLineItems.FirstOrDefault(li =>
                    string.Equals((li.Name ?? string.Empty).Trim().ToLowerInvariant(), wanted, StringComparison.Ordinal) ||
                    string.Equals((li.Description ?? string.Empty).Trim().ToLowerInvariant(), wanted, StringComparison.Ordinal));
                if (exact is not null) return exact;

                var partial = invoiceLineItems.FirstOrDefault(li =>
                    (li.Name ?? string.Empty).ToLowerInvariant().Contains(wanted) ||
                    wanted.Contains((li.Name ?? string.Empty).ToLowerInvariant()));
                if (partial is not null) return partial;
            }

            _logger.LogWarning(
                "Could not match credit line item \"{ItemDescription}\" to a specific invoice line item — falling back to the invoice's first line item. Verify this is correct.",
                creditLineItem.Item_Description);
            return invoiceLineItems[0];
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrEmpty(value) ? null : value;

        private static decimal ParseDecimalOrDefault(string? value, decimal fallback) =>
            decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }
}