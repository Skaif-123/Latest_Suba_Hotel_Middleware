using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Interfaces.PosInvoiceInterface;
using AgentSyncConsole.Models;
using AgentSyncConsole.Models.PosInoviceModel;
using AgentSyncConsole.Utilites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;


namespace AgentSyncConsole.Services.PosInvoiceServices
{


    /// <summary>
    /// Flow 2 — SQL -> Zoho Books for POS Invoices. Follows exactly the same
    /// architecture as AgentSyncConsole.Services.BooksInvoiceSyncService and reuses
    /// its infrastructure directly rather than re-implementing it:
    ///   - IAccessTokenService for the Books token (same "Books" application).
    ///   - ILocationMasterRepository for Location_Master (POS Invoices carry
    ///     Hotel_ID exactly like Invoice does, so the lookup is identical).
    ///   - IItemMasterRepository for Books Item ID resolution (Product_Name lookup,
    ///     same as Invoice's line item Name lookup) — "do not create a separate
    ///     lookup" is honored by calling the existing repository as-is.
    ///   - IGSTService.ResolveTaxIdAsync for Tax_Master -> Books Tax ID resolution
    ///     ("do not duplicate tax logic") — driven by the GST_Type/Tax_Rate each
    ///     PosInvoiceLineItem already carries (computed from taxBreakup[] at the
    ///     JSON -> SQL stage, per spec — no Transaction JSON/table involved).
    ///   - IBooksApiService for every Books HTTP call (Get/Create/Update/MarkSent) —
    ///     no new HTTP code was written for POS Invoices.
    ///   - Models.BooksInvoicePayload / BooksLineItem as the payload builder —
    ///     "do not rewrite invoice creation logic".
    ///   - Models.InvoiceSyncSummary as the result shape.
    ///
    /// Customer: every POS Invoice is created against the Cash Customer, whose
    /// Books ID is read from configuration ("GuestCustomerMapping:CashCustomerBooksId" —
    /// the same key BooksInvoiceSyncService already uses for Guest/Cash invoices,
    /// per spec: never hardcoded). Swapping this for Guest/Corporate/Agent/Walk-in
    /// later only requires changing how `customerBooksID` is resolved below —
    /// nothing else in this service needs to change.
    /// </summary>
    public sealed class PosInvoiceBooksSyncService : IPosInvoiceBooksSyncService
    {
        private readonly IAccessTokenService _accessTokenService;
        private readonly IPosInvoiceRepository _invoiceRepository;
        private readonly IPosInvoiceLineItemRepository _lineItemRepository;
        private readonly ILocationMasterRepository _locationMasterRepository;
        private readonly IItemMasterRepository _itemMasterRepository;
        private readonly IGSTService _gstService;
        private readonly IBooksApiService _booksApi;
        private readonly ICustomerRepository _customerRepository;
        private readonly ILogger<PosInvoiceBooksSyncService> _logger;


        private readonly string _tokenApplication;

        // Configured Customer Books IDs for different payment modes
        private readonly string _cashCustomerBooksId;
        private readonly string _upiCustomerBooksId;
        private readonly string _creditCardCustomerBooksId;

        private readonly string _itemFallbackId;
        private readonly string _fallbackCustomerBooksId;
        private readonly string _defaultInvoiceDate;
        private readonly string _defaultDueDate;
        private readonly string _defaultPaymentTerm;
        private readonly string _gstTreatment;

