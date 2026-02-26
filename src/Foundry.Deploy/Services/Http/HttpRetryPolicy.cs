using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Http;

public static class HttpRetryPolicy
{
    public const int DefaultRetryCount = 5;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(10);

    public static Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default,
        int retryCount = DefaultRetryCount,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        return ExecuteAsync(
            async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return true;
            },
            logger,
            operationName,
            cancellationToken,
            retryCount,
            retryDelay);
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default,
        int retryCount = DefaultRetryCount,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);

        TimeSpan delay = retryDelay ?? DefaultRetryDelay;
        int totalAttempts = retryCount + 1;
        int attempt = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt <= retryCount && IsRetryable(ex, cancellationToken))
            {
                logger.LogWarning(
                    ex,
                    "HTTP operation {OperationName} failed on attempt {Attempt}/{TotalAttempts}. Retrying in {DelaySeconds} seconds.",
                    operationName,
                    attempt,
                    totalAttempts,
                    delay.TotalSeconds);

                attempt++;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        if (exception is HttpRequestException httpRequestException)
        {
            return httpRequestException.StatusCode is null ||
                   httpRequestException.StatusCode is HttpStatusCode.RequestTimeout ||
                   httpRequestException.StatusCode is HttpStatusCode.TooManyRequests ||
                   httpRequestException.StatusCode is HttpStatusCode.InternalServerError ||
                   httpRequestException.StatusCode is HttpStatusCode.BadGateway ||
                   httpRequestException.StatusCode is HttpStatusCode.ServiceUnavailable ||
                   httpRequestException.StatusCode is HttpStatusCode.GatewayTimeout;
        }

        return exception is IOException || exception is TaskCanceledException;
    }
}
