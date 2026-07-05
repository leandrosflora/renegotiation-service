using System.Net.Http.Json;
using renegotiation_service.Models;

namespace renegotiation_service.Clients;

public class FormalizationApiClient(HttpClient httpClient) : IFormalizationApiClient
{
    public async Task<AgreementConfirmationResult> ConfirmAsync(string simulationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"/simulations/{simulationId}/confirmations", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AgreementConfirmationResult>(cancellationToken: cancellationToken);
        return result ?? new AgreementConfirmationResult(false, "unknown", null);
    }

    public async Task<DocumentResult> GetDocumentAsync(string agreementId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/agreements/{agreementId}/document", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DocumentResult>(cancellationToken: cancellationToken);
        return result ?? new DocumentResult(false, "unknown", null);
    }
}
