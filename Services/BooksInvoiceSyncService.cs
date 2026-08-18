using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using AgentSyncConsole.Utilites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace AgentSyncConsole.Services;

/// <summary>
/// Converted 1:1 from the Catalyst function "CatalystToBooksInvoices" (index.js,
/// module.exports async (context, basicIO) => {...}). Every numbered STEP comment
/// below corresponds exactly to the numbered step in the original source, in the
/// same order, with the same conditions, the same hardcoded fallback value, and
/// the same fields written back to the Invoice row. Nothing has been simplified
/// or removed; only the language and the persistence layer changed (Catalyst
/// datastore/zcql -> SQL Server via repositories).
/// </summary>
public class BooksInvoiceSyncService : IBooksInvoiceSyncService
{
    private readonly IAccessTokenService _accessTokenService;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILocationMasterRepository _locationMasterRepository;
    private readonly IInvoiceLineItemRepository _lineItemRepository;
    private readonly IItemMasterRepository _itemMasterRepository;
    private readonly IGSTService _gstService;
    private readonly IBooksApiService _booksApi;
    private readonly ILogger<BooksInvoiceSyncService> _logger;

    private readonly string _tokenApplication;
    private readonly string _hardcodedFallbackCustomerBooksId;
    private readonly string _defaultInvoiceDate;
    private readonly string _defaultDueDate;
    private readonly string _defaultPaymentTerm;
    private readonly decimal _defaultHsnOrSac;
    private readonly string _hardcodedFallbackItemBooksId;
    // NEW (Change 3/8) — Guest Owner_Type Books Customer IDs, sourced from appsettings.json.
    private readonly string _cashCustomerBooksId;
    private readonly string _upiCustomerBooksId;
    private readonly string _creditCardCustomerBooksId;
    // NEW — Fallback Books Item ID, sourced from appsettings.json (itemFallback:Itemfallback).
    // Used when a line item's Item_Master record cannot be found, or is found but has no
    // BooksID, so the line item is not skipped/failed just because of a missing mapping.
    private readonly string _itemFallbackId;
    private string gstTreatment;

    public BooksInvoiceSyncService(
        IAccessTokenService accessTokenService,
        IInvoiceRepository invoiceRepository,
        ICustomerRepository customerRepository,
        ILocationMasterRepository locationMasterRepository,
        IInvoiceLineItemRepository lineItemRepository,
        IItemMasterRepository itemMasterRepository,
        IGSTService gstService,
        IBooksApiService booksApi,
        IConfiguration configuration,
        ILogger<BooksInvoiceSyncService> logger)
    {
        _accessTokenService = accessTokenService;
        _invoiceRepository = invoiceRepository;
        _customerRepository = customerRepository;
        _locationMasterRepository = locationMasterRepository;
        _lineItemRepository = lineItemRepository;
        _itemMasterRepository = itemMasterRepository;
        _gstService = gstService;
        _booksApi = booksApi;
        _logger = logger;

        _tokenApplication = configuration["ZohoAuth:TokenApplication"] ?? "Books";
        _hardcodedFallbackCustomerBooksId = configuration["Sync:HardcodedFallbackCustomerBooksId"] ?? "3233228000000045007";
        _hardcodedFallbackItemBooksId = configuration["Sync:itemFallback"] ?? "3233228000000946070";
        _defaultInvoiceDate = configuration["Sync:DefaultInvoiceDate"] ?? "2026-05-15";
        _defaultDueDate = configuration["Sync:DefaultDueDate"] ?? "2026-05-15";
        _defaultPaymentTerm = configuration["Sync:DefaultPaymentTerm"] ?? "Due on Receipt";
        _defaultHsnOrSac = configuration.GetValue<decimal?>("Sync:DefaultHsnOrSac") ?? 123456m;

        // NEW (Change 3/8)
        _cashCustomerBooksId = configuration["GuestCustomerMapping:CashCustomerBooksId"] ?? string.Empty;
        _upiCustomerBooksId = configuration["GuestCustomerMapping:UpiCustomerBooksId"] ?? string.Empty;
        _creditCardCustomerBooksId = configuration["GuestCustomerMapping:CreditCardCustomerBooksId"] ?? string.Empty;

        // NEW — Fallback Books Item ID (itemFallback:Itemfallback)
        _itemFallbackId = configuration["itemFallback:Itemfallback"] ?? "";
    }

