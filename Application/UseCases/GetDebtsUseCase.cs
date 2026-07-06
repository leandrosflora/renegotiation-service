using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class GetDebtsUseCase(IClientApiClient client) : IGetDebtsUseCase
{
    public async Task<DebtsResult> ExecuteAsync(string contractId, CancellationToken cancellationToken)
    {
        try
        {
            var debts = await client.GetDebtsAsync(contractId, cancellationToken);
            return new DebtsResult(debts is not null, debts ?? []);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("ClientApi", ex);
        }
    }
}
