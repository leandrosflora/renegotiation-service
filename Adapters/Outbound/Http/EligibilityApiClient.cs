using System.Net.Http.Json;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Adapters.Outbound.Http;

public class EligibilityApiClient(HttpClient httpClient) : IEligibilityApiClient
{
    public async Task<EligibilityResult> CheckEligibilityAsync(string contractId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/contracts/{contractId}/eligibility", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EligibilityResult>(cancellationToken: cancellationToken);
        return result ?? new EligibilityResult(false, "unknown");
    }
}
