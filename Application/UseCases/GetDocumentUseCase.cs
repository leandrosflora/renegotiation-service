using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class GetDocumentUseCase(IFormalizationApiClient client) : IGetDocumentUseCase
{
    public async Task<DocumentResult> ExecuteAsync(string agreementId, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetDocumentAsync(agreementId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("FormalizationApi", ex);
        }
    }
}
