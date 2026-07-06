using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class GetClientUseCase(IClientApiClient client) : IGetClientUseCase
{
    public async Task<ClientLookupResult> ExecuteAsync(string cpf, CancellationToken cancellationToken)
    {
        try
        {
            var data = await client.GetClientAsync(cpf, cancellationToken);
            return new ClientLookupResult(data is not null, data);
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("ClientApi", ex);
        }
    }
}
