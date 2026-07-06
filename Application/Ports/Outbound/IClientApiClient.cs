using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Outbound;

public interface IClientApiClient
{
    /// <summary>Returns null when the Client API reports "not found" (a business outcome, not a failure).</summary>
    Task<ClientData?> GetClientAsync(string cpf, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContractSummary>?> GetContractsAsync(string clientId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DebtItem>?> GetDebtsAsync(string contractId, CancellationToken cancellationToken);
}
