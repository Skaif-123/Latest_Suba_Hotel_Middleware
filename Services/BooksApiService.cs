using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Net.Http;

namespace AgentSyncConsole.Services;

/// <summary>
/// Converted from the "booksRequest" HTTP helper plus every inline https.request(...)
/// call in index.js (GET contact, GET item, GET invoice, POST/PUT invoice).
/// Same host, same org query param, same "Zoho-oauthtoken" auth scheme, same
/// best-effort JSON parse (falls back to raw body instead of throwing).
/// </summary>
public class BooksApiService : IBooksApiService
{
    private readonly HttpClient _http;
    private readonly IRetryService _retry;
    private readonly ILogger<BooksApiService> _logger;
    private readonly string _organizationId;
    private readonly string _apiBasePath;

    public BooksApiService(HttpClient http, IRetryService retry, IConfiguration configuration, ILogger<BooksApiService> logger)
    {
        _http = http;
        _retry = retry;
        _logger = logger;
        _organizationId = configuration["ZohoBooks:OrganizationId"]
            ?? throw new InvalidOperationException("Missing ZohoBooks:OrganizationId");
        _apiBasePath = configuration["ZohoBooks:ApiBasePath"] ?? "/books/v3";

        var host = configuration["ZohoBooks:ApiHost"] ?? "www.zohoapis.in";
        Console.WriteLine(host is null
            ? "Using default Zoho Books API host: www.zohoapis.in"
            : $"Using configured Zoho Books API host: {host}");
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri($"https://{host}");
        }
    }

    public Task<BooksApiResponse> GetInvoiceAsync(string accessToken, string booksInvoiceId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"{_apiBasePath}/invoices/{booksInvoiceId}?organization_id={_organizationId}", accessToken, null, ct);

    public Task<BooksApiResponse> CreateInvoiceAsync(string accessToken, BooksInvoicePayload payload, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"{_apiBasePath}/invoices?organization_id={_organizationId}", accessToken, payload, ct);

    public Task<BooksApiResponse> UpdateInvoiceAsync(string accessToken, string booksInvoiceId, BooksInvoicePayload payload, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"{_apiBasePath}/invoices/{booksInvoiceId}?organization_id={_organizationId}", accessToken, payload, ct);

    // Marks a (draft) invoice as "sent" in Zoho Books.
    // POST /books/v3/invoices/{invoice_id}/status/sent?organization_id={org_id}, no body required.
    public Task<BooksApiResponse> MarkInvoiceAsSentAsync(string accessToken, string booksInvoiceId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"{_apiBasePath}/invoices/{booksInvoiceId}/status/sent?organization_id={_organizationId}", accessToken, null, ct);

    private async Task<BooksApiResponse> SendAsync(HttpMethod method, string path, string accessToken, BooksInvoicePayload? payload, CancellationToken ct)
    {
        return await _retry.ExecuteAsync(async () =>
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation("Authorization", $"Zoho-oauthtoken {accessToken}");

            if (payload is not null)
            {
                var json = JsonHelper.Serialize(payload);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                Console.WriteLine($"Sending {method} {path} with payload: {json}");
            }

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("BOOKS API {Method} {Path} => {Body}", method, path, body);

            try
            {
                var parsed = JsonSerializer.Deserialize<BooksApiResponse>(body, JsonHelper.Options) ?? new BooksApiResponse();
                parsed.RawBody = body;
                Console.WriteLine($"Response for {method} {path}: {parsed.RawBody}");
                return parsed;
            }
            catch (JsonException)
            {
                Console.WriteLine($"Failed to parse response for {method} {path}: {body}");
                // Mirrors the Catalyst booksRequest fallback: unparsable body becomes { _rawBody: body }
                return new BooksApiResponse { RawBody = body };
            }
        }, $"BooksApi {method} {path}", ct);
    }
}