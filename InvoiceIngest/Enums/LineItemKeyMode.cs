namespace AgentSyncConsole.InvoiceIngest.Enums;

/// <summary>
/// Matches the JavaScript keyMode variable returned by queryLineItemMap():
/// 'transId' when Hotelogix_Trans_ID column exists, 'composite' when it
/// doesn't and the function falls back to HSN_SAC_Code + Name + Amount.
/// </summary>
public enum LineItemKeyMode
{
    TransId = 0,
    Composite = 1
}
