using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using System.Threading.Tasks;
using System.Threading;
using System;
namespace AgentSyncConsole.Repositories;

/// <summary>Equivalent of: SELECT * FROM Item_Master WHERE Product_Name = ? LIMIT 1</summary>
public class ItemMasterRepository : IItemMasterRepository
{
    private readonly SqlConnectionFactory _factory;

    public ItemMasterRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<ItemMaster?> FindByProductNameAsync(string productName, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT TOP 1 Product_Name, Categories, COA,COA_Id, Amount, GST, HSN_Or_SAC, BooksID, TaxID
                              FROM Item_Master
                              WHERE Product_Name = @ProductName  and master_type='POS'";
        Console.WriteLine(sql);
        Console.WriteLine($"Product Name: {productName}");
        return await conn.QuerySingleOrDefaultAsync<ItemMaster>(
            new CommandDefinition(sql, new { ProductName = productName }, cancellationToken: ct));
    }

    public async Task<ItemMaster?> FindByProductNameAsyncFrontDesk(string productName, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT TOP 1 Product_Name, Categories, COA,COA_Id, Amount, GST, HSN_Or_SAC, BooksID, TaxID
                              FROM Item_Master
                              WHERE item_type = @ProductName and master_type='FD'";
        Console.WriteLine(sql);
        Console.WriteLine($"Product Name: {productName}");
        return await conn.QuerySingleOrDefaultAsync<ItemMaster>(
            new CommandDefinition(sql, new { ProductName = productName }, cancellationToken: ct));
    }
}

