using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Outbound;

public interface IContractingApiClient
{
    Task<SimulationResult> SimulateAsync(string contractId, SimulationRequest request, CancellationToken cancellationToken);
}
