using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface IConfirmAgreementUseCase
{
    Task<AgreementConfirmationResult> ExecuteAsync(
        string simulationId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
