using renegotiation_service.Models;

namespace renegotiation_service.Clients;

public interface IContractingApiClient
{
    Task<SimulationResult> SimulateAsync(string contractId, SimulationRequest request, CancellationToken cancellationToken);
}
