using System.Threading.Tasks;
using Dapper;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Repositories
{
    public class CustomerLookupResult
    {
        public string ROWID { get; set; } = "";
    }

    /// <summary>
    /// Replaces the Customer DataStore table operations:
    ///   - live per-agent lookup:      SELECT ROWID FROM Customer WHERE CustomerID=... AND Customer_Sub_Type='Agent' LIMIT 1
    ///   - duplicate-check lookup performed immediately before every single insert/update
    ///   - customerTable.insertRows(insertRows)  -> InsertCustomerAsync (single immediate INSERT, one row at a time)
    ///   - customerTable.updateRows(updateRows)  -> UpdateCustomerAsync (single immediate UPDATE, one row at a time)
    /// There is no bulk/batched write path - every customer is checked for a
    /// duplicate and written (inserted or updated) immediately, one at a time.
    /// </summary>
    public class CustomerRepository : IAgentCorporateCustomerRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public CustomerRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<CustomerLookupResult?> FindByCustomerIdAsync(string customerId)
        {
            const string sql = @"
                SELECT TOP 1 ROWID
                FROM Customer
                WHERE CustomerID = @CustomerID
                AND Customer_Sub_Type = 'Agent'";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            var rowId = await conn.QueryFirstOrDefaultAsync<string>(sql, new { CustomerID = customerId });
            return rowId == null ? null : new CustomerLookupResult { ROWID = rowId };
        }

        public async Task<CustomerLookupResult?> FindByCustomerIdCorporateAsync(string customerId)
        {
            const string sql = @"
                SELECT TOP 1 ROWID
                FROM Customer
                WHERE CustomerID = @CustomerID
                AND Customer_Sub_Type = 'corporates'";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            var rowId = await conn.QueryFirstOrDefaultAsync<string>(sql, new { CustomerID = customerId });
            return rowId == null ? null : new CustomerLookupResult { ROWID = rowId };
        }

        public async Task<bool> ExistsAsync(string customerId)
        {
            var result = await FindByCustomerIdAsync(customerId);
            return result != null;
        }

        /// <summary>
        /// Inserts a single customer immediately - matches customerTable.insertRows(insertRows)
        /// but executed one row at a time (no bulk/batched insert, no DataTable, no SqlBulkCopy).
        /// Call this immediately after a per-row duplicate check has determined the
        /// customer does not already exist.
        /// </summary>
        public async Task InsertCustomerAsync(CustomerRecord row)
        {
            if (row == null)
            {
                return;
            }

            const string sql = @"
                INSERT INTO Customer (
                    CustomerID,
                    hotelID,
                    First_Name,
                    Code,
                    Last_Name,
                    Email,
                    Company_Name,
                    Customer_Sub_Type,
                    Mobile,
                    Phone,
                    GST_Treatment,
                    GST_NO,
                    Place_Of_Supply,
                    Billing_Country,
                    Billing_State,
                    Billing_City,
                    Billing_Pincode,
                    Shipping_Country,
                    Shipping_State,
                    Shipping_City,
                    Shipping_Pincode,
                    Status,
                    Response,
                    CreatedTime
                ) VALUES (
                    @CustomerID,
                    @hotelID,
                    @First_Name,
                    @Code,
                    @Last_Name,
                    @Email,
                    @Company_Name,
                    @Customer_Sub_Type,
                    @Mobile,
                    @Phone,
                    @GST_Treatment,
                    @GST_NO,
                    @Place_Of_Supply,
                    @Billing_Country,
                    @Billing_State,
                    @Billing_City,
                    @Billing_Pincode,
                    @Shipping_Country,
                    @Shipping_State,
                    @Shipping_City,
                    @Shipping_Pincode,
                    @Status,
                    @Response,
                    SYSDATETIME()
                )";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync(sql, row);
        }

        /// <summary>
        /// Updates a single existing customer immediately - matches
        /// customerTable.updateRows(updateRows) but executed one row at a time
        /// (no bulk/batched update). Call this immediately after a per-row
        /// duplicate check has found an existing customer.
        /// </summary>
        public async Task UpdateCustomerAsync(CustomerRecord row)
        {
            if (row == null)
            {
                return;
            }

            const string sql = @"
                UPDATE Customer SET
                    hotelID           = @hotelID,
                    First_Name        = @First_Name,
                    Code              = @Code,
                    Last_Name         = @Last_Name,
                    Email             = @Email,
                    Company_Name      = @Company_Name,
                    Customer_Sub_Type = @Customer_Sub_Type,
                    Mobile            = @Mobile,
                    Phone             = @Phone,
                    GST_Treatment     = @GST_Treatment,
                    GST_NO            = @GST_NO,
                    Place_Of_Supply   = @Place_Of_Supply,
                    Billing_Country   = @Billing_Country,
                    Billing_State     = @Billing_State,
                    Billing_City      = @Billing_City,
                    Billing_Pincode   = @Billing_Pincode,
                    Shipping_Country  = @Shipping_Country,
                    Shipping_State    = @Shipping_State,
                    Shipping_City     = @Shipping_City,
                    Shipping_Pincode  = @Shipping_Pincode,
                    Status            = @Status,
                    Response          = @Response,
                    SyncStatus        = @Status,
                    SyncResponse      = @Response
                WHERE ROWID = @ROWID";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync(sql, row);
        }
    }
}
