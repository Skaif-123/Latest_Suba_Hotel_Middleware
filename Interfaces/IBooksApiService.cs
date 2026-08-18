using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
namespace AgentSyncConsole.Interfaces;

/// <summary>All outbound HTTP interaction with the Zoho Books v3 API.</summary>
public interface IBooksApiService
{
    Task<BooksApiResponse> GetInvoiceAsync(string accessToken, string booksInvoiceId, CancellationToken ct = default);
    Task<BooksApiResponse> CreateInvoiceAsync(string accessToken, BooksInvoicePayload payload, CancellationToken ct = default);
    Task<BooksApiResponse> UpdateInvoiceAsync(string accessToken, string booksInvoiceId, BooksInvoicePayload payload, CancellationToken ct = default);
    Task<BooksApiResponse> MarkInvoiceAsSentAsync(string accessToken, string booksInvoiceId, CancellationToken ct = default);
}