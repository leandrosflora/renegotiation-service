using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class SimulateUseCase(IContractingApiClient client) : ISimulateUseCase
{
    public async Task<SimulationResult> ExecuteAsync(
        string contractId, SimulationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SimulateAsync(contractId, request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("ContractingApi", ex);
        }
    }
}
