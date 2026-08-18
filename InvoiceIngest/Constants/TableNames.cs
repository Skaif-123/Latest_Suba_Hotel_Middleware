namespace AgentSyncConsole.InvoiceIngest.Constants;

/// <summary>
/// SQL Server table names replacing Catalyst datastore tables
/// (ds.table('Invoice'), ds.table('Invoice_LineItem'), ds.table('ThirdPartyData')).
/// </summary>
public static class TableNames
{
    public const string Invoice = "Invoice";
    public const string InvoiceLineItem = "Invoice_LineItem";
    public const string ThirdPartyData = "ThirdPartyData";
}
