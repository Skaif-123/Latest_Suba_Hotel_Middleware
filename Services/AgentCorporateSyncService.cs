using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using AgentSyncConsole.Utilites;

namespace AgentSyncConsole.Services
{
    /// <summary>
    /// Replaces the Catalyst Job Function's self-chaining behaviour
    /// (jobScheduling().JOB.submitJob() resubmitting the function with the
    /// next offset) with a single in-process while(hasMore) loop. Each loop
    /// iteration is functionally identical to one execution of the original
    /// Job Function: fetch one page, process it, save the offset, decide
    /// whether to continue - no recursive calls, no Catalyst Job APIs.
    ///
    /// This is a straight move of the old Program.Main body into a proper,
    /// DI-friendly orchestration service. No business behaviour changed -
    /// only where the code lives and how its dependencies are supplied.
    /// </summary>
    public class AgentCorporateSyncService : IAgentCorporateSyncService
    {
        private readonly IPlaceOfSupplyRepository _placeOfSupplyRepository;
        private readonly IThirdPartyRepository _thirdPartyRepository;
        private readonly IOffsetManager _offsetManager;
        private readonly IAgentSyncService _agentSyncService;
        private readonly ICorporateSyncService _corporateSyncService;
        private readonly ExecutionTimer _timer;
        private readonly ILogger<AgentCorporateSyncService> _logger;

        public AgentCorporateSyncService(
            IPlaceOfSupplyRepository placeOfSupplyRepository,
            IThirdPartyRepository thirdPartyRepository,
            IOffsetManager offsetManager,
            IAgentSyncService agentSyncService,
            ICorporateSyncService corporateSyncService,
            ExecutionTimer timer,
            ILogger<AgentCorporateSyncService> logger)
        {
            _placeOfSupplyRepository = placeOfSupplyRepository;
            _thirdPartyRepository = thirdPartyRepository;
            _offsetManager = offsetManager;
            _agentSyncService = agentSyncService;
            _corporateSyncService = corporateSyncService;
            _timer = timer;
            _logger = logger;
        }

        public async Task<SyncSummary> RunAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("JOB START");
            var startTime = DateTime.UtcNow;
            var totalRowsScanned = 0;
            var totalAgentsFound = 0;
            var totalCorporatesFound = 0;
            var totalInserted = 0;
            var totalUpdated = 0;
            var totalFailed = 0;
            var totalSkipped = 0;
            var allFailedRecords = new List<FailedRecord>();

            try
            {
                // ──================= Load Place Of Supply ONCE before processing begins ──────
                Dictionary<string, string> placeOfSupplyMap;
                try
                {
                    placeOfSupplyMap = await _placeOfSupplyRepository.LoadAllAsync();
                    _logger.LogInformation("Place_Of_Supply Map Loaded - entries=" + placeOfSupplyMap.Count);
                }
                catch (Exception posLoadErr)
                {
                    // Non-fatal - GST_Master rows just fall back to empty Place_Of_Supply.
                    placeOfSupplyMap = new Dictionary<string, string>();
                    _logger.LogInformation("--------------------------------------------------");
                    _logger.LogInformation("PLACE_OF_SUPPLY MAP LOAD FAILED (non-fatal — GST_Master Place_Of_Supply will default to empty)");
                    _logger.LogInformation("--------------------------------------------------");
                    _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(posLoadErr));
                }

                // ──======================= Load Offset ──────────────────────────────────────────────
                var offset = await _offsetManager.LoadOffsetAsync();
                _logger.LogInformation("Offset Source=SyncOffset table");
                _logger.LogInformation("Current Offset=" + offset);
                _logger.LogInformation("PAGE_SIZE=" + Constants.PAGE_SIZE);

                var hasMore = true;

