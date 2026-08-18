namespace AgentSyncConsole.Models;

/// <summary>
/// Maps 1:1 to the "Transaction" table used by the Hotelogix Transaction Sync
/// function. No Transaction table/model existed anywhere in this project prior
/// to this conversion, so this is new (per the "only create missing methods /
/// models" rule). Column names and types are locked to match the rowData
/// object built in the original Catalyst source exactly: Transaction_ID,
/// Reservation_ID, Tax_value, HSN_Code, Product_Name, Amount, Rate. ROWID
/// follows the same convention already used by every other table migrated
/// from the Catalyst datastore in this project (see Models/Invoice.cs).
/// </summary>
public class Transaction
{
    public int ROWID { get; set; }

    public string Transaction_ID { get; set; } = string.Empty;

    public string Reservation_ID { get; set; } = string.Empty;

    /// <summary>Sum of taxBreakup[].amount — always numeric in the original (parseFloat), never a string.</summary>
    public decimal Tax_value { get; set; }

    public string HSN_Code { get; set; } = string.Empty;

    public string Product_Name { get; set; } = string.Empty;

    /// <summary>String(txn.priceBfDisc || '0') in the original — defaults to "0", not "".</summary>
    public string Amount { get; set; } = string.Empty;

    /// <summary>String(txn.netTotal || '0') in the original — defaults to "0", not "".</summary>
    public string Rate { get; set; } = string.Empty;

    public string Status { get; set; }
    public string Response { get; set; }
}
