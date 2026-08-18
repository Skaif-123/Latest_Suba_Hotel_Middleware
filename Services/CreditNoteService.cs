//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using AgentSyncConsole.Interfaces.CreditNoteInterface;
//using AgentSyncConsole.InvoiceIngest.Interfaces;
//using AgentSyncConsole.InvoiceIngest.Repositories;
//using Microsoft.Extensions.Logging;

//namespace AgentSyncConsole.Services
//{
//    public class CreditNoteService : ICreditService
//    {
//        private readonly IDbConnectionFactory _connectionFactory;
//        private readonly ILogger<CreditNoteSyncService> _logger;
//        private readonly ICreditNoteRepository _creditNoteRepository;

//        public CreditNoteService(IDbConnectionFactory connectionFactory, ILogger<CreditNoteSyncService> logger, ICreditNoteRepository creditNoteRepository)
//        {
//            _connectionFactory = connectionFactory;
//            _creditNoteRepository = creditNoteRepository;
//            _logger = logger;
//        }

//        public async Task<CreditNoteSyncResult> RunAsync(CancellationToken cancellationToken = default)
//        {
//            // =========================
//            // 🔹 COUNTERS
//            // =========================
//            var totalThirdPartyRows = 0;
//            var totalValidJSONRows = 0;
//            var totalInvalidJSONRows = 0;
//            var totalInvoicesFound = 0;
//            var totalCreditNotesFound = 0;

//            // =========================
//            // 🔹 FOLIO TYPE SUMMARY
//            // Counts every folioType value encountered across all processed
//            // invoices, so it is immediately obvious whether any "CN" invoices
//            // exist in the data at all.
//            // =========================
//            var folioTypeSummary = new Dictionary<string, int>();

//            // =========================
//            // 🔹 UNKNOWN FOLIO TYPE CAPTURE
//            // Stores complete diagnostic information for every invoice whose
//            // folioType could not be determined, so the root cause can be
//            // inspected directly from the response.
//            // =========================
//            var unknownFolioInvoices = new List<UnknownFolioInvoiceEntry>();

//            var totalInsertedCreditNotes = 0;
//            var totalUpdatedCreditNotes = 0;
//            var totalInsertedCreditNoteLineItems = 0;
//            var totalUpdatedCreditNoteLineItems = 0;
//            var duplicateCreditNoteLinesSkipped = 0;

//            var insertedCreditNotes = new List<CreditNoteRow>();
//            var updatedCreditNotes = new List<CreditNoteRow>();
//            var insertedCreditNoteLineItems = new List<CreditNoteLineItemRow>();
//            var updatedCreditNoteLineItems = new List<CreditNoteLineItemRow>();
//            var logs = new List<object>();


//            try
//            {
//                var creditNotes = await _creditNoteRepository.GetCreditNotesAsync();



//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine(ex);
//            }


//            return null;
//        }
//    }
//}
