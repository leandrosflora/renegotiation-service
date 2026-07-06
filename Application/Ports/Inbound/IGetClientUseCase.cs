using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface IGetClientUseCase
{
    Task<ClientLookupResult> ExecuteAsync(string cpf, CancellationToken cancellationToken);
}
