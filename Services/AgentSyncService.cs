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
    public class PageProcessResult
    {
        public int RowsScanned { get; set; }
        public int AgentsFound { get; set; }
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
    /// One call to ProcessPageAsync == one execution of the original Job
    /// Function's Steps 3 through 6b: iterate the page, and for every agent
    /// extract + validate + build the row, run the duplicate lookup, and
    /// immediately insert or update that single Customer row before moving on
    /// to the next agent, then perform the bulk GST_Master insert. Customer
    /// writes are never batched or bulk-inserted - each agent is checked and
    /// written (insert or update) one at a time.
    /// </summary>
    public class AgentSyncService : IAgentSyncService
    {
        private readonly IAgentCorporateCustomerRepository _customerRepository;
        private readonly IGSTMasterRepository _gstMasterRepository;
        private readonly IDuplicateCheckService _duplicateCheckService;
        private readonly IThirdPartyRepository _thirdPartyRepository;
        private readonly ILogger<AgentSyncService> _logger;
        private readonly ExecutionTimer _timer;

        public AgentSyncService(
            IAgentCorporateCustomerRepository customerRepository,
            IGSTMasterRepository gstMasterRepository,
            IDuplicateCheckService duplicateCheckService,
            IThirdPartyRepository thirdPartyRepository,
            ILogger<AgentSyncService> logger,
            ExecutionTimer timer)
        {
            _customerRepository = customerRepository;
            _gstMasterRepository = gstMasterRepository;
            _duplicateCheckService = duplicateCheckService;
            _thirdPartyRepository = thirdPartyRepository;
            _logger = logger;
            _timer = timer;
        }

        public async Task<PageProcessResult> ProcessPageAsync(
            List<ThirdPartyDataRecord> pageRows,
            Dictionary<string, string> placeOfSupplyMap)
        {
            var result = new PageProcessResult();

            var gstInsertRows = new List<GSTMasterRecord>();

            var consumedThisPage = 0;

            for (; consumedThisPage < pageRows.Count; consumedThisPage++)
            {
                // Mid-page safety net only - NOT a page loop. If it trips, we
                // stop mid-page and resume exactly here via the caller's offset math.
                if (_timer.IsRuntimeExceeded())
                {
                    _logger.LogInformation("Runtime limit reached mid-page — stopping this execution");
                    break;
                }

                var row = pageRows[consumedThisPage];
                result.RowsScanned++;
                var thirdPartyROWID = row.ROWID ?? "";

                _logger.LogInformation("ThirdPartyData ROWID=" + thirdPartyROWID);

                try
                {
                    if (string.IsNullOrWhiteSpace(row.agent) || row.agent == "null")
                    {
                        result.Skipped++;
                        _logger.LogInformation("Skipped - No agent payload on row ROWID=" + thirdPartyROWID);
                        await SafeUpdateAgentStatusAsync(thirdPartyROWID, "Processed");
                        continue;
                    }

                    JObject parsed;

                    try
                    {
                        parsed = JsonHelper1.ParseJObject(row.agent);
                    }
                    catch (Exception parseErr)
                    {
                        RecordFailure(result, new FailedRecord
                        {
                            ThirdPartyROWID = thirdPartyROWID,
                            ROWID = thirdPartyROWID,
                            Stage = "JSON Parse",
                            Error = parseErr.ToString(),
                            Stack = parseErr.StackTrace ?? "",
                            SourceThirdPartyJSON = row.agent
                        });
                        result.Failed++;
                        _logger.LogInformation("Failure - JSON Parse ROWID=" + thirdPartyROWID);
                        await SafeUpdateAgentStatusAsync(thirdPartyROWID, "Failed");
                        continue;
                    }

                    var extracted = AgentExtractionService.ExtractAgents(parsed);

                    if (extracted.Agents.Count == 0)
                    {
                        result.Skipped++;
                        _logger.LogInformation("Skipped - No agents found in parsed payload ROWID=" + thirdPartyROWID);
                        await SafeUpdateAgentStatusAsync(thirdPartyROWID, "Processed");
                        continue;
                    }

                    foreach (var agentData in extracted.Agents)
                    {
                        result.AgentsFound++;

                        var customerId = (agentData.Id ?? "").Trim();

                        var agentName = !string.IsNullOrEmpty(agentData.Organization)
                            ? agentData.Organization
                            : ((agentData.FName ?? "") + " " + (agentData.LName ?? "")).Trim();

                        var hotelId = !string.IsNullOrEmpty(agentData.HotelId) ? agentData.HotelId : extracted.HotelId;

                        _logger.LogInformation("Current Agent ID=" + customerId + " Agent Name=" + agentName);

                        try
                        {
                            // ── Validation ────────────────────────────────
                            if (!ValidationService.IsCustomerIdValid(customerId))
                            {
                                RecordFailure(result, new FailedRecord
                                {
                                    ThirdPartyROWID = thirdPartyROWID,
                                    ROWID = thirdPartyROWID,
                                    HotelID = hotelId,
                                    Agent_Name = agentName,
                                    Stage = "Validation",
                                    Error = "empty/missing id field",
                                    SourceAgentJSON = JsonConvert.SerializeObject(agentData),
                                    SourceThirdPartyJSON = parsed.ToString()
                                });
                                result.Failed++;
                                _logger.LogInformation("Skipped - Validation failed (missing CustomerID) ROWID=" + thirdPartyROWID);
                                continue;
                            }

                            // ── GST extraction (Customer.GST_NO track - untouched) ──
                            var activeGstin = GSTService.SelectActiveGstin(agentData.GstinDetails);
                            var gstNumber = activeGstin?.Gstin ?? "";

                            // ── GST_MASTER collection (additive track) ──────
                            var gstCandidates = GSTService.BuildGstMasterCandidates(
                                agentData.GstinDetails, customerId, placeOfSupplyMap);
                            gstInsertRows.AddRange(gstCandidates);

                            // ── Safe address extraction ─────────────────────
                            var home = agentData.Addresses?.Home ?? new AddressInfo();
                            var work = agentData.Addresses?.Work ?? new AddressInfo();

                            var rowData = new CustomerRecord
                            {
                                hotelID = hotelId,
                                CustomerID = customerId,
                                First_Name = agentData.FName ?? "",
                                Code = agentData.Code ?? "",
                                Last_Name = agentData.LName ?? "",
                                Email = agentData.Email ?? "",
                                Company_Name = agentData.Organization ?? "",
                                Customer_Sub_Type = "Agent",
                                Mobile = JsonHelper1.ParseIntOrZero(agentData.MobileNoRaw),
                                Phone = JsonHelper1.ParseIntOrZero(agentData.PhoneNoRaw),

                                GST_Treatment = "business_gst",
                                GST_NO = gstNumber,

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

                            // ── CUSTOMER LOOKUP: duplicate check before every write ──
                            var existing = await _duplicateCheckService.FindExistingAgentAsync(customerId);
                            _logger.LogInformation("CustomerID=" + customerId);

                            if (existing != null)
                            {
                                // ---- UPDATE IMMEDIATELY ----
                                rowData.ROWID = existing.ROWID;
                                rowData.Response = JsonConvert.SerializeObject(new { status = "UPDATED", message = "Agent updated successfully" });

                                try
                                {
                                    await _customerRepository.UpdateCustomerAsync(rowData);
                                    result.Updated++;

                                    _logger.LogInformation("Update - CustomerID=" + customerId + " ROWID=" + existing.ROWID);
                                }
                                catch (Exception updateErr)
                                {
                                    RecordFailure(result, new FailedRecord
                                    {
                                        CustomerID = customerId,
                                        ThirdPartyROWID = thirdPartyROWID,
                                        ROWID = existing.ROWID,
                                        HotelID = hotelId,
                                        Agent_Name = agentName,
                                        Stage = "Update",
                                        Error = updateErr.ToString(),
                                        Stack = updateErr.StackTrace ?? "",
                                        SourceThirdPartyJSON = parsed.ToString(),
                                        CustomerPayload = JsonConvert.SerializeObject(rowData)
                                    });
                                    result.Failed++;
                                    _logger.LogInformation("Failure - Update CustomerID=" + customerId);
                                }
                            }
                            else
                            {
                                // ---- INSERT IMMEDIATELY ----
                                rowData.Response = JsonConvert.SerializeObject(new { status = "CREATED", message = "Agent inserted successfully" });

                                try
                                {
                                    await _customerRepository.InsertCustomerAsync(rowData);
                                    result.Inserted++;

                                    _logger.LogInformation("Insert - CustomerID=" + customerId);
                                }
                                catch (Exception insertErr)
                                {
                                    RecordFailure(result, new FailedRecord
                                    {
                                        CustomerID = customerId,
                                        ThirdPartyROWID = thirdPartyROWID,
                                        HotelID = hotelId,
                                        Agent_Name = agentName,
                                        Stage = "Insert",
                                        Error = insertErr.ToString(),
                                        Stack = insertErr.StackTrace ?? "",
                                        SourceThirdPartyJSON = parsed.ToString(),
                                        CustomerPayload = JsonConvert.SerializeObject(rowData)
                                    });
                                    result.Failed++;
                                    _logger.LogInformation("Failure - Insert CustomerID=" + customerId);
                                }
                            }
                        }
                        catch (Exception agentErr)
                        {
                            RecordFailure(result, new FailedRecord
                            {
                                CustomerID = customerId,
                                ThirdPartyROWID = thirdPartyROWID,
                                ROWID = thirdPartyROWID,
                                HotelID = hotelId,
                                Agent_Name = agentName,
                                Stage = "Build",
                                Error = agentErr.ToString(),
                                Stack = agentErr.StackTrace ?? "",
                                SourceAgentJSON = JsonConvert.SerializeObject(agentData),
                                SourceThirdPartyJSON = parsed.ToString()
                            });
                            result.Failed++;
                            _logger.LogInformation("Failure - Build CustomerID=" + customerId);
                        }
                    } // end per-agent loop

                    await SafeUpdateAgentStatusAsync(thirdPartyROWID, "Processed");
                }
                catch (Exception rowErr)
                {
                    RecordFailure(result, new FailedRecord
                    {
                        ThirdPartyROWID = thirdPartyROWID,
                        ROWID = thirdPartyROWID,
                        Stage = "Row Parse",
                        Error = rowErr.ToString(),
                        Stack = rowErr.StackTrace ?? ""
                    });
                    result.Failed++;
                    _logger.LogInformation("Failure - Row Parse ROWID=" + thirdPartyROWID);
                    await SafeUpdateAgentStatusAsync(thirdPartyROWID, "Failed");
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

            result.StoppedEarly = consumedThisPage < pageRows.Count;
            result.ConsumedRows = consumedThisPage;

            return result;
        }

        /// <summary>Exact port of recordFailure().</summary>
        private void RecordFailure(PageProcessResult result, FailedRecord record)
        {
            result.FailedRecords.Add(record);
            _logger.LogInformation("FAILED - CustomerID=" + record.CustomerID + " Stage=" + record.Stage + " Error=" + record.Error);
        }

        /// <summary>
        /// Writes Agent_Status back to ThirdPartyData for this row. Non-fatal by
        /// design - if the status write itself fails, the row simply gets
        /// re-attempted on the next run rather than failing the whole page.
        /// </summary>
        private async Task SafeUpdateAgentStatusAsync(string thirdPartyROWID, string status)
        {
            try
            {
                await _thirdPartyRepository.UpdateAgentStatusAsync(thirdPartyROWID, status);
                _logger.LogInformation("ThirdPartyData ROWID=" + thirdPartyROWID + " Agent_Status=" + status);
            }
            catch (Exception statusErr)
            {
                //_logger.LogInformation("--------------------------------------------------");
                //_logger.LogInformation("AGENT_STATUS UPDATE FAILED (non-fatal — job continues)");
                //_logger.LogInformation("--------------------------------------------------");
                //_logger.LogInformation("ThirdPartyData ROWID=" + thirdPartyROWID + " Status=" + status);
                _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(statusErr));
            }
        }
    }
}