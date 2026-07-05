using System.Net.Http.Json;
using renegotiation_service.Models;

namespace renegotiation_service.Clients;

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
