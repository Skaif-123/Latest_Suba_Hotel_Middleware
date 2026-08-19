using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces.PaymentInterface
{
    public interface IPaymentRepository
    {
        Task<List<string>> GetPaymentsAsync();
        Task AddPaymentAsync(PaymentDetail paymentDetail);
        Task<InvoiceDetails?> GetInvoiceByReservationIdAsync(string reservationId,string HotelID);
        Task<List<PaymentInvoice>> GetPaymentInvoicetransactionDetailsAsync(string customerId,string hotelId);

        Task<CustomerDetail?> GetCustomerByCustomerIdAsync(string customerId,string hotelId);

        Task<List<PaymentDetail>> GetPendingPaymentsAsync();

        Task<Bank_COA?> GetBankCoaByNameAsync_B2C(string details);
        Task<Bank_COA?> GetBankCoaByNameAsync_B2B();


        Task UpdatePaymentSuccessAsync(
                    string paymentNo,
                    string booksPaymentId,
                    string response);

        Task UpdatePaymentFailedAsync(
                        string paymentNo,
                        string response);
    }
}
