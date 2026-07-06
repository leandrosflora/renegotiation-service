using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface IGetContractsUseCase
{
    Task<ContractsResult> ExecuteAsync(string clientId, CancellationToken cancellationToken);
}
