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

public class ClientLookupEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ClientLookupEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task GetClient_Found_ReturnsOkWithClientData()
    {
        var client = new Mock<IClientApiClient>();
        client.Setup(c => c.GetClientAsync("12345678900", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientData("12345678900", "Maria"));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/clients/12345678900");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClientLookupResult>();
        Assert.True(body!.Found);
        Assert.Equal("Maria", body.Client!.Name);
    }

    [Fact]
    public async Task GetClient_NotFound_ReturnsOkWithFoundFalse()
    {
        var client = new Mock<IClientApiClient>();
        client.Setup(c => c.GetClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientData?)null);
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/clients/00000000000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClientLookupResult>();
        Assert.False(body!.Found);
        Assert.Null(body.Client);
    }

    [Fact]
    public async Task GetClient_ClientApiUnavailable_ReturnsBadGateway()
    {
        var client = new Mock<IClientApiClient>();
        client.Setup(c => c.GetClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/clients/12345678900");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task GetContracts_Found_ReturnsOkWithContracts()
    {
        var client = new Mock<IClientApiClient>();
        client.Setup(c => c.GetContractsAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContractSummary> { new("contract-1", "loan", 1000m) });
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/clients/client-1/contracts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContractsResult>();
        Assert.True(body!.Found);
        Assert.Single(body.Contracts);
    }

    [Fact]
    public async Task GetDebts_Found_ReturnsOkWithDebts()
    {
        var client = new Mock<IClientApiClient>();
        client.Setup(c => c.GetDebtsAsync("contract-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DebtItem> { new("debt-1", 500m, DateOnly.FromDateTime(DateTime.Today), 10) });
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/contracts/contract-1/debts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DebtsResult>();
        Assert.True(body!.Found);
        Assert.Single(body.Debts);
    }

    private HttpClient CreateClient(IClientApiClient clientApiClient)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            TestAuth.ConfigureSigningKey(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClientApiClient>();
                services.AddSingleton(clientApiClient);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestAuth.TenantId);
        return client;
    }
}