    public async Task<InvoiceSyncSummary> RunAsync(CancellationToken ct = default)
    {
        var summary = new InvoiceSyncSummary();
        Console.WriteLine("Starting BooksInvoiceSyncService.RunAsync...");
        Console.WriteLine("boooks summary", summary.LatestTokenRow);
        var normalizedTaxRate = "";
        try
        {
            // =====================
            // ACCESS TOKEN
            // =====================
            Console.WriteLine("We are inside try");
            var latestToken = await _accessTokenService.LoadLatestTokenAsync(_tokenApplication, ct)
                ?? throw new InvalidOperationException($"No {_tokenApplication} access token found");
            Console.WriteLine("latest token", latestToken);
            var accessToken = (latestToken.accessToken ?? string.Empty).Trim();
            summary.LatestTokenRow = latestToken;
            Console.WriteLine($"Latest {_tokenApplication} access token: {accessToken}");
            // =====================
            // LOAD INVOICES
            // =====================
            var invoiceRows = await _invoiceRepository.GetAllRowsAsync(ct);
            Console.WriteLine($"Loaded {invoiceRows.Count} invoice rows from the database");
            //Console.WriteLine($"invoice rows { invoiceRows}");
            //Console.WriteLine("invoice rows", invoiceRows);
            foreach (var invoiceData in invoiceRows)
            {

                //Console.WriteLine("-------------------------------------------------------------------");
                var invoiceROWID = invoiceData.ROWID;
                var invoiceCustomerName = invoiceData.Customer_Name ?? string.Empty;
                //Console.WriteLine($"Processing invoice ROWID: {invoiceROWID}");
                //Console.WriteLine($"Processing invoice customer id: {invoiceCustomerName}");
                //Console.WriteLine($"invoice ID: {invoiceData.InvoiceID}");
                //Console.WriteLine($"invoice status: {invoiceData.Books_Status}");
                //Console.WriteLine($"invoice id: {invoiceData.InvoiceID}");
                //Console.WriteLine($"invoice hotel id: {invoiceData.Hotel_ID}");
                //Console.WriteLine($"invoice customer: {invoiceData.Customer_Name}");
                //Console.WriteLine($"invoice owner type: {invoiceData.Owner_Type}");

                //Console.WriteLine("-------------------------------------------------------------------");
                // -----------------------------------------
                // PER-INVOICE ISOLATION (NEW)
                // Every invoice row gets its own try/catch. A failure on ANY one
                // row — regardless of Owner_Type (Agent, Corporate, Company, Guest,
                // etc.) — is logged and recorded against that row only; it never
                // stops the loop from reaching the remaining rows. This is what
                // guarantees a later Guest invoice still gets created even if an
                // earlier Agent/Corporate/Company row throws (e.g. blank
                // Place_Of_Supply at STEP 6 below).
                // -----------------------------------------
                try
                {
                    if (invoiceData != null)
                    {
                        // -----------------------------------------
                        // STEP 1 — VALIDATE InvoiceID
                        // -----------------------------------------
                        var invoiceID = (invoiceData.InvoiceID ?? string.Empty).Trim();

                        if (string.IsNullOrEmpty(invoiceID))
                        {
                            //Console.WriteLine("invoice ID is blank");
                            await _invoiceRepository.UpdateRowAsync(new Invoice
                            {
                                ROWID = invoiceROWID,
                                Books_Status = Constants.BooksStatusFailed,
                                Response = JsonHelper.Serialize(new { status = "FAILED", error = "Invoice.InvoiceID is blank" })
                            }, ct);
                            summary.SkippedInvoices.Add(new { ROWID = invoiceROWID, reason = "Invoice.InvoiceID is blank" });
                            continue;
                        }

                        // -----------------------------------------
                        // STEP 1a — TAX INVOICE FILTER (NEW — Change 1)
                        // Only invoices whose Invoice_Number starts with "INVC" are
                        // Tax Invoices. Everything else (e.g. Proforma Invoices like
                        // "PI 61349") is skipped silently: no payload, no line items,
                        // no row update, no error thrown.
                        // -----------------------------------------
                        //try
                        //{
                        //    var invoiceNumberForFilter = (invoiceData.Invoice_Number ?? string.Empty).Trim();
                        //    var isTaxInvoice = invoiceNumberForFilter.StartsWith(Constants.TaxInvoiceFolioPrefix, StringComparison.OrdinalIgnoreCase);

                        //    if (!isTaxInvoice)
                        //    {
                        //        _logger.LogInformation("SKIPPED PI INVOICE => Invoice_Number={InvoiceNumber} | InvoiceID={InvoiceID}", invoiceNumberForFilter, invoiceID);
                        //        Console.WriteLine($"Skipped PI Invoice: Invoice_Number={invoiceNumberForFilter}, InvoiceID={invoiceID}");
                        //        continue;
                        //    }
                        //}
                        //catch (Exception folioEx)
                        //{
                        //    // Null-safe: if anything unexpected happens while reading the
                        //    // Invoice_Number field, do not let it take down the whole sync
                        //    // run — fall through and let the invoice continue through the
                        //    // existing flow unchanged.
                        //    _logger.LogError(folioEx, "TAX INVOICE FILTER ERROR => InvoiceID={InvoiceID}", invoiceID);
                        //}

                        // -----------------------------------------
                        // STEP 2 — OWNER TYPE VALIDATION
                        // -----------------------------------------
                        var ownerType = (invoiceData.Owner_Type ?? string.Empty).Trim().ToUpperInvariant();

                        _logger.LogInformation("OWNER_TYPE => {OwnerType} | InvoiceID => {InvoiceID}", ownerType, invoiceID);
                        // NOTE (Change 2): the previous "skip all Guest invoices" block that
                        // lived here has been removed. Guest invoices now flow through to
                        // STEP 4 below, where they are handled by new, additive logic.

                        // -----------------------------------------
                        // STEP 3 — VALIDATE Hotel_ID
                        // -----------------------------------------
                        var hotelID = (invoiceData.Hotel_ID ?? string.Empty).Trim();
                        _logger.LogInformation("INVOICE HOTEL_ID => {HotelID}", hotelID);
                        //Console.WriteLine("hotel ID", hotelID.Count());
                        if (string.IsNullOrEmpty(hotelID))
                        {
                            await _invoiceRepository.UpdateRowAsync(new Invoice
                            {
                                ROWID = invoiceROWID,
                                Books_Status = Constants.BooksStatusFailed,
                                Response = JsonHelper.Serialize(new { status = "FAILED", error = "Invoice.Hotel_ID is blank" })
                            }, ct);
                            summary.SkippedInvoices.Add(new { InvoiceID = invoiceID, reason = "Invoice.Hotel_ID is blank" });
                            continue;
                        }

                        // -----------------------------------------
                        // STEP 4 — CUSTOMER LOOKUP
                        // Guest (Change 2/3): no Customer table, no booksID search, no
                        // Customer Repository call — resolved from Payment_Term instead.
                        // Everything else (Company/Agent/Corporate/Travel Agent/...):
                        // exact existing logic, untouched (Change 4).
                        // -----------------------------------------
                        var customerBooksID = string.Empty;
                        var customerStateCode = string.Empty;
                        var customerIDSource = string.Empty;
                        var customerGstNo = string.Empty;
                        var isGuestInvoice = ownerType == Constants.OwnerTypeGuest;


                        if (isGuestInvoice)
                        {
                            _logger.LogInformation("GUEST INVOICE DETECTED => InvoiceID={InvoiceID}", invoiceID);
                            //Console.WriteLine($"Guest Invoice Detected: InvoiceID={invoiceID}");

                            var guestPaymentTerm = (invoiceData.Payment_Term ?? string.Empty).Trim();
                            _logger.LogInformation("PAYMENT TERM => {PaymentTerm} | InvoiceID={InvoiceID}", guestPaymentTerm, invoiceID);
                            //Console.WriteLine($"Payment Term: {guestPaymentTerm}");

                            var guestPaymentTermUpper = guestPaymentTerm.ToUpperInvariant();
                            var resolvedGuestCustomerId = string.Empty;

                            try
                            {
                                if (guestPaymentTermUpper.Contains("CASH"))
                                {
                                    resolvedGuestCustomerId = _cashCustomerBooksId ?? string.Empty;
                                }
                                else if (guestPaymentTermUpper.Contains("UPI"))
                                {
                                    resolvedGuestCustomerId = _upiCustomerBooksId ?? string.Empty;
                                }
                                else if (guestPaymentTermUpper.Contains("CARD"))
                                {
                                    // Covers "Card", "Credit Card", "Debit Card" — all contain "CARD".
                                    resolvedGuestCustomerId = _creditCardCustomerBooksId ?? string.Empty;
                                }
                                else
                                {
                                    resolvedGuestCustomerId = _cashCustomerBooksId ?? string.Empty;
                                }
                            }
                            catch (Exception guestMapEx)
                            {
                                _logger.LogError(guestMapEx, "GUEST PAYMENT TERM MAPPING ERROR => InvoiceID={InvoiceID}", invoiceID);
                                resolvedGuestCustomerId = string.Empty;
                            }

                            if (string.IsNullOrWhiteSpace(resolvedGuestCustomerId))
                            {
                                await _invoiceRepository.UpdateRowAsync(new Invoice
                                {
                                    ROWID = invoiceROWID,
                                    Books_Status = Constants.BooksStatusFailed,
                                    Response = JsonHelper.Serialize(new { status = "FAILED", error = "Unknown Guest Payment Term", Payment_Term = guestPaymentTerm })
                                }, ct);
                                summary.SkippedInvoices.Add(new { InvoiceID = invoiceID, reason = "Unknown Guest Payment Term", Payment_Term = guestPaymentTerm });
                                _logger.LogWarning("UNKNOWN PAYMENT TERM => Payment_Term={PaymentTerm} | InvoiceID={InvoiceID}", guestPaymentTerm, invoiceID);
                                continue;
                            }

                            customerBooksID = resolvedGuestCustomerId;
                            customerIDSource = $"Guest Payment_Term mapping ({guestPaymentTerm}) — Customer table not queried";
                            // customerStateCode intentionally left blank here — it is backfilled
                            // from Location_Master.stateCode right after STEP 5 below, since Guest
                            // invoices have no Customer.Place_Of_Supply on file.
                            _logger.LogInformation("SELECTED BOOKS CUSTOMER => {CustomerBooksID} | Source={Source} | InvoiceID={InvoiceID}", customerBooksID, customerIDSource, invoiceID);
                            //Console.WriteLine($"Selected Books Customer: {customerBooksID} (source: {customerIDSource})");
                        }
                        else
                        {
                            _logger.LogInformation("CUSTOMER QUERY => CustomerID={CustomerName}", invoiceData.Customer_Name);
                            var customer = await _customerRepository.FindByCustomerIdAsync(invoiceData.Customer_Name ?? string.Empty, ct);
                            _logger.LogInformation("CUSTOMER RESULT => {Customer}", JsonHelper.Serialize(customer));

                          

                            if (customer is not null)
                            {
                                customerBooksID = customer.booksID ?? string.Empty;
                                customerStateCode = (customer.Place_Of_Supply ?? string.Empty).Trim().ToUpperInvariant();
                                customerGstNo = (customer.GST_No ?? string.Empty).Trim();
                                customerIDSource = $"Customer.booksID for CustomerID={invoiceData.Customer_Name}";
                                Console.WriteLine($"customer books ID: {customerBooksID}");
                                Console.WriteLine($"customer state code: {customerStateCode}");
                                Console.WriteLine($"ID source: {customerIDSource}");
                            }
                            
                            _logger.LogInformation("CUSTOMER BOOKS ID => {CustomerBooksID} | SOURCE => {Source}", customerBooksID, customerIDSource);
                            _logger.LogInformation("CUSTOMER PLACE_OF_SUPPLY => {CustomerStateCode}", customerStateCode);

                            if (string.IsNullOrEmpty(customerBooksID))
                            {
                                customerBooksID = _hardcodedFallbackCustomerBooksId;
                                customerIDSource = $"HARDCODED FALLBACK — Customer not found for CustomerID={invoiceData.Customer_Name}. Verify this ID exists in org.";
                                _logger.LogWarning("WARNING: CUSTOMER FALLBACK ID IN USE => {CustomerBooksID}", customerBooksID);
                            }
                        }

                        // -----------------------------------------
                        // STEP 5 — LOCATION MASTER LOOKUP
                        // -----------------------------------------
                        var location = await _locationMasterRepository.FindByHotelIdAsync(hotelID, ct);
                        _logger.LogInformation("LOCATION RESULT => {Location}", JsonHelper.Serialize(location));
                        Console.WriteLine($"location result: {JsonHelper.Serialize(location)}");
                        var locationBooksID = location?.locationID ?? string.Empty;
                        var locationStateCode = (location?.stateCode ?? string.Empty).Trim().ToUpperInvariant();
                        Console.WriteLine($"location books ID: {locationBooksID}");
                        Console.WriteLine($"location state code: {locationStateCode}");
                        _logger.LogInformation("LOCATION ID => {LocationBooksID}", locationBooksID);
                        _logger.LogInformation("LOCATION STATECODE => {LocationStateCode}", locationStateCode);

                        if (string.IsNullOrEmpty(locationBooksID))
                        {
                            await _invoiceRepository.UpdateRowAsync(new Invoice
                            {
                                ROWID = invoiceROWID,
                                Books_Status = Constants.BooksStatusFailed,
                                Response = JsonHelper.Serialize(new { status = "FAILED", error = "No locationID in Location_Master for hotelID", Hotel_ID = hotelID })
                            }, ct);
                            summary.SkippedInvoices.Add(new { InvoiceID = invoiceID, reason = "No locationID in Location_Master for hotelID", Hotel_ID = hotelID });
                            continue;
                        }

                        if (string.IsNullOrEmpty(locationStateCode))
                        {
                            await _invoiceRepository.UpdateRowAsync(new Invoice
                            {
                                ROWID = invoiceROWID,
                                Books_Status = Constants.BooksStatusFailed,
                                Response = JsonHelper.Serialize(new { status = "FAILED", error = "Location_Master.stateCode is blank", Hotel_ID = hotelID })
                            }, ct);
                            summary.SkippedInvoices.Add(new { InvoiceID = invoiceID, reason = "Location_Master.stateCode is blank", Hotel_ID = hotelID });
                            continue;
                        }

                        // NEW (Change 3): Guest invoices have no Customer.Place_Of_Supply on
                        // file (Customer table was never queried), so fall back to the
                        // hotel's own Location_Master.stateCode, computed just above.
                        // This makes GST resolve as intra-state (GST, not IGST) for Guests.
                        // TODO: confirm this is the correct treatment for Guest tax invoices.
                        if (isGuestInvoice && string.IsNullOrEmpty(customerStateCode))
                        {
                            customerStateCode = locationStateCode;
                            _logger.LogInformation("GUEST PLACE_OF_SUPPLY DEFAULTED TO LOCATION STATE => {StateCode} | InvoiceID={InvoiceID}", customerStateCode, invoiceID);
                        }

                        // -----------------------------------------
                        // STEP 6 — DETERMINE GST TYPE
                        // 1. Read Customer.Place_Of_Supply
                        // 2. Read Location_Master.stateCode
                        // 3. If equal => GST, else => IGST
                        // 4. If Customer.Place_Of_Supply is blank => throw
                        // -----------------------------------------
                        if (string.IsNullOrEmpty(customerStateCode))
                        {
                            //throw new InvalidOperationException($"Customer Place_of_Supply is blank for CustomerID={invoiceData.Customer_Name}");
                            customerStateCode = locationStateCode;
                        }

                        var gstType = _gstService.DetermineGstType(customerStateCode, locationStateCode);
                        Console.WriteLine($"GST TYPE => {gstType} | Customer Place_of_Supply => {customerStateCode} | Location stateCode => {locationStateCode}");
                        // -----------------------------------------
                        // STEP 7 — FETCH LINE ITEMS
                        // -----------------------------------------
                        var lineItemResult = await _lineItemRepository.GetByInvoiceIdAsync(invoiceID, ct);
                        _logger.LogInformation("LINE ITEMS FETCHED => {Count}", lineItemResult.Count);

                        // -----------------------------------------
                        // STEP 7a — BUILD LINE ITEMS
                        // Tax ID sourced dynamically from Tax_Master using gstType + Tax_Rate,
                        // with fallback to 5% if the exact rate isn't found.
                        // Item_Master.BooksID / HSN_Or_SAC lookups unchanged. No total calc. No 7500 slab logic.
                        //
                        // CHANGED: a missing/invalid Item_Master mapping used to throw and
                        // kill the whole batch. It now records the product name into
                        // missingItemMappings and keeps building the remaining line items;
                        // the invoice is only skipped (not the whole run) once, after the
                        // loop, in the new STEP 7b gate below.
                        //
                        // NEW (performance): itemMasterCache avoids re-querying Item_Master
                        // for the same Product_Name repeated across line items on the same
                        // invoice (e.g. the same dish ordered twice).
                        // -----------------------------------------
                        var lineItems = new List<BooksLineItem>();
                        var missingItemMappings = new List<string>();
                        var itemMasterCache = new Dictionary<string, ItemMaster?>(StringComparer.OrdinalIgnoreCase);

                        foreach (var lineItem in lineItemResult)
                        {
                            var itemName = (lineItem?.Description ?? string.Empty).Trim();
                            _logger.LogInformation("LINE ITEM NAME => {ItemName}", itemName);
                            Console.WriteLine($"line item name: {itemName}");

                            if (string.IsNullOrWhiteSpace(itemName))
                            {
                                missingItemMappings.Add("Product not found in Item_Master : (blank line item name)");
                                continue;
                            }

                            ItemMaster? itemMaster;
                            try
                            {
                                if (!itemMasterCache.TryGetValue(itemName, out itemMaster))
                                {
                                    itemMaster = await _itemMasterRepository.FindByProductNameAsyncFrontDesk(itemName, ct);
                                    itemMasterCache[itemName] = itemMaster;
                                }
                                else
                                {
                                    _logger.LogInformation("ITEM MASTER CACHE HIT => {ItemName}", itemName);
                                }
                            }
                            catch (Exception itemLookupEx)
                            {
                                _logger.LogError(itemLookupEx, "ITEM MASTER LOOKUP ERROR => ItemName={ItemName} | InvoiceID={InvoiceID}", itemName, invoiceID);
                                missingItemMappings.Add($"Product not found in Item_Master : {itemName}");
                                continue;
                            }

                            _logger.LogInformation($"ITEM MASTER ROW => {itemMaster}");
                            Console.WriteLine($"item master row: {JsonHelper.Serialize(itemMaster)}");

                            // NEW — FALLBACK BOOKS ITEM ID
                            // If Item_Master is missing, or is found but has no BooksID, use the
                            // configured fallback Books Item ID (itemFallback:Itemfallback) instead
                            // of skipping the line item. If the fallback itself is not configured,
                            // fail this line item's invoice via the existing missingItemMappings /
                            // STEP 7b gate mechanism — do not send an empty ItemId to Zoho Books.
                            string booksItemId;

                            if (itemMaster is null || string.IsNullOrWhiteSpace(itemMaster.BooksID))
                            {
                                if (string.IsNullOrWhiteSpace(_itemFallbackId))
                                {
                                    missingItemMappings.Add($"Fallback Item ID is not configured in appsettings.json: itemFallback:Itemfallback (product: {itemName})");
                                    continue;
                                }

                                booksItemId = _itemFallbackId;
                            }
                            else
                            {
                                booksItemId = itemMaster.BooksID;
                            }

                            //if (string.IsNullOrEmpty(itemMaster.HSN_Or_SAC))
                            //{
                            //    missingItemMappings.Add($"HSN/SAC missing in Item_Master for product : {itemName}");
                            //    continue;
                            //}

                            if (Convert.ToDecimal(lineItem.Rate) > 0 || Convert.ToDecimal(lineItem.Amount) > 0)
                            {
                                var taxAmount = (Convert.ToDecimal(lineItem.Rate) - Convert.ToDecimal(lineItem.Amount));

                                var tax = Convert.ToInt32((taxAmount / Convert.ToDecimal(lineItem.Amount)) * 100);

                                normalizedTaxRate = tax.ToString();
                                Console.WriteLine($"tax{normalizedTaxRate}");
                            }
                            else
                            {
                                normalizedTaxRate = "0";
                            }

                            var itemTaxID = await _gstService.ResolveTaxIdAsync(gstType, normalizedTaxRate, ct);

                            var itemDisplayName = (itemMaster is not null && !string.IsNullOrWhiteSpace(itemMaster.Categories)) ? itemMaster.Categories!.Trim() : itemName;
                            _logger.LogInformation("PRODUCT NAME => {ItemName}", itemName);
                            _logger.LogInformation("CATEGORY NAME => {DisplayName}", itemDisplayName);

                            var isNonGstSupply =
    string.IsNullOrWhiteSpace(lineItem.HSN_SAC_Code) ||
    lineItem.HSN_SAC_Code.Trim() == "0";

                            if(isNonGstSupply == false)
                            { 
                            lineItems.Add(new BooksLineItem
                            {
                                ItemId = booksItemId,
                                Name = itemMaster.Product_Name + " - " + itemDisplayName,
                                Description = lineItem.Name,
                                Rate = lineItem.Amount,
                                Quantity = lineItem.Quality,
                                TaxId = itemTaxID,
                                HsnOrSac = lineItem.HSN_SAC_Code,
                                Unit = Constants.DefaultUnit
                            });
                            Console.WriteLine($"Added line item: {JsonHelper.Serialize(lineItems)}");
                            }
                            else
                            {
                                lineItems.Add(new BooksLineItem
                                {
                                    ItemId = booksItemId,
                                    Name = itemMaster.Product_Name + " - " + itemDisplayName,
                                    Description = lineItem.Name,
                                    Rate = lineItem.Amount,
                                    Quantity = lineItem.Quality,
                                    TaxId = itemTaxID,
                                    HsnOrSac = _defaultHsnOrSac.ToString(),
                                    GstTreatmentCode = "non_gst_supply",                             
                                    Unit = Constants.DefaultUnit
                                });
                            }
                        }

                        _logger.LogInformation("FINAL LINE ITEMS => {LineItems}", JsonHelper.Serialize(lineItems));

                        // -----------------------------------------
                        // STEP 7b — PRODUCT / ITEM MASTER VALIDATION GATE (NEW)
                        // Runs after all line items are prepared and BEFORE the Zoho Books
                        // Invoice API is called (i.e. before STEP 8 payload / STEP 10 API
                        // call below). If any line item could not be mapped to a valid
                        // Item_Master row, the invoice is skipped entirely — no payload is
                        // built, no Books API call is made — and processing moves on to
                        // the next invoice. Nothing is thrown.
                        // -----------------------------------------
                        if (missingItemMappings.Count > 0)
                        {
                            var missingList = string.Join(Environment.NewLine, missingItemMappings.Select(m => $"- {m}"));
                            var responsePayload = new
                            {
                                status = "FAILED",
                                error = "Missing Item Mapping",
                                missingProducts = missingItemMappings
                            };

                            await _invoiceRepository.UpdateRowAsync(new Invoice
                            {
                                ROWID = invoiceROWID,
                                Books_Status = Constants.BooksStatusFailed,
                                Response = JsonHelper.Serialize(responsePayload)
                            }, ct);

                            summary.SkippedInvoices.Add(new
                            {
                                InvoiceID = invoiceID,
                                InvoiceNumber = invoiceData.Invoice_Number ?? string.Empty,
                                HotelID = hotelID,
                                reason = "Missing Item Mapping",
                                missingProducts = missingItemMappings
                            });

                            _logger.LogWarning(
                                "Invoice {InvoiceNumber} skipped. InvoiceID={InvoiceID} | Hotel_ID={HotelID} | Missing Item Mapping:{NewLine}{MissingList}",
                                invoiceData.Invoice_Number ?? invoiceID, invoiceID, hotelID, Environment.NewLine, missingList);
                            Console.WriteLine($"Invoice {(invoiceData.Invoice_Number ?? invoiceID)} skipped. Missing Item Mapping:{Environment.NewLine}{missingList}");

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
                        // STEP 8 — BUILD PAYLOAD
                        // -----------------------------------------
                        var effectiveBooksInvoiceID = (invoiceData.BooksInvoiceID ?? string.Empty).Trim();
                        var rowIdStr = invoiceROWID.ToString();
                        var rowIdLast4 = rowIdStr.Length > 4 ? rowIdStr[^4..] : rowIdStr;
                        var invoiceNumber = !string.IsNullOrEmpty(invoiceData.Invoice_Number)
                            ? invoiceData.Invoice_Number!
                            : "INV" + rowIdLast4;

                        var payloadObj = new BooksInvoicePayload
                        {
                            CustomerId = customerBooksID,
                            LocationId = locationBooksID,
                            InvoiceNumber = invoiceNumber,
                            Date = DateHelper.OrDefault(invoiceData.Invoice_Date, _defaultInvoiceDate),
                            //DueDate = DateHelper.OrDefault(invoiceData.Due_Date, _defaultDueDate),
                            PaymentTermsLabel = "Due on Receipt",
                            PlaceOfSupply = string.IsNullOrWhiteSpace(customerStateCode)?locationStateCode: customerStateCode,
                            GstTreatment = gstTreatment,
                            LineItems = lineItems,
                            Notes = "Thanks for your business.",
                            Terms = "Payment due immediately."
                        };
                        Console.WriteLine($"payload object: {JsonHelper.Serialize(payloadObj)}");
                        Console.WriteLine("payload", payloadObj);
                        // =========================================
                        // PRE-FLIGHT VALIDATION REMOVED

                        // -----------------------------------------
                        // STEP 9 — VALIDATE EXISTING BooksInvoiceID
                        // If stored ID returns 1002/1004, clear it and fall through to POST mode.
                        // -----------------------------------------
                        if (!string.IsNullOrEmpty(effectiveBooksInvoiceID))
                        {
                            var verifyResp = await _booksApi.GetInvoiceAsync(accessToken, effectiveBooksInvoiceID, ct);

                            _logger.LogInformation("BOOKS_INVOICE_VERIFY => booksInvoiceID={BooksInvoiceID}, code={Code}",
                                effectiveBooksInvoiceID, verifyResp.Code);

                            var invoiceGone = verifyResp.Code is null
                                || verifyResp.Code == Constants.BooksCodeResourceNotFound
                                || verifyResp.Code == Constants.BooksCodeInvalidId;

                            if (invoiceGone)
                            {
                                _logger.LogInformation("STALE BooksInvoiceID DETECTED => {BooksInvoiceID} — clearing and switching to POST", effectiveBooksInvoiceID);

                                await _invoiceRepository.UpdateRowAsync(new Invoice
                                {
                                    ROWID = invoiceROWID,
                                    BooksInvoiceID = "",
                                    Books_Status = Constants.BooksStatusPending,
                                    Response = JsonHelper.Serialize(new
                                    {
                                        status = "STALE_ID_CLEARED",
                                        note = $"BooksInvoiceID {effectiveBooksInvoiceID} returned code {(object?)verifyResp.Code ?? "null"} from Books. ID cleared — this sync will POST a new invoice.",
                                        previousID = effectiveBooksInvoiceID
                                    })
                                }, ct);

                                effectiveBooksInvoiceID = "";
                            }
                        }

                        // -----------------------------------------
                        // STEP 10 — HTTP METHOD & API CALL
                        // -----------------------------------------
                        var method = string.IsNullOrEmpty(effectiveBooksInvoiceID) ? "POST" : "PUT";

                        var parsedResponse = method == "POST"
                            ? await _booksApi.CreateInvoiceAsync(accessToken, payloadObj, ct)
                            : await _booksApi.UpdateInvoiceAsync(accessToken, effectiveBooksInvoiceID, payloadObj, ct);

                        if (parsedResponse.Code == Constants.BooksCodeResourceNotFound)
                        {
                            _logger.LogWarning(
                                "1002 RESOURCE NOT FOUND => One of the IDs does not exist. Pre-flight passed for customer/items — check location_id and tax_ids. customer_id={CustomerId}, location_id={LocationId}",
                                customerBooksID, locationBooksID);
                        }

                        var apiBooksInvoiceID = parsedResponse.Invoice?.InvoiceId ?? string.Empty;
                        var finalBooksInvoiceID = !string.IsNullOrEmpty(apiBooksInvoiceID) ? apiBooksInvoiceID : effectiveBooksInvoiceID;
                        var isSuccess = parsedResponse.Code == Constants.BooksCodeSuccess;

                        // -----------------------------------------
                        // STEP 11 — UPDATE INVOICE ROW
                        // -----------------------------------------
                        var updateObj = new Invoice
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

                            // NEW — mark the newly created invoice as "sent" in Zoho Books.
                            // Best-effort: a failure here should not flip the row back to Failed,
                            // since the invoice itself was already created successfully.
                            try
                            {
                                var sentResp = await _booksApi.MarkInvoiceAsSentAsync(accessToken, finalBooksInvoiceID, ct);
                                _logger.LogInformation("BOOKS_INVOICE_MARK_SENT => booksInvoiceID={BooksInvoiceID}, code={Code}",
                                    finalBooksInvoiceID, sentResp.Code);

                                if (sentResp.Code != Constants.BooksCodeSuccess)
                                {
                                    _logger.LogWarning("BOOKS_INVOICE_MARK_SENT_FAILED => booksInvoiceID={BooksInvoiceID}, response={Response}",
                                        finalBooksInvoiceID, sentResp.RawBody);
                                }
                            }
                            catch (Exception sentEx)
                            {
                                _logger.LogError(sentEx, "BOOKS_INVOICE_MARK_SENT_EXCEPTION => booksInvoiceID={BooksInvoiceID}", finalBooksInvoiceID);
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
                    else
                    {
                        _logger.LogError("ERROR => InvoiceID: {InvoiceID}", invoiceData.InvoiceID ?? invoiceROWID.ToString());

                        await _invoiceRepository.UpdateRowAsync(new Invoice
                        {
                            ROWID = invoiceROWID,
                            Books_Status = Constants.BooksStatusFailed,
                            Response = JsonHelper.Serialize(new { status = "FAILED", error = "error" })
                        }, ct);

                        summary.SkippedInvoices.Add(new { InvoiceID = invoiceData.InvoiceID ?? string.Empty, error = "error" });
                    }
                }
                catch (Exception rowEx)
                {
                    // NEW — per-invoice isolation: this catch is what prevents one
                    // customer type's failure (Agent/Corporate/Company/... or Guest)
                    // from ever stopping processing of the remaining invoice rows.
                    // Only THIS row is marked Failed; the loop continues normally.
                    var failedOwnerType = invoiceData?.Owner_Type ?? string.Empty;
                    var failedInvoiceId = invoiceData?.InvoiceID ?? invoiceData?.ROWID.ToString() ?? string.Empty;

                    _logger.LogError(rowEx,
                        "INVOICE ROW FAILED — SKIPPING THIS ROW ONLY => InvoiceID={InvoiceID} | Owner_Type={OwnerType} | ROWID={ROWID}",
                        failedInvoiceId, failedOwnerType, invoiceData?.ROWID);
                    Console.WriteLine($"Invoice row failed, skipping only this row. InvoiceID={failedInvoiceId}, Owner_Type={failedOwnerType}, Error={rowEx.Message}");

                    try
                    {
                        if (invoiceData != null)
                        {
                            await _invoiceRepository.UpdateRowAsync(new Invoice
                            {
                                ROWID = invoiceData.ROWID,
                                Books_Status = Constants.BooksStatusFailed,
                                Response = JsonHelper.Serialize(new { status = "FAILED", error = rowEx.Message, Owner_Type = failedOwnerType })
                            }, ct);
                        }
                    }
                    catch (Exception updateEx)
                    {
                        // Null-safe: even if the status update itself fails, do not
                        // let that take down the batch either.
                        _logger.LogError(updateEx, "FAILED TO WRITE FAILURE STATUS => InvoiceID={InvoiceID}", failedInvoiceId);
                    }

                    summary.SkippedInvoices.Add(new { InvoiceID = failedInvoiceId, OwnerType = failedOwnerType, reason = rowEx.Message });
                    // No return / throw / break here — the foreach simply moves on
                    // to the next invoice row.
                }
            }

            // =====================
            // FINAL SUMMARY
            // =====================
            summary.Status = "success";
            summary.TotalCreated = summary.CreatedInvoices.Count;
            summary.TotalUpdated = summary.UpdatedInvoices.Count;
            summary.TotalSkipped = summary.SkippedInvoices.Count;

            //_logger.LogInformation("SYNC SUMMARY => {Summary}", JsonHelper.Serialize(summary));

            return summary;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "BooksInvoiceSyncService failed");
            summary.Status = "error";
            Console.WriteLine($"BooksInvoiceSyncService failed: {error}");
            return summary;
        }
    }
}