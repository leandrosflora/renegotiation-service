using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class GetEligibilityUseCase(IEligibilityApiClient client) : IGetEligibilityUseCase
{
    public async Task<EligibilityResult> ExecuteAsync(string contractId, CancellationToken cancellationToken)
    {
        try
        {
            return await client.CheckEligibilityAsync(contractId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("EligibilityApi", ex);
        }
    }
}
