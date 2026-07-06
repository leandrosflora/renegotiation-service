using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface IGetEligibilityUseCase
{
    Task<EligibilityResult> ExecuteAsync(string contractId, CancellationToken cancellationToken);
}
