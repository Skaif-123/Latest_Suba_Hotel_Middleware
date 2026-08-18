namespace AgentSyncConsole.Utilites
{
    /// <summary>
    /// Mirrors the literal/config values that were hard-coded in the original
    /// Catalyst Job Function (PAGE_SIZE, MAX_RUNTIME, offset cache key, etc.).
    /// </summary>
    public static class Constants
    {
        // Original: const PAGE_SIZE = 20;
        public const int PAGE_SIZE = 300;

        // Original: const MAX_RUNTIME = 90000; (mid-page safety net only, NOT a page loop)
        public const int MAX_RUNTIME_MS = 90000;

        // Original: OFFSET_CACHE_KEY = 'AgentSync_Offset' (now the
        // .ModuleName row key)
        public const string ModuleName = "AgentSync";

        public const string CustomerSubType = "Agent";

        public const string GstTreatment = "business_gst";


        public const string BooksStatusPending = "pending";
        public const string BooksStatusProcessed = "processed";
        public const string BooksStatusFailed = "failed";
        public const string BooksStatusSkipped = "skipped";

        public const string OwnerTypeGuest = "GUEST";

        public const string GstTypeGst = "GST";
        public const string GstTypeIgst = "IGST";

        public const int BooksCodeSuccess = 0;
        public const int BooksCodeResourceNotFound = 1002;
        public const int BooksCodeInvalidId = 1004;

        public const string DefaultUnit = "nos";
        public const string DefaultLineItemName = "Hotel Charges";
        public const string DefaultLineItemDescription = "Room Invoice";

        /// <summary>
        /// Only invoices whose Invoice_Number starts with this prefix are Tax
        /// Invoices and should be processed. Proforma Invoices ("PI 61349") do
        /// not start with this prefix and are skipped.
        /// </summary>
        public const string TaxInvoiceFolioPrefix = "INVC";
    }
}
