using System.Text.Json;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace AgentSyncConsole.Services;

/// <summary>
/// Centralized authentication logic — the ONLY place in the solution that loads,
/// validates, refreshes, or saves an OAuth token. Every other service must call
/// GetValidAccessTokenAsync() rather than touching the accesToken table directly.
///
/// The token *read* path (latest row by application, ordered by CREATEDTIME desc)
/// is converted 1:1 from index.js's zcql query in the Books invoice function.
/// The refresh-token grant call itself was not part of the uploaded
/// CatalystToBooksInvoices function (that function only ever reads the latest
/// row); it is implemented here against the standard Zoho OAuth token endpoint
/// so the service fulfils the "refresh + save" responsibilities requested for
/// the (not yet uploaded) accessToken Catalyst function. Replace/verify against
/// that function's real source once it is uploaded.
/// </summary>
public class AccessTokenService : IAccessTokenService, IDisposable
{
    private readonly IAccessTokenRepository _repository;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccessTokenService> _logger;
    private readonly int _skewSeconds;
    private readonly int _refreshIntervalMinutes;

    // Single app-wide refresh process: one cached token + one timer + one lock,
    // shared by every consumer since this service is registered as a singleton.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AccessTokenRecord? _cachedToken;
    private Timer? _refreshTimer;
    private string? _application;

    public AccessTokenService(
        IAccessTokenRepository repository,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AccessTokenService> logger)
    {
        _repository = repository;
        _http = httpClientFactory.CreateClient("ZohoAccounts");
        _configuration = configuration;
        _logger = logger;
        _skewSeconds = configuration.GetValue<int?>("ZohoAuth:TokenRefreshSkewSeconds") ?? 120;
        _refreshIntervalMinutes = configuration.GetValue<int?>("ZohoAuth:TokenRefreshIntervalMinutes") ?? 45;
    }

    public async Task InitializeAsync(string application, CancellationToken ct = default)
    {
        _application = application;

        var token = await _repository.GetLatestAsync(application, ct)
            ?? throw new InvalidOperationException($"No {application} access token found");

        if (IsExpired(token))
        {
            token = await RefreshAsync(token, ct);
        }

        _cachedToken = token;
        _logger.LogInformation("Token initialized on startup => tokenROWID={RowId}", token.ROWID);

        // ONE timer for the whole application, firing every _refreshIntervalMinutes.
        _refreshTimer ??= new Timer(
            async _ => await RefreshOnTimerAsync(),
            null,
            TimeSpan.FromMinutes(_refreshIntervalMinutes),
            TimeSpan.FromMinutes(_refreshIntervalMinutes));
    }

    private async Task RefreshOnTimerAsync()
    {
        try
        {
            if (_cachedToken is null || _application is null) return;
            _cachedToken = await RefreshAsync(_cachedToken, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Previous token (_cachedToken) is left untouched on failure.
            _logger.LogError(ex, "Scheduled {Application} token refresh failed — keeping previous token", _application);
        }
    }

    public Task<AccessTokenRecord?> LoadLatestTokenAsync(string application, CancellationToken ct = default)
    {
        // Return the in-memory (already refreshed) token instead of re-querying the DB,
        // so callers always see the latest token, not a row picked only by CreatedTime.
        if (_cachedToken is not null && string.Equals(_application, application, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<AccessTokenRecord?>(_cachedToken);
        }

        return LoadFromDbAsync(application, ct);
    }

    private async Task<AccessTokenRecord?> LoadFromDbAsync(string application, CancellationToken ct)
    {
        var token = await _repository.GetLatestAsync(application, ct);
        if (token is null)
        {
            _logger.LogError("No {Application} access token found", application);
            return null;
        }

        _logger.LogInformation("Token loaded => tokenROWID={RowId}, tokenCreatedTime={CreatedTime}", token.ROWID, token.CREATEDTIME);
        return token;
    }

    public bool IsExpired(AccessTokenRecord token)
    {
        if (token.expiresAt is null) return true;
        return token.expiresAt.Value <= DateTime.UtcNow.AddSeconds(_skewSeconds);
    }

    public async Task<string> GetValidAccessTokenAsync(string application, CancellationToken ct = default)
    {
        var token = await LoadLatestTokenAsync(application, ct)
            ?? throw new InvalidOperationException($"No {application} access token found");

        if (IsExpired(token))
        {
            _logger.LogInformation("{Application} token expired or expiring soon — refreshing", application);
            token = await RefreshAsync(token, ct);
        }

        var accessToken = (token.accessToken ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException($"No {application} access token found");
        }

        return accessToken;
    }

    public async Task<AccessTokenRecord> RefreshAsync(AccessTokenRecord currentToken, CancellationToken ct = default)
    {
        // Prevent concurrent refreshes (startup + timer + any direct caller racing together).
        await _refreshLock.WaitAsync(ct);
        try
        {
            var accountsHost = _configuration["ZohoAuth:AccountsHost"] ?? "accounts.zoho.in";
            var clientId = _configuration["ZohoAuth:ClientId"];
            var clientSecret = _configuration["ZohoAuth:ClientSecret"];
            var refreshToken = currentToken.refreshToken ?? _configuration["ZohoAuth:RefreshToken"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogError("Cannot refresh token: ZohoAuth:ClientId / ClientSecret / RefreshToken are not configured. Keeping previous token.");
                return currentToken;
            }

            var requestUri = $"https://{accountsHost}/oauth/v2/token" +
                              $"?refresh_token={Uri.EscapeDataString(refreshToken)}" +
                              $"&client_id={Uri.EscapeDataString(clientId)}" +
                              $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
                              $"&grant_type=refresh_token";

            AccessTokenRecord updated;
            try
            {
                using var response = await _http.PostAsync(requestUri, content: null, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("access_token", out var accessTokenElement))
                {
                    // Keep the previous token if refresh fails.
                    _logger.LogError("Token refresh failed => {Body}. Keeping previous token.", body);
                    return currentToken;
                }

                var expiresInSeconds = root.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt32() : 3600;

                updated = new AccessTokenRecord
                {
                    ROWID = currentToken.ROWID,
                    application = currentToken.application,
                    accessToken = accessTokenElement.GetString(),
                    refreshToken = refreshToken,
                    expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds),
                    CREATEDTIME = currentToken.CREATEDTIME
                };
            }
            catch (Exception ex)
            {
                // Keep the previous token if refresh fails (network/parse errors, etc.).
                _logger.LogError(ex, "Token refresh threw an exception. Keeping previous token.");
                return currentToken;
            }

            // ROWID > 0 updates the existing Books row in place — never inserts a duplicate.
            await _repository.SaveAsync(updated, ct);
            _cachedToken = updated;
            _logger.LogInformation("Token refreshed and saved => tokenROWID={RowId}", updated.ROWID);

            return updated;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshTimer?.Dispose();
}
