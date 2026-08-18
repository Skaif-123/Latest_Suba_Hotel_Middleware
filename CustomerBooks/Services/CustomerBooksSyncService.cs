using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentSyncConsole.CustomerBooks.Configuration;
using AgentSyncConsole.CustomerBooks.Interfaces;
using AgentSyncConsole.CustomerBooks.Models;
using AgentIAccessTokenRepository = AgentSyncConsole.Interfaces.IAccessTokenRepository;

namespace AgentSyncConsole.CustomerBooks.Services
{
    /// <summary>
    /// Ports CustomerBooksSync.Api's CustomerBooksSyncService. Pagination is a
    /// single in-process loop, matching the console architecture used by the
    /// rest of this application.
    ///
    /// ACCESS-TOKEN NOTE: the original CustomerBooksSync.Api project shipped
    /// its own AccessTokenRepository whose GetLatestAccessTokenAsync() did
    /// not actually query the database — it returned a hardcoded token
    /// string, with the real "SELECT TOP 1 AccessToken FROM dbo.AccessTokens"
    /// query left commented out. AgentSyncConsole already has a fully
    /// functioning, tested IAccessTokenRepository reading the same
    /// application's ("Books") token from the same database, used by
    /// BooksInvoiceSyncService. Per the merge rule "if a shared/equivalent
    /// piece of working infrastructure already exists, reuse it rather than
    /// duplicating a broken/stub copy", this module calls that existing
    /// repository instead of re-introducing the CustomerBooksSync.Api stub.
    /// </summary>
    public class CustomerBooksSyncService : ICustomerBooksSyncService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IGstMasterRepository _gstMasterRepository;
        private readonly AgentIAccessTokenRepository _accessTokenRepository;
        private readonly IZohoBooksApiClient _booksApiClient;
        private readonly CustomerBooksSettings _options;
        private readonly ILogger<CustomerBooksSyncService> _logger;

        public CustomerBooksSyncService(
            ICustomerRepository customerRepository,
            IGstMasterRepository gstMasterRepository,
            AgentIAccessTokenRepository accessTokenRepository,
            IZohoBooksApiClient booksApiClient,
            IOptions<CustomerBooksSettings> options,
            ILogger<CustomerBooksSyncService> logger)
        {
            _customerRepository = customerRepository;
            _gstMasterRepository = gstMasterRepository;
            _accessTokenRepository = accessTokenRepository;
            _booksApiClient = booksApiClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<CustomerBooksSyncSummary> RunFullSyncAsync(CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var summary = new CustomerBooksSyncSummary();

            var tokenRecord = await _accessTokenRepository.GetLatestAsync("Books", ct);
            var accessToken = tokenRecord?.accessToken?.Trim();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                stopwatch.Stop();
                summary.Status = "failed";
                summary.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                _logger.LogError("Customer Books Sync aborted — no Books access token available.");
                return summary;
            }

            var offset = 0;

            while (true)
            {
                var page = await _customerRepository.GetPageAsync(offset, _options.PageSize, ct);

                if (page.Count == 0)
                {
                    break;
                }

                var updateResults = new List<CustomerSyncResult>();

                foreach (var customer in page)
                {
                    summary.TotalScanned++;

                    IReadOnlyList<GstMasterRecord> gstRows;
                    try
                    {
                        gstRows = await _gstMasterRepository.GetByCustomerIdAsync(customer.CustomerID, ct);
                    }
                    catch (Exception)
                    {
                        gstRows = Array.Empty<GstMasterRecord>();
                    }

                    var result = await SyncCustomerAsync(customer, accessToken, gstRows, ct);

                    if (result.Success)
                    {
                        if (result.WasCreate) summary.Created++; else summary.Updated++;
                    }
                    else
                    {
                        summary.Failed++;
                        // Never stop the run — continue with the next customer.
                        // Detailed per-row failure reasons are not written to
                        // console; they are still captured in result.Response
                        // for whatever wrote it back to dbo.Customer.
                    }

                    updateResults.Add(result);
                }

                await _customerRepository.UpdateResultsAsync(updateResults, ct);

                if (page.Count < _options.PageSize)
                {
                    break; // last page — fewer rows than PageSize means the table is exhausted.
                }

                offset += _options.PageSize;
            }

            stopwatch.Stop();
            summary.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            summary.Status = "success";

            return summary;
        }

