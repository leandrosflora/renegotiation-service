using System.Net.Http.Json;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Adapters.Outbound.Http;

public class FormalizationApiClient(HttpClient httpClient) : IFormalizationApiClient
{
    public async Task<AgreementConfirmationResult> ConfirmAsync(
        string simulationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/simulations/{simulationId}/confirmations");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
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
