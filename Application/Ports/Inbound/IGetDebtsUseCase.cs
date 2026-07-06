using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface IGetDebtsUseCase
{
    Task<DebtsResult> ExecuteAsync(string contractId, CancellationToken cancellationToken);
}