        /// <summary>
        /// booksID present -> PUT, blank -> POST. Success is exactly: Books
        /// response has code === 0 AND a returned contact_id.
        /// </summary>
        private async Task<CustomerSyncResult> SyncCustomerAsync(
            CustomerRecord customer, string accessToken, IReadOnlyList<GstMasterRecord> gstRows, CancellationToken ct)
        {
            var existingBooksId = string.IsNullOrWhiteSpace(customer.booksID) ? null : customer.booksID.Trim();
            var wasCreate = existingBooksId is null;

            var defaultGst = gstRows.FirstOrDefault(g => g.isDefault);

            var payload = PayloadBuilder.BuildContactPayload(customer, defaultGst, _options.CodeCustomFieldId, _options.HotelIDCustomField);

            try
            {
                var response = await _booksApiClient.CreateOrUpdateContactAsync(accessToken, existingBooksId, payload, ct);

                JsonNode? parsed = null;
                try
                {
                    parsed = JsonNode.Parse(response.RawBody);
                }
                catch (Exception)
                {
                    // Best-effort parse only — fall through with parsed == null.
                }

                // DEFENSIVE FIX: previously this called
                // parsed?["contact"]?["contact_id"]?.GetValue<string>()
                // directly. GetValue<string>() throws if the node isn't
                // already a JSON string (e.g. a bare number, which
                // System.Text.Json will happily parse even though Zoho's
                // documented contract is a string). That exception was
                // caught by the surrounding try/catch and reported as a
                // sync FAILURE even though Zoho Books had already created
                // the Contact — i.e. it could produce exactly the "created
                // in Books but booksID never stored" symptom by itself.
                // ExtractStringValue below never throws: it accepts a
                // string, coerces a number, and returns null for anything
                // else so the existing failure path still applies cleanly.
                var contactId = ExtractStringValue(parsed?["contact"]?["contact_id"]);
                var code = parsed is not null && parsed.AsObject().TryGetPropertyValue("code", out var codeNode)
                    ? codeNode?.GetValue<int?>()
                    : null;

                var success = code == 0 && !string.IsNullOrEmpty(contactId);

                if (!success)
                {
                    return new CustomerSyncResult
                    {
                        CustomerId = customer.CustomerID,
                        RowId = customer.ROWID,
                        Success = false,
                        BooksId = existingBooksId ?? string.Empty,
                        Status = "Failed",
                        ErrorMessage = ExtractStringValue(parsed?["message"]) ?? "Unknown Books API error",
                        Response = response.RawBody
                    };
                }

                // GUARANTEE: this is the only place that calls POST/PUT
                // /books/v3/contacts. Everything below only calls the
                // /taxinfo sub-resource against this already-created Contact.
                try
                {
                    await SyncCustomerGstRegistrationsAsync(contactId!, accessToken, gstRows, defaultGst, ct);
                }
                catch (Exception)
                {
                    // Never turns the Contact sync itself into a failure.
                }

                return new CustomerSyncResult
                {
                    CustomerId = customer.CustomerID,
                    RowId = customer.ROWID,
                    Success = true,
                    BooksId = contactId!,
                    Status = "Synced",
                    Response = response.RawBody,
                    WasCreate = wasCreate
                };
            }
            catch (Exception ex)
            {
                return new CustomerSyncResult
                {
                    CustomerId = customer.CustomerID,
                    RowId = customer.ROWID,
                    Success = false,
                    BooksId = existingBooksId ?? string.Empty,
                    Status = "Failed",
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Reads a JSON node as a string without ever throwing, regardless
        /// of its actual token type. Handles the documented case (JSON
        /// string) and defensively coerces a bare JSON number, since
        /// System.Text.Json.Nodes will parse either without complaint but
        /// JsonNode.GetValue&lt;string&gt;() throws on a mismatch. Returns null
        /// for missing/empty/other node types so existing null-handling at
        /// each call site (treated as "no id returned") is unchanged.
        /// </summary>
        private static string? ExtractStringValue(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return null;
            }

            if (value.TryGetValue<string>(out var s))
            {
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }

            if (value.TryGetValue<long>(out var l))
            {
                return l.ToString();
            }

            if (value.TryGetValue<double>(out var d))
            {
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        /// <summary>
        /// Runs only after a Contact ID is known, skips the default GST row
        /// (already on the Contact payload), and PUTs/POSTs every other row
        /// as a Tax Registration, writing the returned tax_information_id
        /// back so re-runs PUT instead of duplicating.
        /// </summary>
        private async Task SyncCustomerGstRegistrationsAsync(
            string contactId, string accessToken, IReadOnlyList<GstMasterRecord> gstRows,
            GstMasterRecord? defaultGst, CancellationToken ct)
        {
            if (gstRows.Count <= 1)
            {
                return;
            }

            foreach (var gstRow in gstRows)
            {
                if (defaultGst is not null && gstRow.Id == defaultGst.Id)
                {
                    continue; // already applied to the Contact itself.
                }

                var taxPayload = PayloadBuilder.BuildTaxInfoPayload(gstRow);
                var existingTaxId = string.IsNullOrWhiteSpace(gstRow.BooksID) ? null : gstRow.BooksID!.Trim();

                try
                {
                    var response = await _booksApiClient.CreateOrUpdateTaxInfoAsync(accessToken, contactId, existingTaxId, taxPayload, ct);

                    JsonNode? parsed = null;
                    try
                    {
                        parsed = JsonNode.Parse(response.RawBody);
                    }
                    catch (Exception)
                    {
                        // Best-effort parse only.
                    }

                    var taxInformationId = ExtractStringValue(parsed?["tax_information"]?["tax_information_id"]) ?? existingTaxId;

                    if (!string.IsNullOrEmpty(taxInformationId))
                    {
                        try
                        {
                            await _gstMasterRepository.UpdateBooksIdAsync(gstRow.Id, taxInformationId, ct);
                        }
                        catch (Exception)
                        {
                            // Never stop — continue with the next GST row.
                        }
                    }
                }
                catch (Exception)
                {
                    // Never stop — continue with the next GST row.
                }
            }
        }
    }
}