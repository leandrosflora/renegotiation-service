using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using renegotiation_service.Adapters.Outbound.Http;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Tests.Testing;
using Xunit;

namespace renegotiation_service.Tests.Adapters.Outbound.Http;

public class ClientApiClientTests
{
    [Fact]
    public async Task GetClientAsync_Found_ReturnsClientData()
    {
        var expected = new ClientData("12345678900", "Maria");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });
        var client = BuildClient(handler);

        var result = await client.GetClientAsync("12345678900", CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetClientAsync_NotFound_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = BuildClient(handler);

        var result = await client.GetClientAsync("00000000000", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetClientAsync_TransientFailureThenSuccess_RetriesAndReturnsData()
    {
        var expected = new ClientData("12345678900", "Maria");
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });
        var client = BuildClient(handler, maxRetryAttempts: 2);

        var result = await client.GetClientAsync("12345678900", CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task GetClientAsync_Unreachable_ThrowsAfterRetries()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetClientAsync("12345678900", CancellationToken.None));
    }

    private static IClientApiClient BuildClient(StubHttpMessageHandler handler, int maxRetryAttempts = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpClientBuilder = services.AddHttpClient<IClientApiClient, ClientApiClient>(client =>
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
        return provider.GetRequiredService<IClientApiClient>();
    }
}
