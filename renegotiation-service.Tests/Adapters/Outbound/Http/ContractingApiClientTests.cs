using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using renegotiation_service.Adapters.Outbound.Http;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Tests.Testing;
using Xunit;

namespace renegotiation_service.Tests.Adapters.Outbound.Http;

public class ContractingApiClientTests
{
    [Fact]
    public async Task SimulateAsync_Success_ReturnsSimulation()
    {
        var expected = new SimulationResult(true, null, new SimulationData("sim-1", 12, 100m, 1200m));
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });
        var client = BuildClient(handler);

        var result = await client.SimulateAsync(
            "contract-1", new SimulationRequest { Installments = 12, DiscountPercentage = 0 }, "idem-key-1", CancellationToken.None);

        Assert.True(result.Possible);
        Assert.Equal("sim-1", result.Simulation!.SimulationId);
    }

    [Fact]
    public async Task SimulateAsync_NotPossible_ReturnsPossibleFalseWithReason()
    {
        var expected = new SimulationResult(false, "installments_out_of_range", null);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });
        var client = BuildClient(handler);

        var result = await client.SimulateAsync(
            "contract-1", new SimulationRequest { Installments = 999, DiscountPercentage = 0 }, "idem-key-2", CancellationToken.None);

        Assert.False(result.Possible);
        Assert.Equal("installments_out_of_range", result.Reason);
    }

    [Fact]
    public async Task SimulateAsync_TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var expected = new SimulationResult(true, null, new SimulationData("sim-1", 12, 100m, 1200m));
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });
        var client = BuildClient(handler, maxRetryAttempts: 2);

        var result = await client.SimulateAsync(
            "contract-1", new SimulationRequest { Installments = 12, DiscountPercentage = 0 }, "idem-key-3", CancellationToken.None);

        Assert.True(result.Possible);
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task SimulateAsync_Unreachable_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SimulateAsync(
            "contract-1", new SimulationRequest { Installments = 12, DiscountPercentage = 0 }, "idem-key-4", CancellationToken.None));
    }

    private static IContractingApiClient BuildClient(StubHttpMessageHandler handler, int maxRetryAttempts = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpClientBuilder = services.AddHttpClient<IContractingApiClient, ContractingApiClient>(client =>
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
        return provider.GetRequiredService<IContractingApiClient>();
    }
}
