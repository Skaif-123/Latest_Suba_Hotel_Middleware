using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Interfaces.PaymentInterface;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.Models;
using AgentSyncConsole.Models.jsonModels;
using Microsoft.Extensions.Configuration;
using static AgentSyncConsole.Services.PaymentService;

namespace AgentSyncConsole.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentRepository _repository;
        private readonly HttpClient _httpClient;
        private readonly string _tokenApplication;

        // NEW (Change 3/8) — Guest Owner_Type Books Customer IDs, sourced from appsettings.json.
        private readonly string _cashCustomerBooksId;
        private readonly string _upiCustomerBooksId;
        private readonly string _creditCardCustomerBooksId;
        private readonly IAccessTokenService _accessTokenService;

        public PaymentService(IPaymentRepository repository, HttpClient httpClient, IAccessTokenService accessTokenService, IConfiguration configuration)
        {
            _repository = repository;
            _httpClient = httpClient;
            _accessTokenService = accessTokenService;
            _tokenApplication = configuration["ZohoAuth:TokenApplication"] ?? "Books";


            // NEW (Change 3/8)
            _cashCustomerBooksId = configuration["GuestCustomerMapping:CashCustomerBooksId"] ?? string.Empty;
            _upiCustomerBooksId = configuration["GuestCustomerMapping:UpiCustomerBooksId"] ?? string.Empty;
            _creditCardCustomerBooksId = configuration["GuestCustomerMapping:CreditCardCustomerBooksId"] ?? string.Empty;
        }



        public async Task PrintPaymentsAsync()
        {
            Console.WriteLine("Please wait for 10 minutes we are fetching data and updating status in thirdpartydata");
            var paymentJsonList = await _repository.GetPaymentsAsync();

            Console.WriteLine($"Total JSON records: {paymentJsonList.Count}");

            foreach (var json in paymentJsonList)
            {
                try
                {

                    var options = new JsonSerializerOptions
                    {
                        NumberHandling = JsonNumberHandling.AllowReadingFromString
                    };
                    //Console.WriteLine(json);
                    var root = JsonSerializer.Deserialize<PaymentRoot>(json, options);
                    //Console.WriteLine(root.Hotelogix.Response.HotelId);

                    if (root == null)
                        continue;

                    var hotelId = root.Hotelogix.Response.HotelId.ToString();
                    var reservation = root.Hotelogix.Response;

                    foreach (var payment in reservation.Data.Payments)
                    {
                        var paymentDetail = new PaymentDetail
                        {
                            Customer_Name = payment.RsvId?.ToString() ?? null,//
                            Location_Name = hotelId.ToString() ?? null,
                            Amount_Received = payment.Amount ?? null,
                            Payment_Date = payment.Date ?? null,
                            Payment_No = payment.Id ?? null,
                            Payment_Mode = payment.PayTypeId ?? null,
                            Deposit_to = payment.Receipt ?? null,
                            Details = payment.Details ?? null,
                            Hotel_ID = hotelId ?? null,
                        };

                        // Serialize objects to JSON
                        //string paymentJson = JsonSerializer.Serialize(payment, new JsonSerializerOptions { WriteIndented = true });
                        //string paymentDetailJson = JsonSerializer.Serialize(paymentDetail, new JsonSerializerOptions { WriteIndented = true });

                        //Console.WriteLine("--- Payment ---");
                        //Console.WriteLine(paymentJson);

                        //Console.WriteLine("--- Payment Detail ---");
                        //Console.WriteLine(paymentDetailJson);



                        await _repository.AddPaymentAsync(paymentDetail);

                        Console.WriteLine("--------------------------------------");
                        Console.WriteLine($"Inserted Payment : {paymentDetail.Payment_No}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                }
            }

            Console.WriteLine();
            Console.WriteLine("All payments inserted successfully.");
            Console.WriteLine();
        }

        public async Task UploadPaymentsToZohoAsync()

        {


            // 1. Declare payload at the top
            object payload = null;
            var invoices = new List<InvoiceAllocation>();



            Console.WriteLine("using Token from Databasee");
            var latestToken = await _accessTokenService.LoadLatestTokenAsync(_tokenApplication, default)
                ?? throw new InvalidOperationException($"No {_tokenApplication} access token found");
            Console.WriteLine("latest token", latestToken.accessToken);

            var payments = await _repository.GetPendingPaymentsAsync();


            Console.WriteLine($"Total Pending Payments : {payments.Count}");

            foreach (var payment in payments)
            {
                var invoice = await _repository.GetInvoiceByReservationIdAsync(payment.Customer_Name!);

                if (invoice == null)
                {
                    Console.WriteLine($"Invoice not found for Reservation : {payment.Customer_Name}");
                    continue;

                }

                var customer = await _repository.GetCustomerByCustomerIdAsync(invoice.Customer_Name!);

                if (customer == null)
                {
                    Console.WriteLine($"Customer not found : {invoice.Customer_Name}");
                    continue;
                }
                else
                {
                    Console.WriteLine($"Customer found:{customer.CustomerID}");
                    // Assuming you have fetched your invoice object which contains Owner_Type
                }


                string ownerType = invoice?.Owner_Type?.Trim();

                Bank_COA? bankCoa = null;

                if (string.Equals(ownerType, "agent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ownerType, "corp", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. If owner is Agent or Corporate, run B2B function
                    bankCoa = await _repository.GetBankCoaByNameAsync_B2C(payment.Details);
                    invoices = new List<InvoiceAllocation>
                {
                    new InvoiceAllocation
                    {
                        invoice_id = invoice.BooksInvoiceID,
                        amount_applied = Convert.ToDecimal(payment.Amount_Received)
                    }
                };
                    payload = new
                    {

                        customer_id = customer.BooksID,
                        payment_mode = "Cash",
                        account_id = bankCoa.BooksID ?? "3233228000000063811",
                        amount = Convert.ToDecimal(payment.Amount_Received),
                        date = payment.Payment_Date,
                        reference_number = payment.Payment_No,
                        invoices = invoices

                    }
               ;
                }
                else if (string.Equals(ownerType, "guest", StringComparison.OrdinalIgnoreCase))
                {

                    var resolvedGuestCustomerId = "";




                    // 2. If owner is Guest, run B2C function with details
                    bankCoa = await _repository.GetBankCoaByNameAsync_B2C(payment.Details);

                    if (payment.Details.Contains("CASH"))
                    {
                        resolvedGuestCustomerId = _cashCustomerBooksId ?? string.Empty;
                    }
                    else if (payment.Details.Contains("UPI"))
                    {
                        resolvedGuestCustomerId = _upiCustomerBooksId ?? string.Empty;
                    }
                    else if (payment.Details.Contains("CARD"))
                    {
                        // Covers "Card", "Credit Card", "Debit Card" — all contain "CARD".
                        resolvedGuestCustomerId = _creditCardCustomerBooksId ?? string.Empty;
                    }



                    invoices = new List<InvoiceAllocation>
                {
                    new InvoiceAllocation
                    {
                        invoice_id = invoice.BooksInvoiceID,
                        amount_applied = Convert.ToDecimal(payment.Amount_Received)
                    }
                };
                    payload = new
                    {

                        customer_id = resolvedGuestCustomerId,
                        payment_mode = "Cash",
                        account_id = bankCoa.BooksID ?? "3233228000000063811",
                        amount = Convert.ToDecimal(payment.Amount_Received),
                        date = payment.Payment_Date,
                        reference_number = payment.Payment_No,
                        invoices = invoices

                    }
               ;
                }
                else
                {
                    // Optional: Handle any unexpected owner types (default/fallback)
                    Console.WriteLine($"Unknown Owner_Type: '{ownerType}' for payment details: {payment.Details}");
                }

                // Log if no Bank COA was retrieved
                if (bankCoa == null)
                {
                    Console.WriteLine($"Bank COA not found for: {payment.Details}");


                    //continue; // Uncomment if inside a loop
                }








                Console.WriteLine("-----------PAYLOAD-----------");
                Console.WriteLine(JsonSerializer.Serialize(payload,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                Console.WriteLine("-----------------------------");

                var json = JsonSerializer.Serialize(payload);

                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://www.zohoapis.in/books/v3/customerpayments?organization_id=60059112783");

                request.Headers.Add(
                    "Authorization",
                    $"Zoho-oauthtoken {latestToken.accessToken}");

                request.Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.SendAsync(request);

                var responseBody = await response.Content.ReadAsStringAsync();

                Console.WriteLine("-------------------------------------");
                Console.WriteLine($"Payment No : {payment.Payment_No}");
                Console.WriteLine($"Status     : {response.StatusCode}");
                Console.WriteLine($"Response   : {responseBody}");

                var zohoResponse = JsonSerializer.Deserialize<ZohoPaymentResponse>(responseBody);

                if (response.IsSuccessStatusCode &&
                    zohoResponse != null &&
                    zohoResponse.Code == 0)
                {
                    await _repository.UpdatePaymentSuccessAsync(
                        payment.Payment_No!,
                        zohoResponse.Payment?.PaymentId ?? "",
                        responseBody);

                    Console.WriteLine("Payment uploaded successfully.");
                }
                else
                {
                    await _repository.UpdatePaymentFailedAsync(
                        payment.Payment_No!,
                        responseBody);

                    Console.WriteLine("Payment upload failed.");
                }
            }
        }
    }
}