                // ── Loop through pages until none remain ────────────────────
                while (hasMore)
                {
                    // Fresh runtime budget per page - matches the original
                    // per-execution START_TIME/MAX_RUNTIME mid-page safety net.
                    _timer.Reset();

                    List<ThirdPartyDataRecord> pageRows;
                    List<ThirdPartyDataRecord> pageRowsCorporate;

                    //──=====================================================================
                    try
                    {
                        pageRows = await _thirdPartyRepository.GetPageAsync(offset, Constants.PAGE_SIZE);
                        _logger.LogInformation("PageProcess Agent json: " + JsonConvert.SerializeObject(pageRows));
                    }
                    catch (Exception queryErr)
                    {
                        //_logger.LogInformation("--------------------------------------------------");
                        //_logger.LogInformation("THIRDPARTYDATA QUERY FAILED");
                        //_logger.LogInformation("--------------------------------------------------");
                        //_logger.LogInformation("Current Offset=" + offset);
                        _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(queryErr));
                        throw;
                    }

                    //──=====================================================================
                    try
                    {
                        pageRowsCorporate = await _thirdPartyRepository.GetPageAsyncCorporate(offset, Constants.PAGE_SIZE);
                        _logger.LogInformation("PageProcess corporate json: " + JsonConvert.SerializeObject(pageRowsCorporate));
                    }
                    catch (Exception queryErr)
                    {
                  
                        _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(queryErr));
                        throw;
                    }

                    //_logger.LogInformation("THIRDPARTYDATA FETCHED");

                    //──=====================================================================
                    if (pageRows == null || pageRows.Count == 0)
                    {
                        // No more rows left — reset offset and end the chain.
                        await _offsetManager.SaveOffsetAsync(0, offset);
                     
                        hasMore = false;
                        break;
                    }

                    //──=====================================================================
                    //corporate
                    if (pageRowsCorporate == null || pageRowsCorporate.Count == 0)
                    {
                        // No more rows left — reset offset and end the chain.
                        await _offsetManager.SaveOffsetAsync(0, offset);
                        _logger.LogInformation("corporate OFFSET SAVED");
                        _logger.LogInformation("corporate Next Offset=n/a (no more rows)");
                        _logger.LogInformation("corporate NO ROWS REMAINING — CHAIN ENDED (no next job submitted)");
                        hasMore = false;
                        break;
                    }

                    _logger.LogInformation("Page Started");
                    _logger.LogInformation("PAGE START - offset=" + offset + " rows=" + pageRows.Count);

                    var pageResult = await _agentSyncService.ProcessPageAsync(pageRows, placeOfSupplyMap);
                    var pageresult2 = await _corporateSyncService.ProcessPageAsync(pageRowsCorporate, placeOfSupplyMap);

                    //──=====================================================================
                    //corporate
                    _logger.LogInformation("pageresult2: " + JsonConvert.SerializeObject(pageresult2));
                    totalRowsScanned += pageresult2.RowsScanned;
                    totalCorporatesFound += pageresult2.CorporateFound;
                    totalInserted += pageresult2.Inserted;
                    totalUpdated += pageresult2.Updated;
                    totalFailed += pageresult2.Failed;
                    totalSkipped += pageresult2.Skipped;
                    allFailedRecords.AddRange(pageresult2.FailedRecords);

                    //──=====================================================================
                    //agent
                    totalRowsScanned += pageResult.RowsScanned;
                    totalAgentsFound += pageResult.AgentsFound;
                    totalInserted += pageResult.Inserted;
                    totalUpdated += pageResult.Updated;
                    totalFailed += pageResult.Failed;
                    totalSkipped += pageResult.Skipped;
                    allFailedRecords.AddRange(pageResult.FailedRecords);

                    //──=====================================================================
                    _logger.LogInformation("Page Completed");
                    _logger.LogInformation(
                        "PAGE END - scanned=" + pageResult.RowsScanned +
                        " agentsFound=" + pageResult.AgentsFound +
                        " inserted=" + pageResult.Inserted +
                        " updated=" + pageResult.Updated +
                        " failed=" + pageResult.Failed +
                        " skipped=" + pageResult.Skipped);

