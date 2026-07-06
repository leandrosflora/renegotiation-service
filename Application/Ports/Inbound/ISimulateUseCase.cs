using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface ISimulateUseCase
{
    Task<SimulationResult> ExecuteAsync(string contractId, SimulationRequest request, CancellationToken cancellationToken);
}
