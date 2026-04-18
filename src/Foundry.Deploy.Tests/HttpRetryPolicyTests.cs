using System.Net;
using Foundry.Deploy.Services.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class HttpRetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_WhenFailureIsTransient_RetriesUntilSuccess()
    {
        int attempts = 0;

        string result = await HttpRetryPolicy.ExecuteAsync(
            async _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new HttpRequestException("Transient failure", null, HttpStatusCode.ServiceUnavailable);
                }

                await Task.CompletedTask;
                return "ok";
            },
            NullLogger.Instance,
            "download catalog",
            retryCount: 3,
            retryDelay: TimeSpan.Zero);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFailureIsNotRetryable_DoesNotRetry()
    {
        int attempts = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HttpRetryPolicy.ExecuteAsync(
                _ =>
                {
                    attempts++;
                    throw new HttpRequestException("Not found", null, HttpStatusCode.NotFound);
                },
                NullLogger.Instance,
                "download catalog",
                retryCount: 3,
                retryDelay: TimeSpan.Zero));

        Assert.Equal(1, attempts);
    }
}