                    //──=====================================================================
                    //corporate
                    totalRowsScanned += pageresult2.RowsScanned;
                    totalCorporatesFound += pageresult2.CorporateFound;
                    totalInserted += pageresult2.Inserted;
                    totalUpdated += pageresult2.Updated;
                    totalFailed += pageresult2.Failed;
                    totalSkipped += pageresult2.Skipped;
                    allFailedRecords.AddRange(pageresult2.FailedRecords);
                    _logger.LogInformation("Page Completed");
                    _logger.LogInformation(
                        "PAGE END - scanned=" + pageresult2.RowsScanned +
                        " corporatesFound=" + pageresult2.CorporateFound +
                        " inserted=" + pageresult2.Inserted +
                        " updated=" + pageresult2.Updated +
                        " failed=" + pageresult2.Failed +
                        " skipped=" + pageresult2.Skipped);

                    // ── Determine whether more pages remain ──────────────────
                    var stoppedEarly = pageResult.StoppedEarly;
                    var pageHasMore = stoppedEarly || pageRows.Count == Constants.PAGE_SIZE;
                    var resumeOffset = stoppedEarly
                        ? offset + pageResult.ConsumedRows
                        : offset + Constants.PAGE_SIZE;

                    await _offsetManager.SaveOffsetAsync(pageHasMore ? resumeOffset : 0, offset);
                    _logger.LogInformation("OFFSET SAVED");
                    _logger.LogInformation("Next Offset=" + (pageHasMore ? resumeOffset.ToString() : "n/a (no more rows)"));

                    if (pageHasMore)
                    {
                        offset = resumeOffset;
                        _logger.LogInformation("Continuing to next page - Next Offset=" + resumeOffset);
                    }
                    else
                    {
                        hasMore = false;
                        _logger.LogInformation("NO ROWS REMAINING — CHAIN ENDED (no next job submitted)");
                    }
                }

                // ── Generate final summary ──────────────────────────────────
                var summary = new SyncSummary
                {
                    Status = "completed",
                    TotalRowsScanned = totalRowsScanned,
                    TotalAgentsFound = totalAgentsFound,
                    TotalCorporatesFound = totalCorporatesFound,
                    TotalInserted = totalInserted,
                    TotalUpdated = totalUpdated,
                    TotalFailed = totalFailed,
                    TotalSkipped = totalSkipped,
                    FailedRecords = allFailedRecords,
                    ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    NextOffset = null,
                    HasMore = false
                };

                _logger.LogInformation("Final Summary");
                _logger.LogInformation("FINAL SUMMARY: " + JsonConvert.SerializeObject(summary));
                _logger.LogInformation("Execution Time=" + summary.ExecutionTime + "ms");
                _logger.LogInformation("JOB COMPLETED");

                return summary;
            }
            catch (Exception err)
            {
                _logger.LogError(err, "FATAL ERROR");
                _logger.LogInformation("--------------------------------------------------");
                _logger.LogInformation("FATAL ERROR");
                _logger.LogInformation("--------------------------------------------------");
                _logger.LogInformation("Error Type=" + err.GetType().Name);
                _logger.LogInformation("Error Message=" + err.Message);
                _logger.LogInformation("Error Stack=" + err.StackTrace);
                _logger.LogInformation("Complete Error Object=" + AppLogger.SafeStringifyError(err));

                var errorSummary = new SyncSummary
                {
                    Status = "error",
                    TotalRowsScanned = totalRowsScanned,
                    TotalAgentsFound = totalAgentsFound,
                    TotalCorporatesFound = totalCorporatesFound,
                    TotalInserted = totalInserted,
                    TotalUpdated = totalUpdated,
                    TotalFailed = totalFailed,
                    TotalSkipped = totalSkipped,
                    FailedRecords = allFailedRecords,
                    ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    NextOffset = null,
                    HasMore = false
                };

                _logger.LogInformation("SUMMARY: " + JsonConvert.SerializeObject(errorSummary) +
                    " message=" + err.Message);

                return errorSummary;
            }
        }
    }
}
