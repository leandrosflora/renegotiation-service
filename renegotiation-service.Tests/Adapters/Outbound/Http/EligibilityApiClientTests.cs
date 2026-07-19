using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using renegotiation_service.Adapters.Outbound.Http;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Tests.Testing;
using Xunit;

namespace renegotiation_service.Tests.Adapters.Outbound.Http;

public class EligibilityApiClientTests
{
    [Fact]
    public async Task CheckEligibilityAsync_Eligible_ReturnsEligibleTrue()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new EligibilityResult(true, null))
        });
        var client = BuildClient(handler);

        var result = await client.CheckEligibilityAsync("contract-1", CancellationToken.None);

        Assert.True(result.Eligible);
    }

    [Fact]
    public async Task CheckEligibilityAsync_NotEligible_ReturnsEligibleFalseWithReason()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new EligibilityResult(false, "overdue_too_long"))
        });
        var client = BuildClient(handler);

        var result = await client.CheckEligibilityAsync("contract-1", CancellationToken.None);

        Assert.False(result.Eligible);
        Assert.Equal("overdue_too_long", result.Reason);
    }

    [Fact]
    public async Task CheckEligibilityAsync_TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new EligibilityResult(true, null)) });
        var client = BuildClient(handler, maxRetryAttempts: 2);

        var result = await client.CheckEligibilityAsync("contract-1", CancellationToken.None);

        Assert.True(result.Eligible);
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task CheckEligibilityAsync_Unreachable_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CheckEligibilityAsync("contract-1", CancellationToken.None));
    }

    private static IEligibilityApiClient BuildClient(StubHttpMessageHandler handler, int maxRetryAttempts = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpClientBuilder = services.AddHttpClient<IEligibilityApiClient, EligibilityApiClient>(client =>
        {
            client.BaseAddress = new Uri("http://localhost/");
        });
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => handler);

        if (maxRetryAttempts > 0)
        {
            httpClientBuilder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = maxRetryAttempts;
                options.Retry.Delay = TimeSpan.FromMilliseconds(10);
                options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.MinimumThroughput = int.MaxValue;
            });
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IEligibilityApiClient>();
    }
}
