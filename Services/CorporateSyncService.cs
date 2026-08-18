using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Services
{
    public class PageProcessResultsCorporate
    {
        public int RowsScanned { get; set; }
        public int CorporateFound { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<FailedRecord> FailedRecords { get; set; } = new List<FailedRecord>();

        // True if the mid-page runtime guard tripped before the whole page was consumed.
        public bool StoppedEarly { get; set; }

        // How many rows of the page were actually consumed (== page size unless StoppedEarly).
        public int ConsumedRows { get; set; }
    }

    /// <summary>
    /// One call to ProcessPageAsync == one execution of the corporate track's
    /// per-page processing: iterate the page, and for every corporate
    /// extract + validate + build the row, run the duplicate lookup, and
    /// immediately insert or update that single Customer row before moving on
    /// to the next corporate, then perform the bulk GST_Master insert.
    /// </summary>
    public class CorporateSyncService : ICorporateSyncService
    {
        private readonly IAgentCorporateCustomerRepository _customerRepository;
        private readonly IGSTMasterRepository _gstMasterRepository;
        private readonly IDuplicateCheckService _duplicateCheckService;
        private readonly IThirdPartyRepository _thirdPartyRepository;
        private readonly ILogger<CorporateSyncService> _logger;
        private readonly ExecutionTimer _timer;

        public CorporateSyncService(
            IAgentCorporateCustomerRepository customerRepository,
            IGSTMasterRepository gstMasterRepository,
            IDuplicateCheckService duplicateCheckService,
            IThirdPartyRepository thirdPartyRepository,
            ILogger<CorporateSyncService> logger,
            ExecutionTimer timer)
        {
            _customerRepository = customerRepository;
            _gstMasterRepository = gstMasterRepository;
            _duplicateCheckService = duplicateCheckService;
            _thirdPartyRepository = thirdPartyRepository;
            _logger = logger;
            _timer = timer;
        }

        public async Task<PageProcessResultsCorporate> ProcessPageAsync(
            List<ThirdPartyDataRecord> pageRows,
            Dictionary<string, string> placeOfSupplyMap)
        {
            var result1 = new PageProcessResultsCorporate();

            var gstInsertRows = new List<GSTMasterRecord>();

            var consumedThisPage = 0;

            for (; consumedThisPage < pageRows.Count; consumedThisPage++)
            {
                // Mid-page safety net only - NOT a page loop. If it trips, we
                // stop mid-page and resume exactly here via the caller's offset math.

                //if (_timer.IsRuntimeExceeded())
                //{
                //    _logger.LogInformation("Runtime limit reached mid-page — stopping this execution");
                //    break;
                //}

                var row = pageRows[consumedThisPage];
                result1.RowsScanned++;
                var thirdPartyROWID = row.ROWID ?? "";

                _logger.LogInformation("ThirdPartyData coporate ROWID=" + thirdPartyROWID);

                try
                {
                    if (string.IsNullOrWhiteSpace(row.corporates) || row.corporates == "null")
                    {
                        result1.Skipped++;
                        _logger.LogInformation("Skipped - No corporate payload on row ROWID=" + thirdPartyROWID);
                    }

                    JObject parsed;

                    try
                    {
                        parsed = JsonHelper1.ParseJObject(row.corporates);
                    }
                    catch (Exception parseErr)
                    {
                        RecordFailure(result1, new FailedRecord
                        {
                            ThirdPartyROWID = thirdPartyROWID,
                            ROWID = thirdPartyROWID,
                            Stage = "JSON Parse",
                            Error = parseErr.ToString(),
                            Stack = parseErr.StackTrace ?? "",
                            SourceThirdPartyJSON = row.corporates
                        });
                        result1.Failed++;
                        _logger.LogInformation("Failure - coporate JSON Parse ROWID=" + thirdPartyROWID);
                        await SafeUpdateCorporateStatusAsync(thirdPartyROWID, "Failed");
                        continue;
                    }

                    var extracted = CorporatesExtractionService.ExtractCorporates(parsed);

                    if (extracted.Corporates.Count == 0)
                    {
                        result1.Skipped++;
                        _logger.LogInformation("Skipped - No corporates found in parsed payload ROWID=" + thirdPartyROWID);
                        await SafeUpdateCorporateStatusAsync(thirdPartyROWID, "Processed");
                        continue;
                    }

                    foreach (var corporateData in extracted.Corporates)
                    {
                        result1.CorporateFound++;

                        var customerId = (corporateData.Id ?? "").Trim();

                        var corporateName = !string.IsNullOrEmpty(corporateData.Organization)
                            ? corporateData.Organization
                            : ((corporateData.FName ?? "") + " " + (corporateData.LName ?? "")).Trim();

                        var hotelId = !string.IsNullOrEmpty(corporateData.HotelId) ? corporateData.HotelId : extracted.HotelId;

                        _logger.LogInformation("Current Corporate ID=" + customerId + " Corporate Name=" + corporateName);

                        try
                        {
                            // ── Validation ────────────────────────────────
                            if (!ValidationService.IsCustomerIdValid(customerId))
                            {
                                RecordFailure(result1, new FailedRecord
                                {
                                    ThirdPartyROWID = thirdPartyROWID,
                                    ROWID = thirdPartyROWID,
                                    HotelID = hotelId,
                                    Agent_Name = corporateName,
                                    Stage = "Validation",
                                    Error = "empty/missing id field",
                                    SourceCorporateJSON = JsonConvert.SerializeObject(corporateData),
                                    SourceThirdPartyJSON = parsed.ToString()
                                });
                                result1.Failed++;
                                _logger.LogInformation("Skipped - Validation failed (missing CustomerID) ROWID=" + thirdPartyROWID);
                                continue;
                            }

                            // ── GST extraction (Customer.GST_NO track - untouched) ──
                            var activeGstin = GSTService.SelectActiveGstin(corporateData.GstinDetails);
                            var gstNumber = activeGstin?.Gstin ?? "";

                            // ── GST_MASTER collection (additive track) ──────
                            var gstCandidates = GSTService.BuildGstMasterCandidates(
                                corporateData.GstinDetails, customerId, placeOfSupplyMap);
                            gstInsertRows.AddRange(gstCandidates);

                            var gstStateCode = gstNumber.Length >= 2 ? gstNumber.Substring(0, 2) : gstNumber;
                            var mappedPlaceOfSupply = "";
                            if (placeOfSupplyMap != null && placeOfSupplyMap.TryGetValue(gstStateCode, out var pos))
                            {
                                mappedPlaceOfSupply = pos ?? "";
                            }

                            // ── Safe address extraction ─────────────────────
                            var home = corporateData.Addresses?.Home ?? new AddressInfo();
                            var work = corporateData.Addresses?.Work ?? new AddressInfo();

                            var rowData = new CustomerRecord
                            {
                                hotelID = hotelId,
                                CustomerID = customerId,
                                First_Name = corporateData.FName ?? "",
                                Code = corporateData.Code ?? "",
                                Last_Name = corporateData.LName ?? "",
                                Email = corporateData.Email ?? "",
                                Company_Name = corporateData.Organization ?? "",
                                Customer_Sub_Type = "corporates",
                                Mobile = JsonHelper1.ParseIntOrZero(corporateData.MobileNoRaw),
                                Phone = JsonHelper1.ParseIntOrZero(corporateData.PhoneNoRaw),

                                GST_Treatment = "business_gst",
                                GST_NO = gstNumber,
                                Place_Of_Supply = mappedPlaceOfSupply,

                                Billing_Country = home.Country ?? "",
                                Billing_State = home.State ?? "",
                                Billing_City = home.City ?? "",
                                Billing_Pincode = JsonHelper1.ParseIntOrZero(home.Zip),

                                Shipping_Country = work.Country ?? "",
                                Shipping_State = work.State ?? "",
                                Shipping_City = work.City ?? "",
                                Shipping_Pincode = JsonHelper1.ParseIntOrZero(work.Zip),

                                Status = "processed"
                            };
                            _logger.LogInformation("$Rowdata:" + JsonConvert.SerializeObject(rowData));

                            // ── CUSTOMER LOOKUP: duplicate check before every write ──
                            var existing = await _duplicateCheckService.FindExistingCorporateAsync(customerId);
                            _logger.LogInformation("coporate CustomerID=" + customerId);

                            if (existing != null)
                            {
                                // ---- UPDATE IMMEDIATELY ----
                                rowData.ROWID = existing.ROWID;
                                rowData.Response = JsonConvert.SerializeObject(new { status = "UPDATED", message = "Corporate updated successfully" });

                                try
                                {
                                    await _customerRepository.UpdateCustomerAsync(rowData);
                                    result1.Updated++;

                                    _logger.LogInformation("Update - coporateCustomerID=" + customerId + " ROWID=" + existing.ROWID);
                                }
                                catch (Exception updateErr)
                                {
                                    RecordFailure(result1, new FailedRecord
                                    {
                                        CustomerID = customerId,
                                        ThirdPartyROWID = thirdPartyROWID,
                                        ROWID = existing.ROWID,
                                        HotelID = hotelId,
                                        Agent_Name = corporateName,
                                        Stage = "Update",
                                        Error = updateErr.ToString(),
                                        Stack = updateErr.StackTrace ?? "",
                                        SourceThirdPartyJSON = parsed.ToString(),
                                        CustomerPayload = JsonConvert.SerializeObject(rowData)
                                    });
                                    result1.Failed++;
                                    _logger.LogInformation("Failure - Update CustomerID=" + customerId);
                                }
                            }
                            else
                            {
                                // ---- INSERT IMMEDIATELY ----
                                rowData.Response = JsonConvert.SerializeObject(new { status = "CREATED", message = "Corporate inserted successfully" });

                                try
                                {
                                    await _customerRepository.InsertCustomerAsync(rowData);
                                    result1.Inserted++;

                                    _logger.LogInformation("coporate Insert - CustomerID=" + customerId);
                                }
                                catch (Exception insertErr)
                                {
                                    RecordFailure(result1, new FailedRecord
                                    {
                                        CustomerID = customerId,
                                        ThirdPartyROWID = thirdPartyROWID,
                                        HotelID = hotelId,
                                        Agent_Name = corporateName,
                                        Stage = "Insert",
                                        Error = insertErr.ToString(),
                                        Stack = insertErr.StackTrace ?? "",
                                        SourceThirdPartyJSON = parsed.ToString(),
                                        CustomerPayload = JsonConvert.SerializeObject(rowData)
                                    });
                                    result1.Failed++;
                                    _logger.LogInformation("Failure - Insert CustomerID=" + customerId);
                                }
                            }
                        }
                        catch (Exception agentErr)
                        {
                            RecordFailure(result1, new FailedRecord
                            {
                                CustomerID = customerId,
                                ThirdPartyROWID = thirdPartyROWID,
                                ROWID = thirdPartyROWID,
                                HotelID = hotelId,
                                Agent_Name = corporateName,
                                Stage = "Build",
                                Error = agentErr.ToString(),
                                Stack = agentErr.StackTrace ?? "",
                                SourceCorporateJSON = JsonConvert.SerializeObject(corporateData),
                                SourceThirdPartyJSON = parsed.ToString()
                            });
                            result1.Failed++;
                            _logger.LogInformation("Failure - Build CustomerID=" + customerId);
                        }
                    } // end per-corporate loop

                    await SafeUpdateCorporateStatusAsync(thirdPartyROWID, "Processed");
                }
                catch (Exception rowErr)
                {
                    RecordFailure(result1, new FailedRecord
                    {
                        ThirdPartyROWID = thirdPartyROWID,
                        ROWID = thirdPartyROWID,
                        Stage = "Row Parse",
                        Error = rowErr.ToString(),
                        Stack = rowErr.StackTrace ?? ""
                    });
                    result1.Failed++;
                    _logger.LogInformation("Failure - Row Parse ROWID=" + thirdPartyROWID);
                    await SafeUpdateCorporateStatusAsync(thirdPartyROWID, "Failed");
                }
            } // end per-row loop

            // =====================================================
            // STEP 6b: GST_MASTER BULK INSERT - after Customer processing,
            // never touches Customer, never fails the page/job.
            // =====================================================
            if (gstInsertRows.Count > 0)
            {
                try
                {
                    // De-duplicate CustomerID + GST_No combinations collected within this page.
                    var uniqueMap = new Dictionary<string, GSTMasterRecord>();

                    foreach (var g in gstInsertRows)
                    {
                        var key = g.CustomerID + "||" + g.GST_No;

                        if (!uniqueMap.ContainsKey(key))
                        {
                            uniqueMap[key] = g;
                        }
                    }

                    var rowsToInsert = new List<GSTMasterRecord>();

                    foreach (var g in uniqueMap.Values)
                    {
                        try
                        {
                            var exists = await _gstMasterRepository.ExistsAsync(g.CustomerID, g.GST_No);

                            if (exists)
                            {
                                _logger.LogInformation("Skipped - GST_Master duplicate CustomerID=" + g.CustomerID + " GST_No=" + g.GST_No);
                            }
                            else
                            {
                                rowsToInsert.Add(g);
                            }
                        }
                        catch (Exception gstCheckErr)
                        {
                            _logger.LogInformation("--------------------------------------------------");
                            _logger.LogInformation("GST_MASTER DUPLICATE CHECK FAILED (non-fatal — job continues)");
                            _logger.LogInformation("--------------------------------------------------");
                            _logger.LogInformation("CustomerID=" + g.CustomerID + " GST_No=" + g.GST_No);
                            _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(gstCheckErr));
                        }
                    }

                    if (rowsToInsert.Count > 0)
                    {
                        try
                        {
                            await _gstMasterRepository.BulkInsertAsync(rowsToInsert);

                            foreach (var g in rowsToInsert)
                            {
                                _logger.LogInformation("GST_Master Insert - CustomerID=" + g.CustomerID + " GST_No=" + g.GST_No);
                            }

                            _logger.LogInformation("GST_Master Bulk Insert - rows=" + rowsToInsert.Count);
                        }
                        catch (Exception gstInsertErr)
                        {
                            _logger.LogInformation("--------------------------------------------------");
                            _logger.LogInformation("GST_MASTER BULK INSERT FAILED (non-fatal — job continues)");
                            _logger.LogInformation("--------------------------------------------------");
                            _logger.LogInformation("Rows Attempted=" + rowsToInsert.Count);
                            _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(gstInsertErr));
                        }
                    }
                }
                catch (Exception gstOuterErr)
                {
                    _logger.LogInformation("--------------------------------------------------");
                    _logger.LogInformation("GST_MASTER PROCESSING FAILED (non-fatal — job continues)");
                    _logger.LogInformation("--------------------------------------------------");
                    _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(gstOuterErr));
                }
            }

            result1.StoppedEarly = consumedThisPage < pageRows.Count;
            result1.ConsumedRows = consumedThisPage;

            return result1;
        }

        private void RecordFailure(PageProcessResultsCorporate result, FailedRecord record)
        {
            result.FailedRecords.Add(record);
            _logger.LogInformation("Failed Record: " + JsonConvert.SerializeObject(record));
            _logger.LogInformation("FAILED - CustomerID=" + record.CustomerID + " Stage=" + record.Stage + " Error=" + record.Error);
        }

        /// <summary>
        /// Writes Corporate_Status back to ThirdPartyData for this row. Non-fatal
        /// by design - if the status write itself fails, the row simply gets
        /// re-attempted on the next run rather than failing the whole page.
        /// </summary>
        private async Task SafeUpdateCorporateStatusAsync(string thirdPartyROWID, string status)
        {
            try
            {
                await _thirdPartyRepository.UpdateCorporateStatusAsync(thirdPartyROWID, status);
                _logger.LogInformation("ThirdPartyData ROWID=" + thirdPartyROWID + " Corporate_Status=" + status);
            }
            catch (Exception statusErr)
            {
                //_logger.LogInformation("--------------------------------------------------");
                //_logger.LogInformation("CORPORATE_STATUS UPDATE FAILED (non-fatal — job continues)");
                //_logger.LogInformation("--------------------------------------------------");
                //_logger.LogInformation("ThirdPartyData ROWID=" + thirdPartyROWID + " Status=" + status);
                _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(statusErr));
            }
        }
    }
}