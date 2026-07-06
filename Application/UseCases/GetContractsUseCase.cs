using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class GetContractsUseCase(IClientApiClient client) : IGetContractsUseCase
{
    public async Task<ContractsResult> ExecuteAsync(string clientId, CancellationToken cancellationToken)
    {
        try
        {
            var contracts = await client.GetContractsAsync(clientId, cancellationToken);
            return new ContractsResult(contracts is not null, contracts ?? []);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("ClientApi", ex);
        }
    }
}
