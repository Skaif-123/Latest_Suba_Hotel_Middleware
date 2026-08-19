using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AgentSyncConsole.Interfaces;
using InvoiceIngest = AgentSyncConsole.InvoiceIngest;
using CustomerBooks = AgentSyncConsole.CustomerBooks;
using AgentSyncConsole.Interfaces.PaymentInterface;
using AgentSyncConsole.Interfaces.PosInvoiceInterface;
using AgentSyncConsole.Services;

namespace AgentSyncConsole.Services
{
    /// <summary>
    /// Top-level composition of the merged application. Runs, in this exact
    /// order and never out of sequence:
    ///
    ///   1) Agent Sync + Corporate Sync   (AgentCorporateSyncService)
    ///   2) Customer Books Sync           (CustomerBooks.Services.CustomerBooksSyncService,
    ///                                     paged in a single in-process loop)
    ///   3) Invoice JSON -&gt; SQL           (InvoiceIngest.Services.InvoiceSyncService,
    ///                                     looped page-by-page exactly like the
    ///                                     original InvoiceSync Program.cs did)
    ///   4) Books Invoice Sync            (BooksInvoiceSyncService)
    ///
    /// Customer Books Sync runs against an independent data set (dbo.Customer
    /// booksID/Response/status write-back), so — same policy already applied
    /// to the Agent/Corporate -&gt; Invoice JSON step below — it always runs
    /// once Agent/Corporate Sync has finished, whether or not that step
    /// reported a clean "completed" status.
    ///
    /// Books Invoice Sync is only started once the Invoice JSON -&gt; SQL phase
    /// has finished and did not report an "error" status on any page - it
    /// reads the very rows step 3 just wrote/updated in the Invoice table.
    ///
    /// NOTE: IInvoiceSyncService.RunOnceAsync() takes only a CancellationToken.
    /// The per-row Invoice_Date value is resolved internally, from each row's
    /// own hotelogix.datetime, during InvoiceSyncService's own Pass 1 — it is
    /// not something the pipeline orchestrator has (or needs) to supply here.
    /// </summary>
    public class PipelineRunner : IPipelineRunner
    {
        private readonly IAgentCorporateSyncService _agentCorporateSyncService;
        private readonly CustomerBooks.Interfaces.ICustomerBooksSyncService _customerBooksSyncService;
        private readonly InvoiceIngest.Interfaces.IInvoiceSyncService _invoiceSyncService;
        private readonly IBooksInvoiceSyncService _booksInvoiceSyncService;
        private readonly ILogger<PipelineRunner> _logger;
        private readonly ITransactionSyncService _transactionSyncService;
        private readonly IPaymentService _paymentService;
        private readonly IPaymentRepository _paymentRepository;
        private readonly ICreditNoteSyncService _creditNoteSyncService;
        private readonly ICreditNoteSyncService_ZohoBooks _creditNoteSyncService_ZohoBooks;
        private readonly IPosInvoiceService _posInvoiceService;
        private readonly IPosInvoiceBooksSyncService _posInvoiceBooksSyncService;

