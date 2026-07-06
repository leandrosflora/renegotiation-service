using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface IGetDocumentUseCase
{
    Task<DocumentResult> ExecuteAsync(string agreementId, CancellationToken cancellationToken);
}
