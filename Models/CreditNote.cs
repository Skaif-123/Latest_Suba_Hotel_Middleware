////using System;
////using System.Collections.Generic;
//namespace AgentSyncConsole.Models
//{
//    /// <summary>Maps 1:1 to the "Credit_Note" table (mirrors the Catalyst Credit_Note datastore table).</summary>
//    public class CreditNote
//    {
//        public long ROWID { get; set; }
//        public string? InvoiceID { get; set; }
//        public string? Customer_Name { get; set; }
//        public string? Credit_Note_No { get; set; }
//        public DateTime Credit_Note_Date { get; set; }
//        public string? BooksStatus { get; set; }
//        public string? BooksID { get; set; }
//        public string? Response { get; set; }
//        public string? ThirdpartyStatus { get; set; }
//    }

//    /// <summary>Maps 1:1 to the "Credit_Note_LineItem" table.</summary>
//    public class CreditNoteLineItem
//    {
//        public string? Credit_Note_No { get; set; }
//        public string? Item_Description { get; set; }
//        public string? Quantity { get; set; }
//        public string? Amount { get; set; }
//        public string? SAC_HSN_Code { get; set; }
//    }

//    /// <summary>Execution result, mirrors the various basicIO.write(...) payloads in index.js.</summary>
//    public class CreditNoteSyncResult_ZohoBooks
//    {
//        public string Status { get; set; } = "";
//        public long? CreditNoteROWID { get; set; }
//        public string? InvoiceID { get; set; }
//        public string? CreditNoteNo { get; set; }
//        public string? Reason { get; set; }
//        public string? BooksInvoiceID { get; set; }
//        public string? BooksCreditNoteID { get; set; }
//        public string? CustomerBooksID { get; set; }
//        public decimal? TotalCreditAmount { get; set; }
//        public object? ResolvedGstFields { get; set; }
//        public object? Step1Response { get; set; }
//        public object? Step2Response { get; set; }
//        public string? Message { get; set; }
//    }
//}