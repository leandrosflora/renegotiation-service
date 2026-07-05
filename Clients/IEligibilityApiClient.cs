using renegotiation_service.Models;

namespace renegotiation_service.Clients;

public interface IEligibilityApiClient
{
    Task<EligibilityResult> CheckEligibilityAsync(string contractId, CancellationToken cancellationToken);
}
