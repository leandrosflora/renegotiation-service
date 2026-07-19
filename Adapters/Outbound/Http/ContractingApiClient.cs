using System.Net.Http.Json;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Adapters.Outbound.Http;

public class ContractingApiClient(HttpClient httpClient) : IContractingApiClient
{
    public async Task<SimulationResult> SimulateAsync(
        string contractId,
        SimulationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/contracts/{contractId}/simulations")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SimulationResult>(cancellationToken: cancellationToken);
        return result ?? new SimulationResult(false, "unknown", null);
    }
}
