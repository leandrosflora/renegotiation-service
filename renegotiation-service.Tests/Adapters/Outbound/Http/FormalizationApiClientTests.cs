using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using renegotiation_service.Adapters.Outbound.Http;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Tests.Testing;
using Xunit;

namespace renegotiation_service.Tests.Adapters.Outbound.Http;

public class FormalizationApiClientTests
{
    [Fact]
    public async Task ConfirmAsync_Success_ReturnsAgreementId()
    {
        var expected = new AgreementConfirmationResult(true, null, new AgreementData("agr-1"));
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });
        var client = BuildClient(handler);

        var result = await client.ConfirmAsync("sim-1", "idem-1", CancellationToken.None);

        Assert.True(result.Confirmed);
        Assert.Equal("agr-1", result.Agreement!.AgreementId);
    }

    [Fact]
    public async Task ConfirmAsync_NotPossible_ReturnsConfirmedFalseWithReason()
    {
        var expected = new AgreementConfirmationResult(false, "simulation_expired", null);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });
        var client = BuildClient(handler);

        var result = await client.ConfirmAsync("sim-1", "idem-1", CancellationToken.None);

        Assert.False(result.Confirmed);
        Assert.Equal("simulation_expired", result.Reason);
    }

    [Fact]
    public async Task ConfirmAsync_Unreachable_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ConfirmAsync("sim-1", "idem-1", CancellationToken.None));
    }

    [Fact]
    public async Task GetDocumentAsync_Success_ReturnsDocumentUrl()
    {
        var expected = new DocumentResult(true, null, "http://docs/agr-1.pdf");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        });
        var client = BuildClient(handler);

        var result = await client.GetDocumentAsync("agr-1", CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("http://docs/agr-1.pdf", result.DocumentUrl);
    }

    [Fact]
    public async Task GetDocumentAsync_TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var expected = new DocumentResult(true, null, "http://docs/agr-1.pdf");
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });
        var client = BuildClient(handler, maxRetryAttempts: 2);

        var result = await client.GetDocumentAsync("agr-1", CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task GetDocumentAsync_Unreachable_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetDocumentAsync("agr-1", CancellationToken.None));
    }

    private static IFormalizationApiClient BuildClient(StubHttpMessageHandler handler, int maxRetryAttempts = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpClientBuilder = services.AddHttpClient<IFormalizationApiClient, FormalizationApiClient>(client =>
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
        return provider.GetRequiredService<IFormalizationApiClient>();
    }
}
