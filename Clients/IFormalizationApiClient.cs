using renegotiation_service.Models;

namespace renegotiation_service.Clients;

public interface IFormalizationApiClient
{
    Task<AgreementConfirmationResult> ConfirmAsync(string simulationId, CancellationToken cancellationToken);

    Task<DocumentResult> GetDocumentAsync(string agreementId, CancellationToken cancellationToken);
}