        public PosInvoiceBooksSyncService(
            IAccessTokenService accessTokenService,
            IPosInvoiceRepository invoiceRepository,
            IPosInvoiceLineItemRepository lineItemRepository,
            ILocationMasterRepository locationMasterRepository,
            IItemMasterRepository itemMasterRepository,
            IGSTService gstService,
            IBooksApiService booksApi,
            ICustomerRepository customerRepository,
            IConfiguration configuration,
            ILogger<PosInvoiceBooksSyncService> logger)
        {
            _accessTokenService = accessTokenService;
            _invoiceRepository = invoiceRepository;
            _lineItemRepository = lineItemRepository;
            _locationMasterRepository = locationMasterRepository;
            _itemMasterRepository = itemMasterRepository;
            _gstService = gstService;
            _booksApi = booksApi;
            _customerRepository = customerRepository;
            _logger = logger;

            _tokenApplication = configuration["ZohoAuth:TokenApplication"] ?? "Books";
            _itemFallbackId = configuration["itemFallback:Itemfallback"] ?? string.Empty;

            _fallbackCustomerBooksId =
                configuration["Sync:HardcodedFallbackCustomerBooksId"]
                ?? string.Empty;
            // Same configuration key BooksInvoiceSyncService already reads for Guest/Cash
            // invoices — reused here, never hardcoded, per spec.
            // 1. Read Customer Books IDs from appsettings.json
            _cashCustomerBooksId = configuration["GuestCustomerMapping:CashCustomerBooksId"] ?? string.Empty;
            _upiCustomerBooksId = configuration["GuestCustomerMapping:UpiCustomerBooksId"] ?? string.Empty;
            _creditCardCustomerBooksId = configuration["GuestCustomerMapping:CreditCardCustomerBooksId"] ?? string.Empty;

            // Example usage inside PosInvoiceBooksSyncService
            //string customerBooksId = PaymentModeCustomerResolver.ResolveCustomerBooksId(
            //    paymentModeFromInvoice, // extracted payment mode (e.g., "CC", "OTH")
            //    _cashCustomerBooksId,
            //    _upiCustomerBooksId,
            //    _creditCardCustomerBooksId
            //);


            _defaultInvoiceDate = configuration["Sync:DefaultInvoiceDate"] ?? "2026-05-15";
            _defaultDueDate = configuration["Sync:DefaultDueDate"] ?? "2026-05-15";
            _defaultPaymentTerm = configuration["Sync:DefaultPaymentTerm"] ?? "Due on Receipt";
            _gstTreatment = configuration["Sync:PosInvoiceGstTreatment"] ?? "business_none";
        }

