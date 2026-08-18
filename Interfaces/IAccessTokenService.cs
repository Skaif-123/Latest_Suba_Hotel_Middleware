using AgentSyncConsole.Models;
using System.Threading.Tasks;
using System.Threading;
namespace AgentSyncConsole.Interfaces;

/// <summary>
/// Centralized authentication logic. This is the ONLY service in the solution
/// permitted to load, validate, refresh, save, or hand out an access token.
/// No other service implements token handling (per project convention).
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// Generates/loads the token for the given application once at application start
    /// and starts the single app-wide 45-minute refresh timer. Must be called exactly
    /// once from the composition root (Program.cs) before the token is used.
    /// </summary>
    Task InitializeAsync(string application, CancellationToken ct = default);

    /// <summary>Loads the latest token row for the given application, refreshing it first if expired.</summary>
    Task<string> GetValidAccessTokenAsync(string application, CancellationToken ct = default);

    /// <summary>Loads the most recent token row as-is, without validating/refreshing expiry.</summary>
    Task<AccessTokenRecord?> LoadLatestTokenAsync(string application, CancellationToken ct = default);

    /// <summary>Returns true if the given token record is expired or about to expire within the configured skew.</summary>
    bool IsExpired(AccessTokenRecord token);

    /// <summary>Refreshes the token via the OAuth refresh-token grant and persists the new value.</summary>
    Task<AccessTokenRecord> RefreshAsync(AccessTokenRecord currentToken, CancellationToken ct = default);
}
