using System.Net.Http.Json;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Adapters.Outbound.Http;

public class ContractingApiClient(HttpClient httpClient) : IContractingApiClient
{
    public async Task<SimulationResult> SimulateAsync(
        string contractId, SimulationRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"/contracts/{contractId}/simulations", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SimulationResult>(cancellationToken: cancellationToken);
        return result ?? new SimulationResult(false, "unknown", null);
    }
}
