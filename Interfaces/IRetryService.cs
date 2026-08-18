using System.Threading.Tasks;
using System.Threading;
using System;

namespace AgentSyncConsole.Interfaces;

/// <summary>Generic retry-with-backoff wrapper used by outbound API calls.</summary>
public interface IRetryService
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName, CancellationToken ct = default);
    Task ExecuteAsync(Func<Task> action, string operationName, CancellationToken ct = default);
}