        public async Task<InvoiceSyncSummary> RunAsync(CancellationToken ct = default)
        {
            var summary = new InvoiceSyncSummary();

            try
            {
                var latestToken = await _accessTokenService.LoadLatestTokenAsync(_tokenApplication, ct)
                    ?? throw new InvalidOperationException($"No {_tokenApplication} access token found");
                var accessToken = (latestToken.accessToken ?? string.Empty).Trim();
                summary.LatestTokenRow = latestToken;

                if (string.IsNullOrWhiteSpace(_cashCustomerBooksId))
                {
                    _logger.LogError("GuestCustomerMapping:CashCustomerBooksId is not configured — cannot sync any POS Invoice.");
                    summary.Status = "error";
                    return summary;
                }

                var invoiceRows = await _invoiceRepository.GetAllRowsAsync(ct);
                _logger.LogInformation("Loaded {Count} PosInvoice rows from the database", invoiceRows.Count);

                foreach (var invoiceData in invoiceRows)
                {
                    var invoiceROWID = invoiceData.ROWID;

                    try
                    {
                        // -----------------------------------------
                        // VALIDATE Invoice_ID
                        // -----------------------------------------
                        var invoiceID = (invoiceData.Invoice_ID ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(invoiceID))
                        {
                            await FailRowAsync(invoiceROWID, "PosInvoice.Invoice_ID is blank", ct);
                            summary.SkippedInvoices.Add(new { ROWID = invoiceROWID, reason = "PosInvoice.Invoice_ID is blank" });
                            continue;
                        }

                        // Already successfully synced — skip (POS Invoices are not
                        // re-posted every run the way the Invoice module currently
                        // re-POST/PUTs every row regardless of prior status).
                        if (string.Equals(invoiceData.Books_Status, Constants.BooksStatusProcessed, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // -----------------------------------------
                        // CUSTOMER — always Cash Customer, from configuration
                        // -----------------------------------------
                        var customerBooksID = _cashCustomerBooksId;

                        // -----------------------------------------
                        // LOCATION MASTER LOOKUP — identical to the Invoice module
                        // -----------------------------------------
                        var hotelID = (invoiceData.HotelID ?? string.Empty).Trim();
                        var location = string.IsNullOrEmpty(hotelID)
                            ? null
                            : await _locationMasterRepository.FindByHotelIdAsync(hotelID, ct);

                        var locationBooksID = location?.locationID ?? string.Empty;
                        var locationStateCode = (location?.stateCode ?? string.Empty).Trim().ToUpperInvariant();

                        if (string.IsNullOrEmpty(locationBooksID))
                        {
                            await FailRowAsync(invoiceROWID, "No locationID in Location_Master for Hotel_ID", ct);
                            summary.SkippedInvoices.Add(new { InvoiceID = invoiceID, reason = "No locationID in Location_Master for Hotel_ID", Hotel_ID = hotelID });
                            continue;
                        }

                        // POS Invoices are posted against the Cash Customer, which has no
                        // Customer.Place_Of_Supply on file — fall back to the POS point's
                        // own Location_Master.stateCode, the same treatment Guest invoices
                        // already receive in the Invoice module.
                        var placeOfSupply = !string.IsNullOrEmpty(locationStateCode) ? locationStateCode : string.Empty;


                        //checking with the payment mode and assigning the customerBooksID accordingly
                        if (invoiceData.PaymentMode != null)
                        {
                            var paymentMode = invoiceData.PaymentMode.Trim().ToUpperInvariant();
                            customerBooksID = PaymentModeCustomerResolver.ResolveCustomerBooksId(
                                paymentMode,
                                _cashCustomerBooksId,
                                _upiCustomerBooksId,
                                _creditCardCustomerBooksId
                            );
                        }
                        else
                        {
                            customerBooksID = _cashCustomerBooksId; // Default to Cash Customer if PaymentMode is null
                        }

                        // Customer Place_Of_Supply lookup — reuses the existing
                        // ICustomerRepository.FindByCustomerIdAsync the Invoice module already
                        // uses for this exact purpose. Falls back to locationStateCode when the
                        // customer record or its Place_Of_Supply is unavailable.
                        var customer = await _customerRepository.FindByCustomerIdAsync(customerBooksID, ct);
                        var customerStateCode = (customer?.Place_Of_Supply ?? string.Empty).Trim().ToUpperInvariant();
                        placeOfSupply = !string.IsNullOrWhiteSpace(customerStateCode) ? customerStateCode : locationStateCode;


                        // -----------------------------------------
                        // LINE ITEMS — Item Master + Tax Master resolution, reusing the
                        // exact same repositories/service the Invoice module uses.
                        // -----------------------------------------
                        var lineItemRows = await _lineItemRepository.GetByInvoiceIdAsync(invoiceID, ct);
                        var lineItems = new List<BooksLineItem>();
                        var missingItemMappings = new List<string>();

                        foreach (var li in lineItemRows)
                        {
                            var productName = (li.Product_Name ?? string.Empty).Trim();

                            if (string.IsNullOrWhiteSpace(productName))
                            {
                                missingItemMappings.Add("Product not found in Item_Master : (blank line item name)");
                                continue;
                            }

                            var itemMaster = await _itemMasterRepository.FindByProductNameAsync(productName, ct);

                            // NEW — FALLBACK BOOKS ITEM ID
                            // If Item_Master is missing, or is found but has no BooksID, use the
                            // configured fallback Books Item ID (itemFallback:Itemfallback) instead
                            // of skipping the line item. Only when both the real BooksID and the
                            // fallback are unavailable is the line item recorded as a missing mapping.
                            string booksItemId;

                            if (itemMaster is not null && !string.IsNullOrWhiteSpace(itemMaster.BooksID))
                            {
                                booksItemId = itemMaster.BooksID;
                            }
                            else if (!string.IsNullOrWhiteSpace(_itemFallbackId))
                            {
                                booksItemId = _itemFallbackId;
                                _logger.LogWarning("USING FALLBACK BOOKS ITEM ID => Product_Name={ProductName}, FallbackItemId={FallbackItemId}", productName, _itemFallbackId);
                            }
                            else
                            {
                                missingItemMappings.Add($"Product not found in Item_Master and no fallback Item ID configured (itemFallback:Itemfallback) : {productName}");
                                continue;
                            }

                            var hsnOrSac = li.hsnCode?.Trim();
                            var isNonGstSupply =
                                string.IsNullOrWhiteSpace(hsnOrSac) ||
                                hsnOrSac == "0";

                            string? itemTaxID = null;
                            string gstTreatmentCode = "";

                            if (!isNonGstSupply)
                            {
                                // Existing GST behavior — unchanged.
                                var gstType = !string.IsNullOrWhiteSpace(li.GST_Type) ? li.GST_Type : Constants.GstTypeGst;
                                itemTaxID = await _gstService.ResolveTaxIdAsync(
                                    gstType, li.TaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
                                Console.WriteLine($"TAXID:-->>{itemTaxID}");
                            }
                            else
                            {
                                // Non-GST line: do not skip it, do not send TaxId, mark it
                                // gst_treatment_code=non_gst_supply, use the existing default
                                // HSN fallback ("1") already used elsewhere in this method.
                                gstTreatmentCode = "non_gst_supply";
                                hsnOrSac = "123456";
                            }

                            var itemDisplayName = !string.IsNullOrWhiteSpace(itemMaster?.Categories) ? itemMaster!.Categories!.Trim() : productName;
                            var accountID = itemMaster?.COA_Id;
                            lineItems.Add(new BooksLineItem
                            {
                                ItemId = booksItemId,
                                Name = itemDisplayName,
                                Description = productName,
                                // Unit_Price (not Total_Price) so Books' rate * quantity
                                // reproduces the line total correctly.
                                Rate = li.Unit_Price.ToString(),
                                account_id = accountID,
                                Quantity = string.IsNullOrWhiteSpace(li.Quantity) ? "1" : li.Quantity,
                                TaxId = itemTaxID,
                                HsnOrSac = hsnOrSac,
                                GstTreatmentCode = gstTreatmentCode,
                                Unit = Constants.DefaultUnit
                            });
                            Console.WriteLine($"lineItems{lineItems}");
                        }

                        if (missingItemMappings.Count > 0)
                        {
                            var responsePayload = new { status = "FAILED", error = "Missing Item Mapping", missingProducts = missingItemMappings };
                            await FailRowAsync(invoiceROWID, JsonHelper.Serialize(responsePayload), ct);
                            summary.SkippedInvoices.Add(new
                            {
                                InvoiceID = invoiceID,
                                InvoiceNumber = invoiceData.Invoice_Number ?? string.Empty,
                                HotelID = hotelID,
                                reason = "Missing Item Mapping",
                                missingProducts = missingItemMappings
                            });
                            continue;
                        }

                        if (lineItems.Count == 0)
                        {
                            lineItems.Add(new BooksLineItem
                            {
                                Name = Constants.DefaultLineItemName,
                                Description = Constants.DefaultLineItemDescription,
                                Rate = "1",
                                Quantity = "1",
                                TaxId = "",
                                HsnOrSac = null,
                                Unit = Constants.DefaultUnit
                            });
                        }

                        // -----------------------------------------
                        // BUILD PAYLOAD — same Models.BooksInvoicePayload/BooksLineItem the
                        // Invoice module uses; no new payload type was introduced.
                        // -----------------------------------------
                        var effectiveBooksInvoiceID = (invoiceData.BooksInvoiceID ?? string.Empty).Trim();
                        var invoiceNumber = !string.IsNullOrEmpty(invoiceData.Invoice_Number)
                            ? invoiceData.Invoice_Number!
                            : (!string.IsNullOrEmpty(invoiceData.Invoice_No) ? invoiceData.Invoice_No! : "POS" + invoiceROWID);

                        var payloadObj = new BooksInvoicePayload
                        {
                            CustomerId = customerBooksID,//
                            LocationId = locationBooksID,
                            InvoiceNumber = invoiceNumber,
                            Date = DateHelper.OrDefault(invoiceData.CreatedOn, _defaultInvoiceDate),
                            DueDate = DateHelper.OrDefault(invoiceData.SettledOn, _defaultDueDate),
                            PaymentTermsLabel = "Payment due immediately.",
                            PlaceOfSupply = placeOfSupply,
                            GstTreatment = _gstTreatment,
                            LineItems = lineItems,
                            reason = "Updating Invoice Details",
                            Notes = "Thanks for your business.",
                            Terms = "Payment due immediately."
                        };


                        Console.WriteLine(payloadObj);





                        Console.WriteLine("WE ARE AT LINE 293............");
                        // -----------------------------------------
                        // VALIDATE EXISTING BooksInvoiceID — same stale-ID handling as Invoice
                        // -----------------------------------------
                        if (!string.IsNullOrEmpty(effectiveBooksInvoiceID))
                        {
                            var verifyResp = await _booksApi.GetInvoiceAsync(accessToken, effectiveBooksInvoiceID, ct);
                            var invoiceGone = verifyResp.Code is null
                                || verifyResp.Code == Constants.BooksCodeResourceNotFound
                                || verifyResp.Code == Constants.BooksCodeInvalidId;

                            if (invoiceGone)
                            {
                                _logger.LogInformation("STALE BooksInvoiceID DETECTED => {BooksInvoiceID} — clearing and switching to POST", effectiveBooksInvoiceID);
                                await _invoiceRepository.UpdateRowAsync(new PosInvoice
                                {
                                    ROWID = invoiceROWID,
                                    BooksInvoiceID = "",
                                    Books_Status = Constants.BooksStatusPending
                                }, ct);
                                effectiveBooksInvoiceID = "";
                            }
                        }

                        // -----------------------------------------
                        // HTTP METHOD & API CALL — reuses IBooksApiService as-is
                        // -----------------------------------------
                        var method = string.IsNullOrEmpty(effectiveBooksInvoiceID) ? "POST" : "PUT";

                        var parsedResponse = method == "POST"
                            ? await _booksApi.CreateInvoiceAsync(accessToken, payloadObj, ct)
                            : await _booksApi.UpdateInvoiceAsync(accessToken, effectiveBooksInvoiceID, payloadObj, ct);

                        var apiBooksInvoiceID = parsedResponse.Invoice?.InvoiceId ?? string.Empty;
                        var finalBooksInvoiceID = !string.IsNullOrEmpty(apiBooksInvoiceID) ? apiBooksInvoiceID : effectiveBooksInvoiceID;
                        var isSuccess = parsedResponse.Code == Constants.BooksCodeSuccess;

                        var updateObj = new PosInvoice
                        {
                            ROWID = invoiceROWID,
                            Books_Status = isSuccess ? Constants.BooksStatusProcessed : Constants.BooksStatusFailed,
                            Response = JsonHelper.Serialize(new
                            {
                                status = isSuccess ? (method == "POST" ? "CREATED" : "UPDATED") : "FAILED",
                                booksInvoiceID = finalBooksInvoiceID,
                                response = parsedResponse.RawBody
                            })
                        };

                        if (isSuccess && method == "POST" && !string.IsNullOrEmpty(finalBooksInvoiceID))
                        {
                            updateObj.BooksInvoiceID = finalBooksInvoiceID;

                            try
                            {
                                var sentResp = await _booksApi.MarkInvoiceAsSentAsync(accessToken, finalBooksInvoiceID, ct);
                                if (sentResp.Code != Constants.BooksCodeSuccess)
                                {
                                    _logger.LogWarning("POS Invoice MARK_SENT_FAILED => booksInvoiceID={BooksInvoiceID}, response={Response}", finalBooksInvoiceID, sentResp.RawBody);
                                }
                            }
                            catch (Exception sentEx)
                            {
                                _logger.LogError(sentEx, "POS Invoice MARK_SENT_EXCEPTION => booksInvoiceID={BooksInvoiceID}", finalBooksInvoiceID);
                            }
                        }

                        await _invoiceRepository.UpdateRowAsync(updateObj, ct);

                        var record = new InvoiceSyncRecord
                        {
                            InvoiceID = invoiceID,
                            InvoiceNumber = invoiceNumber,
                            BooksInvoiceID = finalBooksInvoiceID,
                            Response = parsedResponse.RawBody
                        };

                        if (isSuccess)
                        {
                            if (method == "POST") summary.CreatedInvoices.Add(record);
                            else summary.UpdatedInvoices.Add(record);
                        }
                        else
                        {
                            summary.SkippedInvoices.Add(new { InvoiceID = invoiceID, invoiceNumber, response = parsedResponse.RawBody });
                        }
                    }
                    catch (Exception rowEx)
                    {
                        _logger.LogError(rowEx, "POS INVOICE ROW FAILED — SKIPPING THIS ROW ONLY => InvoiceID={InvoiceID} | ROWID={ROWID}",
                            invoiceData.Invoice_ID, invoiceROWID);

                        try
                        {
                            await FailRowAsync(invoiceROWID, rowEx.Message, ct);
                        }
                        catch (Exception updateEx)
                        {
                            _logger.LogError(updateEx, "FAILED TO WRITE FAILURE STATUS => InvoiceID={InvoiceID}", invoiceData.Invoice_ID);
                        }

                        summary.SkippedInvoices.Add(new { InvoiceID = invoiceData.Invoice_ID ?? string.Empty, reason = rowEx.Message });
                    }
                }

                summary.Status = "success";
                summary.TotalCreated = summary.CreatedInvoices.Count;
                summary.TotalUpdated = summary.UpdatedInvoices.Count;
                summary.TotalSkipped = summary.SkippedInvoices.Count;
                return summary;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "PosInvoiceBooksSyncService failed");
                summary.Status = "error";
                return summary;
            }
        }

        private async Task FailRowAsync(int rowId, string reason, CancellationToken ct)
        {
            await _invoiceRepository.UpdateRowAsync(new PosInvoice
            {
                ROWID = rowId,
                Books_Status = Constants.BooksStatusFailed,
                Response = JsonHelper.Serialize(new { status = "FAILED", error = reason })
            }, ct);
        }
    }
}