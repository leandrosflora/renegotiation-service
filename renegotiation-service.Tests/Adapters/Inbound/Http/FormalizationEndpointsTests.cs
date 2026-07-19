using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Tests.Testing;
using Xunit;

namespace renegotiation_service.Tests.Adapters.Inbound.Http;

public class FormalizationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public FormalizationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task PostConfirmation_Success_ReturnsOkWithAgreementId()
    {
        var client = new Mock<IFormalizationApiClient>();
        client.Setup(c => c.ConfirmAsync("sim-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgreementConfirmationResult(true, null, new AgreementData("agr-1")));
        var httpClient = CreateClient(client.Object);

        var response = await PostConfirmationAsync(httpClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AgreementConfirmationResult>();
        Assert.True(body!.Confirmed);
        Assert.Equal("agr-1", body.Agreement!.AgreementId);
    }

    [Fact]
    public async Task PostConfirmation_NotPossible_ReturnsOkWithReason()
    {
        var client = new Mock<IFormalizationApiClient>();
        client.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgreementConfirmationResult(false, "simulation_expired", null));
        var httpClient = CreateClient(client.Object);

        var response = await PostConfirmationAsync(httpClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AgreementConfirmationResult>();
        Assert.False(body!.Confirmed);
        Assert.Equal("simulation_expired", body.Reason);
    }

    [Fact]
    public async Task PostConfirmation_ApiUnavailable_ReturnsBadGateway()
    {
        var client = new Mock<IFormalizationApiClient>();
        client.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var httpClient = CreateClient(client.Object);

        var response = await PostConfirmationAsync(httpClient);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmation_MissingIdempotencyKey_ReturnsBadRequest()
    {
        var client = new Mock<IFormalizationApiClient>();
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.PostAsync("/simulations/sim-1/confirmations", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostConfirmationAsync(HttpClient httpClient)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/simulations/sim-1/confirmations");
        request.Headers.Add("Idempotency-Key", "idem-1");
        return httpClient.SendAsync(request);
    }

    [Fact]
    public async Task GetDocument_Success_ReturnsOkWithDocumentUrl()
    {
        var client = new Mock<IFormalizationApiClient>();
        client.Setup(c => c.GetDocumentAsync("agr-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentResult(true, null, "http://docs/agr-1.pdf"));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/agreements/agr-1/document");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentResult>();
        Assert.True(body!.Available);
        Assert.Equal("http://docs/agr-1.pdf", body.DocumentUrl);
    }

    [Fact]
    public async Task GetDocument_ApiUnavailable_ReturnsBadGateway()
    {
        var client = new Mock<IFormalizationApiClient>();
        client.Setup(c => c.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var httpClient = CreateClient(client.Object);

        var response = await httpClient.GetAsync("/agreements/agr-1/document");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private HttpClient CreateClient(IFormalizationApiClient formalizationApiClient)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            TestAuth.ConfigureSigningKey(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFormalizationApiClient>();
                services.AddSingleton(formalizationApiClient);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestAuth.TenantId);
        return client;
    }
}
