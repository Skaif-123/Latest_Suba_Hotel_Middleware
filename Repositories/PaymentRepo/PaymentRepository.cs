using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces.PaymentInterface;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.InvoiceIngest.Models;
using AgentSyncConsole.Models;
using Microsoft.Data.SqlClient;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AgentSyncConsole.Repositories.PaymentRepo
{
    public class PaymentRepository : IPaymentRepository
    {

        private readonly SqlConnectionFactory _connectionFactory;

        public PaymentRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        
        public async Task<List<string>> GetPaymentsAsync()
        {
            List<string> payments = new List<string>();

            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            string selectQuery = @"
        SELECT ROWID, payments
        FROM ThirdPartyData
        WHERE payments IS NOT NULL
          AND LTRIM(RTRIM(payments)) <> ''
          AND (status IS NULL OR status = '')
        ORDER BY ROWID";

            using var command = new SqlCommand(selectQuery, connection);
            using var reader = await command.ExecuteReaderAsync();

            var records = new List<(string RowId, string Payment)>();


            while (await reader.ReadAsync())
            {
                string rowId = reader["ROWID"]?.ToString() ?? "";
                string payment = reader["payments"]?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(payment))
                {
                    records.Add((rowId, payment));
                }
            }

            await reader.CloseAsync();

            foreach (var record in records)
            {
                string updateQuery = @"
            UPDATE ThirdPartyData
            SET syncStatus = 'fetched data from thirdparty into PaymentDetail Table',
                syncTime = SYSDATETIME(),
                status = 'transfered'
            WHERE ROWID = @RowId";

                using var updateCommand = new SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@RowId", record.RowId);
                await updateCommand.ExecuteNonQueryAsync();

                payments.Add(record.Payment);
            }

            return payments;
        }





        public async Task AddPaymentAsync(PaymentDetail payment)
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string checkQuery = @"
        SELECT CASE
            WHEN EXISTS (
                SELECT 1
                FROM PaymentDetails
                WHERE Payment_No = @Payment_No
            )
            THEN 1
            ELSE 0
        END;";

            using var checkCommand = new SqlCommand(checkQuery, connection);

            checkCommand.Parameters.AddWithValue(
                "@Payment_No",
                (object?)payment.Payment_No ?? DBNull.Value);

            var result = await checkCommand.ExecuteScalarAsync();

            bool paymentExists = Convert.ToInt32(result) == 1;

            if (paymentExists)
            {
                throw new Exception(
                    $"Payment with Payment_No '{payment.Payment_No}' already exists.");



            }

            // Payment does NOT exist, so continue with insertion

            const string insertQuery = @"
        INSERT INTO PaymentDetails (
            Customer_Name,
            Location_Name,
            Amount_Received,
            Payment_Date,
            Payment_No,
            Payment_Mode,
            Deposit_to,
            Tax_if_Applicable_COApaymentID,
            Hotel_ID,
            Books_ID,
            Books_Status,
            Response,
            Details,
            groupId
        )
        VALUES (
            @Customer_Name,
            @Location_Name,
            @Amount_Received,
            @Payment_Date,
            @Payment_No,
            @Payment_Mode,
            @Deposit_to,
            @Tax_if_Applicable_COApaymentID,
            @Hotel_ID,
            @Books_ID,
            @Books_Status,
            @Response,
            @Details,
            @GroupId
        );";

            using var insertCommand = new SqlCommand(insertQuery, connection);

            insertCommand.Parameters.AddWithValue("@Customer_Name",
                (object?)payment.Customer_Name ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Location_Name",
                (object?)payment.Location_Name ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Amount_Received",
                (object?)payment.Amount_Received ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Payment_Date",
                (object?)payment.Payment_Date ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Payment_No",
                (object?)payment.Payment_No ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Payment_Mode",
                (object?)payment.Payment_Mode ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Deposit_to",
                (object?)payment.Deposit_to ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Tax_if_Applicable_COApaymentID",
                (object?)payment.Tax_if_Applicable_COApaymentID ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Hotel_ID",
                (object?)payment.Hotel_ID ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Books_ID",
                (object?)payment.Books_ID ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Books_Status",
                (object?)payment.Books_Status ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Response",
                (object?)payment.Response ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@Details",
                (object?)payment.Details ?? DBNull.Value);

            insertCommand.Parameters.AddWithValue("@GroupId",
               (object?)payment.groupId ?? DBNull.Value);

            await insertCommand.ExecuteNonQueryAsync();
        }






        // This is for update Payment zoho sql queries
        public async Task<List<PaymentDetail>> GetPendingPaymentsAsync()
        {
            var payments = new List<PaymentDetail>();

            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            //string query2 = 
            /*@"SELECT 
P.*
FROM PaymentDetails P 
INNER JOIN Invoice I 
ON P.Customer_Name = I.Reservation_ID
INNER JOIN Customer C 
ON C.CustomerID = I.Customer_Name
WHERE (P.Books_Status IS NULL
OR P.Books_Status = 'failed'
OR P.Books_Status = 'invoice not present'
OR  p.Books_Status ='')
AND(I.BooksInvoiceID IS NOT NULL)*/


            string query = @"Select * from PaymentDetails where (Books_Status IS NULL OR Books_Status = 'Failed' OR Books_Status = 'Processed' OR Books_Status ='')";



            using var command = new SqlCommand(query, connection);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                payments.Add(new PaymentDetail
                {
                    Customer_Name = reader["Customer_Name"]?.ToString(),
                    Location_Name = reader["Location_Name"]?.ToString(),
                    Amount_Received = reader["Amount_Received"]?.ToString(),
                    Payment_Date = reader["Payment_Date"]?.ToString(),
                    Payment_No = reader["Payment_No"]?.ToString(),
                    Payment_Mode = reader["Payment_Mode"]?.ToString(),
                    Deposit_to = reader["Deposit_to"]?.ToString(),
                    Hotel_ID = reader["Hotel_ID"]?.ToString(),
                    Books_ID = reader["Books_ID"]?.ToString(),
                    Books_Status = reader["Books_Status"]?.ToString(),
                    Details = reader["Details"]?.ToString(),
                    Response = reader["Response"]?.ToString()
                });
            }

            return payments;
        }



        public async Task<InvoiceDetails?> GetInvoiceByReservationIdAsync(string reservationId, string HOTELID)
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string query = @"
        SELECT 
            I.BooksInvoiceID, 
            I.Invoice_Number, 
            I.Customer_Name, 
            I.Reservation_ID, 
            I.Owner_Type,
                   IL.InvoiceID, 
            IL.TransactionID, 
            T.Transaction_ID, 
            T.Amount, 
            T.Tax_value, 
            T.Rate
        FROM Invoice I
               LEFT JOIN Invoice_LineItem IL
                   ON IL.InvoiceID = I.InvoiceID
               LEFT JOIN TransactionModule T
                   ON T.Transaction_ID = IL.TransactionID
               WHERE I.Reservation_ID = @Reservation_ID
                 AND I.Hotel_ID = @Hotel_ID
                 AND ISNULL(I.BooksInvoiceID, '') <> ''";
       
    using var command = new SqlCommand(query, connection);

            command.Parameters.AddWithValue("@Reservation_ID", reservationId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Hotel_ID", HOTELID ?? (object)DBNull.Value);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new InvoiceDetails
                {
                    BooksInvoiceID = reader["BooksInvoiceID"] != DBNull.Value ? reader["BooksInvoiceID"].ToString() : null,
                    InvoiceNumber = reader["Invoice_Number"] != DBNull.Value ? reader["Invoice_Number"].ToString() : null,
                    Customer_Name = reader["Customer_Name"] != DBNull.Value ? reader["Customer_Name"].ToString() : null,
                    InvoiceID = reader["InvoiceID"] != DBNull.Value ? reader["InvoiceID"].ToString() : null,
                    Reservation_ID = reader["Reservation_ID"] != DBNull.Value ? reader["Reservation_ID"].ToString() : null,
                    Transaction_ID = reader["Transaction_ID"] != DBNull.Value ? reader["Transaction_ID"].ToString() : null,
                    TransactionAmount = reader["Amount"] != DBNull.Value ? reader["Amount"].ToString() : null,
                    TransactionRate = reader["Rate"] != DBNull.Value ? reader["Rate"].ToString() : null,
                    Tax_value = reader["Tax_value"] != DBNull.Value ? reader["Tax_value"].ToString() : null,
                    Owner_Type = reader["Owner_Type"] != DBNull.Value ? reader["Owner_Type"].ToString() : null
                };
            }

            return null;
        }

        public async Task<CustomerDetail?> GetCustomerByCustomerIdAsync(string customerId,string hotelId)
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string query = @"
        SELECT TOP 1
            CustomerID,
            booksID
        FROM Customer
        WHERE CustomerID = @CustomerID
          AND hotelID = @Hotel_ID";

            using var command = new SqlCommand(query, connection);

            command.Parameters.AddWithValue("@CustomerID", customerId);
            command.Parameters.AddWithValue("@Hotel_ID", hotelId);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new CustomerDetail
                {
                    CustomerID = reader["CustomerID"]?.ToString(),
                    BooksID = reader["booksID"]?.ToString()
                };
            }

            return null;
        }

        public async Task<List<PaymentInvoice>>
            GetPaymentInvoicetransactionDetailsAsync(
                string customerName,
                string hotelId)
        {
            using var connection =
                await _connectionFactory.CreateOpenConnectionAsync();

            /*const string query = @"
        SELECT
            I.BooksInvoiceID,
            T.Amount AS InvoiceAmount,
            T.Rate AS TransactionRate
        FROM PaymentDetails P
        INNER JOIN Invoice I
            ON I.Reservation_ID = P.Customer_Name
            AND I.Hotel_ID = P.Hotel_ID
        INNER JOIN Invoice_LineItem IL
            ON IL.InvoiceID = I.BooksInvoiceID
        INNER JOIN TransactionModule T
            ON T.Transaction_ID = IL.TransactionID
        WHERE P.Customer_Name = @Customer_Name
          AND P.Hotel_ID = @Hotel_ID
          AND NOT (I.BooksInvoiceID = '' OR I.BooksInvoiceID IS NULL)";*/

            const string query = @"

SELECT
    I.BooksInvoiceID,
    I.Invoice_Number,
    IL.InvoiceID,
    IL.TransactionID,
    T.Transaction_ID,
    T.Amount AS TransactionAmount, 
    T.Tax_value,
    T.Rate AS TransactionRate     
FROM Invoice I
LEFT JOIN Invoice_LineItem IL
    ON IL.InvoiceID = I.InvoiceID
LEFT JOIN TransactionModule T
    ON T.Transaction_ID = IL.TransactionID
WHERE I.Reservation_ID = @Customer_Name
  AND I.Hotel_ID = @Hotel_ID
  AND I.BooksInvoiceID IS NOT NULL
AND LTRIM(RTRIM(I.BooksInvoiceID)) <> ''
";

            using var command = new SqlCommand(query, connection);

            command.Parameters.AddWithValue("@Customer_Name", customerName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Hotel_ID", hotelId ?? (object)DBNull.Value);

            using var reader = await command.ExecuteReaderAsync();
            var result = new List<PaymentInvoice>();

            while (await reader.ReadAsync())
            {
                result.Add(new PaymentInvoice
                {
                    BooksInvoiceID = reader["BooksInvoiceID"] != DBNull.Value ? reader["BooksInvoiceID"].ToString() : null,
                    InvoiceNumber = reader["Invoice_Number"] != DBNull.Value ? reader["Invoice_Number"].ToString() : null,
                    InvoiceID = reader["InvoiceID"] != DBNull.Value ? reader["InvoiceID"].ToString() : null,
                    TransactionID = reader["TransactionID"] != DBNull.Value ? reader["TransactionID"].ToString() : null,
                    Transaction_ID = reader["Transaction_ID"] != DBNull.Value ? reader["Transaction_ID"].ToString() : null,
                    TransactionAmount = reader["TransactionAmount"] != DBNull.Value ? reader["TransactionAmount"].ToString() : null,
                    Tax_value = reader["Tax_value"] != DBNull.Value ? reader["Tax_value"].ToString() : null,
                    TransactionRate = reader["TransactionRate"] != DBNull.Value ? reader["TransactionRate"].ToString() : null
                });
            }

            return result;
        }

        public async Task<Bank_COA?> GetBankCoaByNameAsync_B2C(string details)
        {
            // Determine the COA_Name ("Credit Card", "UPI", or "Cash") in C# first
            string coaName = DetermineCoaName(details);

            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string query = @"SELECT TOP 1 COA_Name, BooksID 
                           FROM Bank_COA 
                           WHERE COA_Name = @COA_Name;";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@COA_Name", coaName);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Bank_COA
                {
                    COA_Name = reader["COA_Name"]?.ToString(),
                    BooksID = reader["BooksID"]?.ToString()
                };
            }

            return null;
        }


        public async Task<Bank_COA?> GetBankCoaByNameAsync_B2B()
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            // 1. Added COA_Name to the SELECT clause
            const string query = @"SELECT 
             
            C.booksID
        FROM PaymentDetails P 
        INNER JOIN Invoice I 
            ON P.Customer_Name = I.Reservation_ID
        INNER JOIN Customer C 
            ON C.CustomerID = I.Customer_Name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Bank_COA
                {

                    BooksID = reader["booksID"] == DBNull.Value ? null : reader["booksID"].ToString()
                };
            }

            return null;
        }
        // Private helper method to handle payment details parsing
        private string DetermineCoaName(string details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return "Cash";
            }

            string text = details.ToLower();

            if (text.Contains("card") || text.Contains("mastercard") || text.Contains("visa") || text.Contains("amex"))
            {
                return "Credit Card";
            }

            if (text.Contains("upi") || text.Contains("gpay") || text.Contains("phone pe") || text.Contains("phonepe") || text.Contains("paytm") || text.Contains("qr code"))
            {
                return "UPI";
            }

            return "Cash";
        }


        public async Task UpdatePaymentSuccessAsync(
    string paymentNo,
    string booksPaymentId,
    string response
    )
        {
            using var connection =
                await _connectionFactory.CreateOpenConnectionAsync();

            const string query = @"
                UPDATE PaymentDetails
                SET
                    Books_Status = @Books_Status,
                    Books_ID = @Books_ID,
                    Response = @Response,
                    syncStatus= 'Inserted data in Zoho Books',
                    syncTime = SYSDATETIME()
                WHERE Payment_No = @Payment_No AND (Books_Status IS NULL OR Books_Status='' OR Books_Status='failed') ";

            using var command =
                new SqlCommand(query, connection);

            command.Parameters.AddWithValue("@Books_Status", "Processed");
            command.Parameters.AddWithValue("@Books_ID", booksPaymentId);
            command.Parameters.AddWithValue("@Response", response);
            command.Parameters.AddWithValue("@Payment_No", paymentNo);


            await command.ExecuteNonQueryAsync();
        }




        public async Task UpdatePaymentFailedAsync(
        string paymentNo,
        string response)
        {
            using var connection =
                await _connectionFactory.CreateOpenConnectionAsync();

            const string query = @"
UPDATE PaymentDetails
SET
    Books_Status = @Books_Status,
    Response = @Response,
    syncStatus= 'Failed to insert data in Books PaymentDetail Table'
WHERE Payment_No = @Payment_No";

            using var command =
                new SqlCommand(query, connection);

            command.Parameters.AddWithValue("@Books_Status", "Failed");
            command.Parameters.AddWithValue("@Response", response);
            command.Parameters.AddWithValue("@Payment_No", paymentNo);

            await command.ExecuteNonQueryAsync();
        }
    }
}



