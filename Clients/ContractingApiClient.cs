using System.Net.Http.Json;
using renegotiation_service.Models;

namespace renegotiation_service.Clients;

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
