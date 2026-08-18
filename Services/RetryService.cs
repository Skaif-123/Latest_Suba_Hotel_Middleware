using AgentSyncConsole.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
namespace AgentSyncConsole.Services;

/// <summary>
/// Simple exponential-backoff retry wrapper. The original Catalyst function had no
/// explicit retry loop around its HTTP calls; this centralizes retry so every
/// outbound Books API call in the .NET solution gets consistent resiliency
/// without changing any business outcome (success/failure codes are unaffected).
/// </summary>
public class RetryService : IRetryService
{
    private readonly int _maxAttempts;
    private readonly int _baseDelayMs;
    private readonly ILogger<RetryService> _logger;

    public RetryService(IConfiguration configuration, ILogger<RetryService> logger)
    {
        _maxAttempts = configuration.GetValue<int?>("RetryPolicy:MaxAttempts") ?? 3;
        _baseDelayMs = configuration.GetValue<int?>("RetryPolicy:BaseDelayMilliseconds") ?? 500;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName, CancellationToken ct = default)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < _maxAttempts)
            {
                lastException = ex;
                var delay = _baseDelayMs * (int)Math.Pow(2, attempt - 1);
                _logger.LogWarning(ex, "{Operation} failed on attempt {Attempt}/{MaxAttempts}, retrying in {Delay}ms",
                    operationName, attempt, _maxAttempts, delay);
                await Task.Delay(delay, ct);
            }
        }

        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Operation} failed after {MaxAttempts} attempts", operationName, _maxAttempts);
            throw new AggregateException($"{operationName} failed after {_maxAttempts} attempts", lastException ?? ex);
        }
    }

    public async Task ExecuteAsync(Func<Task> action, string operationName, CancellationToken ct = default)
    {
        await ExecuteAsync(async () => { await action(); return true; }, operationName, ct);
    }
}
