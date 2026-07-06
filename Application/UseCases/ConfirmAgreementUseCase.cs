using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class ConfirmAgreementUseCase(IFormalizationApiClient client) : IConfirmAgreementUseCase
{
    public async Task<AgreementConfirmationResult> ExecuteAsync(string simulationId, CancellationToken cancellationToken)
    {
        try
        {
            return await client.ConfirmAsync(simulationId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("FormalizationApi", ex);
        }
    }
}
