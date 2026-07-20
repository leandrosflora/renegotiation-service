using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Headers;
using Moq;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Tests.Testing;
using Xunit;

namespace renegotiation_service.Tests.Adapters.Inbound.Http;

public class SimulationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public SimulationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task PostSimulation_Possible_ReturnsOkWithSimulationId()
    {
        var client = new Mock<IContractingApiClient>();
        client.Setup(c => c.SimulateAsync("contract-1", It.IsAny<SimulationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationResult(true, null, new SimulationData("sim-1", 12, 100m, 1200m)));
        var httpClient = CreateClient(client.Object);
        AuthorizeSimulation(httpClient, "idem-possible-1");

        var response = await httpClient.PostAsJsonAsync(
            "/contracts/contract-1/simulations", new { installments = 12, discount_percentage = 0.0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SimulationResult>();
        Assert.True(body!.Possible);
        Assert.Equal("sim-1", body.Simulation!.SimulationId);
    }

    [Fact]
    public async Task PostSimulation_NotPossible_ReturnsOkWithReason()
    {
        var client = new Mock<IContractingApiClient>();
        client.Setup(c => c.SimulateAsync(It.IsAny<string>(), It.IsAny<SimulationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationResult(false, "installments_out_of_range", null));
        var httpClient = CreateClient(client.Object);
        AuthorizeSimulation(httpClient, "idem-not-possible-1");

        var response = await httpClient.PostAsJsonAsync(
            "/contracts/contract-1/simulations", new { installments = 999, discount_percentage = 0.0 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SimulationResult>();
        Assert.False(body!.Possible);
        Assert.Equal("installments_out_of_range", body.Reason);
    }

    [Fact]
    public async Task PostSimulation_ApiUnavailable_ReturnsBadGateway()
    {
        var client = new Mock<IContractingApiClient>();
        client.Setup(c => c.SimulateAsync(It.IsAny<string>(), It.IsAny<SimulationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var httpClient = CreateClient(client.Object);
        AuthorizeSimulation(httpClient, "idem-unavailable-1");

        var response = await httpClient.PostAsJsonAsync(
            "/contracts/contract-1/simulations", new { installments = 12, discount_percentage = 0.0 });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private static void AuthorizeSimulation(HttpClient httpClient, string idempotencyKey)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestAuth.IssueGovernedToolToken("simular_proposta", "ContractSelected", idempotencyKey));
        httpClient.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
    }

    private HttpClient CreateClient(IContractingApiClient contractingApiClient)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            TestAuth.ConfigureSigningKey(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IContractingApiClient>();
                services.AddSingleton(contractingApiClient);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestAuth.TenantId);
        return client;
    }
}
