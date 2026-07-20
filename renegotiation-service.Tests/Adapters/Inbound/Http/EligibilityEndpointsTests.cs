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

public class EligibilityEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public EligibilityEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task GetEligibility_Eligible_ReturnsOk()
    {
        var client = new Mock<IEligibilityApiClient>();
        client.Setup(c => c.CheckEligibilityAsync("contract-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EligibilityResult(true, null));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/contracts/contract-1/eligibility");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EligibilityResult>();
        Assert.True(body!.Eligible);
    }

    [Fact]
    public async Task GetEligibility_NotEligible_ReturnsOkWithReason()
    {
        var client = new Mock<IEligibilityApiClient>();
        client.Setup(c => c.CheckEligibilityAsync("contract-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EligibilityResult(false, "overdue_too_long"));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/contracts/contract-1/eligibility");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EligibilityResult>();
        Assert.False(body!.Eligible);
        Assert.Equal("overdue_too_long", body.Reason);
    }

    [Fact]
    public async Task GetEligibility_ApiUnavailable_ReturnsBadGateway()
    {
        var client = new Mock<IEligibilityApiClient>();
        client.Setup(c => c.CheckEligibilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/contracts/contract-1/eligibility");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private HttpClient CreateClient(IEligibilityApiClient eligibilityApiClient)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            TestAuth.ConfigureInboundSecret(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEligibilityApiClient>();
                services.AddSingleton(eligibilityApiClient);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestAuth.TenantId);
        return client;
    }
}
