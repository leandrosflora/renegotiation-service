using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Outbound;

public interface IEligibilityApiClient
{
    Task<EligibilityResult> CheckEligibilityAsync(string contractId, CancellationToken cancellationToken);
}
