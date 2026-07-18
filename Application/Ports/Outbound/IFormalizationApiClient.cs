using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Outbound;

public interface IFormalizationApiClient
{
    Task<AgreementConfirmationResult> ConfirmAsync(
        string simulationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<DocumentResult> GetDocumentAsync(string agreementId, CancellationToken cancellationToken);
}
