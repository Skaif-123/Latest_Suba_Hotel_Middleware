using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentSyncConsole.CustomerBooks.Configuration;
using AgentSyncConsole.CustomerBooks.Interfaces;

namespace AgentSyncConsole.CustomerBooks.Services
{
    /// <summary>
    /// 15s request timeout, retry on 429/500/502/503/504 and on
    /// timeout/connection-reset errors, attempt*1000ms backoff, capped at
    /// MaxRetries attempts. POST vs PUT and URL construction are handled by
    /// the two public methods below.
    /// </summary>
    public class ZohoBooksApiClient : IZohoBooksApiClient
    {
        private static readonly HashSet<int> RetryableStatusCodes = new() { 429, 500, 502, 503, 504 };

        private readonly HttpClient _httpClient;
        private readonly CustomerBooksSettings _options;
        private readonly ILogger<ZohoBooksApiClient> _logger;

        public ZohoBooksApiClient(HttpClient httpClient, IOptions<CustomerBooksSettings> options, ILogger<ZohoBooksApiClient> logger)
        {
            _options = options.Value;
            _logger = logger;

            httpClient.BaseAddress ??= new Uri($"https://{_options.Hostname}");
            // Per-attempt timeout is enforced manually below so it composes with
            // the retry loop; disable HttpClient's own end-to-end timeout.
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            _httpClient = httpClient;
        }

        public Task<BooksApiResponse> CreateOrUpdateContactAsync(string accessToken, string? existingBooksId, JsonObject payload, CancellationToken ct = default)
        {
            var hasId = !string.IsNullOrWhiteSpace(existingBooksId);
            var method = hasId ? HttpMethod.Put : HttpMethod.Post;
            var path = hasId
                ? $"/books/v3/contacts/{existingBooksId}?organization_id={_options.OrganizationId}"
                : $"/books/v3/contacts?organization_id={_options.OrganizationId}";

            return SendWithRetryAsync(method, path, accessToken, payload.ToJsonString(), ct);
        }

        public Task<BooksApiResponse> CreateOrUpdateTaxInfoAsync(string accessToken, string contactId, string? existingTaxInfoId, JsonObject payload, CancellationToken ct = default)
        {
            var hasId = !string.IsNullOrWhiteSpace(existingTaxInfoId);
            var method = hasId ? HttpMethod.Put : HttpMethod.Post;
            var path = hasId
                ? $"/books/v3/contacts/{contactId}/taxinfo/{existingTaxInfoId}?organization_id={_options.OrganizationId}"
                : $"/books/v3/contacts/{contactId}/taxinfo?organization_id={_options.OrganizationId}";

            return SendWithRetryAsync(method, path, accessToken, payload.ToJsonString(), ct);
        }

        private async Task<BooksApiResponse> SendWithRetryAsync(
            HttpMethod method, string path, string accessToken, string body, CancellationToken ct, int attempt = 1)
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", accessToken);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.RequestTimeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                using var response = await _httpClient.SendAsync(request, linkedCts.Token);

                if (attempt < _options.MaxRetries && RetryableStatusCodes.Contains((int)response.StatusCode))
                {
                    _logger.LogWarning(
                        "Books API returned retryable status {StatusCode} (attempt {Attempt} of {MaxRetries}), retrying...",
                        (int)response.StatusCode, attempt, _options.MaxRetries);

                    await Task.Delay(attempt * 1000, ct);
                    return await SendWithRetryAsync(method, path, accessToken, body, ct, attempt + 1);
                }

                var raw = await response.Content.ReadAsStringAsync(ct);
                return new BooksApiResponse
                {
                    IsSuccessStatusCode = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    RawBody = raw
                };
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && attempt < _options.MaxRetries)
            {
                _logger.LogWarning("Books API request timed out (attempt {Attempt} of {MaxRetries}), retrying...", attempt, _options.MaxRetries);
                await Task.Delay(attempt * 1000, ct);
                return await SendWithRetryAsync(method, path, accessToken, body, ct, attempt + 1);
            }
            catch (HttpRequestException ex) when (attempt < _options.MaxRetries)
            {
                _logger.LogWarning(ex, "Books API request failed (attempt {Attempt} of {MaxRetries}), retrying...", attempt, _options.MaxRetries);
                await Task.Delay(attempt * 1000, ct);
                return await SendWithRetryAsync(method, path, accessToken, body, ct, attempt + 1);
            }
        }
    }
}