        public PipelineRunner(
            IAgentCorporateSyncService agentCorporateSyncService,
            CustomerBooks.Interfaces.ICustomerBooksSyncService customerBooksSyncService,
            InvoiceIngest.Interfaces.IInvoiceSyncService invoiceSyncService,
            IBooksInvoiceSyncService booksInvoiceSyncService,
            ITransactionSyncService transactionSyncService,
            IPaymentRepository paymentRepository,
            ICreditNoteSyncService creditNoteSyncService,
            ICreditNoteSyncService_ZohoBooks creditNoteSyncService_ZohoBooks,
            IPaymentService paymentService,
            IPosInvoiceService posInvoiceService,

            IPosInvoiceBooksSyncService posInvoiceBooksSyncService,
        ILogger<PipelineRunner> logger)
        {
            _agentCorporateSyncService = agentCorporateSyncService;
            _customerBooksSyncService = customerBooksSyncService;
            _invoiceSyncService = invoiceSyncService;
            _booksInvoiceSyncService = booksInvoiceSyncService;
            _transactionSyncService = transactionSyncService;
            _paymentRepository = paymentRepository;
            _paymentService = paymentService;
            _creditNoteSyncService = creditNoteSyncService;
            _creditNoteSyncService_ZohoBooks = creditNoteSyncService_ZohoBooks;
            _posInvoiceService = posInvoiceService;
            _posInvoiceBooksSyncService = posInvoiceBooksSyncService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct = default)
        {
            var overallSuccess = true;


           
            // =========================================================
            // STEP 1: AGENT SYNC + CORPORATE SYNC
            // =========================================================
            Console.WriteLine("==================================================");
            Console.WriteLine("AGENT / CORPORATE SYNC START");
            Console.WriteLine("==================================================");

            //var agentCorporateSummary = await _agentCorporateSyncService.RunAsync(ct);

            //_logger.LogInformation(
            //    "AGENT / CORPORATE SYNC FINISHED => status={Status}, rowsScanned={RowsScanned}, agents={Agents}, corporates={Corporates}, inserted={Inserted}, updated={Updated}, failed={Failed}, skipped={Skipped}",
            //    agentCorporateSummary.Status, agentCorporateSummary.TotalRowsScanned, agentCorporateSummary.TotalAgentsFound,
            //    agentCorporateSummary.TotalCorporatesFound, agentCorporateSummary.TotalInserted, agentCorporateSummary.TotalUpdated,
            //    agentCorporateSummary.TotalFailed, agentCorporateSummary.TotalSkipped);

            //if (agentCorporateSummary.Status != "completed")
            //{
            //    overallSuccess = false;
            //    _logger.LogWarning("Agent/Corporate sync did not complete cleanly — continuing to Invoice JSON -> SQL regardless (independent data sets), but overall run will be reported as failed.");
            //}




            //=========================================================
            //STEP 2: CUSTOMER BOOKS SYNC
            //Independent data set(dbo.Customer) — always runs after
            //Agent / Corporate Sync and before Invoice JSON->SQL, same as
            //Invoice JSON->SQL is never skipped just because Agent /
            //Corporate reported a non - clean status.



            //=========== comment by amol ==============================================
            // =========================================================
            //var customerBooksSummary = await _customerBooksSyncService.RunFullSyncAsync(ct);

            //_logger.LogInformation(
            //    "===================================\n" +
            //    "Customer Books Sync Summary\n" +
            //    "===================================\n" +
            //    "Total Scanned : {TotalScanned}\n" +
            //    "Created       : {Created}\n" +
            //    "Updated       : {Updated}\n" +
            //    "Failed        : {Failed}\n" +
            //    "Execution Time: {ExecutionTimeMs} ms\n" +
            //    "===================================",
            //    customerBooksSummary.TotalScanned, customerBooksSummary.Created,
            //    customerBooksSummary.Updated, customerBooksSummary.Failed, customerBooksSummary.ExecutionTimeMs);

            //if (customerBooksSummary.Status != "success")
            //{
            //    overallSuccess = false;
            //    _logger.LogWarning("Customer Books Sync did not complete cleanly — see Logs folder for details. Continuing to Invoice JSON -> SQL regardless (independent data set).");
            //}





            //var transactionSummary = await _transactionSyncService.RunAsync();









            // =========================================================
            // STEP 3: INVOICE JSON -> SQL
            // Loops page-by-page exactly like the original InvoiceSync
            // Program.cs while(hasMore) loop: stop when a page finds nothing
            // left to process, or when a page's runtime guard trips (resume
            // on next run), or when a page reports an error.
            // =========================================================
            _logger.LogInformation("==================================================");
            _logger.LogInformation("INVOICE JSON -> SQL START");
            _logger.LogInformation("==================================================");

            //var invoiceIngestFailed = false;
            //var pageCount = 0;
            //var hasMore = true;
            //while (hasMore)
            //{
            //    pageCount++;

            //    var result = await _invoiceSyncService.RunOnceAsync(ct);

            //    _logger.LogInformation(
            //        "INVOICE JSON -> SQL page {PageNum} complete. Status={Status} ProcessedRows={ProcessedRows} StoppedEarly={StoppedEarly}",
            //        pageCount, result.Status, result.ProcessedRows, result.ExecutionStoppedEarly);

            //    if (result.Status == "error")
            //    {
            //        _logger.LogError("INVOICE JSON -> SQL halted due to error: {Message}", result.Message);
            //        invoiceIngestFailed = true;
            //        hasMore = false;
            //        break;
            //    }

            //    // Stop when this page found nothing left to process.
            //    if (result.ProcessedRows == 0 && !result.ExecutionStoppedEarly)
            //    {
            //        hasMore = false;
            //    }

            //    // Stop if runtime guard tripped — next run resumes.
            //    if (result.ExecutionStoppedEarly)
            //    {
            //        _logger.LogInformation("INVOICE JSON -> SQL runtime limit reached — stopping this run; resume on next invocation.");
            //        hasMore = false;
            //    }
            //}

            //_logger.LogInformation("INVOICE JSON -> SQL FINISHED after {PageCount} page(s). Failed={Failed}", pageCount, invoiceIngestFailed);

            //if (invoiceIngestFailed)
            //{
            //    overallSuccess = false;
            //}

            //// =========================================================
            //// STEP 4: BOOKS INVOICE SYNC
            //// Never runs before Invoice JSON -> SQL has completed successfully.
            //// =========================================================
            //if (invoiceIngestFailed)
            //{
            //    _logger.LogWarning("BOOKS INVOICE SYNC SKIPPED — Invoice JSON -> SQL did not complete successfully.");
            //}
            //else
            //{
            //    _logger.LogInformation("==================================================");
            //    _logger.LogInformation("BOOKS INVOICE SYNC START");
            //    _logger.LogInformation("==================================================");

                //var booksSummary = await _booksInvoiceSyncService.RunAsync(ct);

            //    _logger.LogInformation(
            //        "BOOKS INVOICE SYNC FINISHED => status={Status}, created={Created}, updated={Updated}, skipped={Skipped}",
            //        booksSummary.Status, booksSummary.TotalCreated, booksSummary.TotalUpdated, booksSummary.TotalSkipped);

            //    if (booksSummary.Status != "success")
            //    {
            //        overallSuccess = false;
            //    }
            //}


            // =========================================================
            // STEP 4: PAYMENT SYNC
            // =========================================================
            Console.WriteLine("==================================================");
            Console.WriteLine("PosInvoice process START");
            Console.WriteLine("==================================================");

            //await _posInvoiceService.RunOnceAsync();

            // =========================================================
            Console.WriteLine("==================================================");
            Console.WriteLine("PosInvoice ZohoBooks POST START");
            Console.WriteLine("==================================================");
            //await _posInvoiceBooksSyncService.RunAsync();


            Console.WriteLine("Starting with payment part");
            //await _paymentService.PrintPaymentsAsync();
            await _paymentService.UploadPaymentsToZohoAsync();

            Console.WriteLine("Starting with credit note part");
            //await _creditNoteSyncService.RunAsync(default);
            //await _creditNoteSyncService_ZohoBooks.RunAsync(default);




            return overallSuccess ? 0 : 1;
        }
    }
}