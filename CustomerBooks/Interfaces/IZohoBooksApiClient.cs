using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentSyncConsole.CustomerBooks.Interfaces
{
    public class BooksApiResponse
    {
        public bool IsSuccessStatusCode { get; set; }
        public int StatusCode { get; set; }
        public string RawBody { get; set; } = string.Empty;
    }

    /// <summary>
    /// NOTE: this is a distinct client from AgentSyncConsole.Interfaces.
    /// IBooksApiService (which handles Invoice GET/POST/PUT for the Books
    /// Invoice Sync module). This one only ever touches the /contacts and
    /// /contacts/{id}/taxinfo sub-resources, so it is kept as its own
    /// namespaced client rather than bolted onto the existing invoice client.
    /// </summary>
    public interface IZohoBooksApiClient
    {
        /// <summary>POST when existingBooksId is null/empty, PUT when it already holds a Contact ID.</summary>
        Task<BooksApiResponse> CreateOrUpdateContactAsync(string accessToken, string? existingBooksId, JsonObject payload, CancellationToken ct = default);

        /// <summary>POST when existingTaxInfoId is null/empty, PUT when it already holds a Tax Information ID.</summary>
        Task<BooksApiResponse> CreateOrUpdateTaxInfoAsync(string accessToken, string contactId, string? existingTaxInfoId, JsonObject payload, CancellationToken ct = default);
    }
}
