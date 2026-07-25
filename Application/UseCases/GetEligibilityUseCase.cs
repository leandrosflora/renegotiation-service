using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Application.UseCases;

public class GetEligibilityUseCase(IEligibilityApiClient client) : IGetEligibilityUseCase
{
    public async Task<EligibilityResult> ExecuteAsync(string contractId, CancellationToken cancellationToken)
    {
        try
        {
            // A contractId that doesn't resolve to a real contract (e.g. the caller passed a raw
            // selection index like "1" instead of resolving it via consultar_contratos first -
            // confirmed live, agent-runtime-renegotiation did exactly this) is a normal "not
            // found" business outcome, not an upstream failure - EnsureSuccessStatusCode would
            // otherwise turn core-bancario-mock's 404 into a misleading 502 here, implying a retry
            // might help when the real problem is the identifier itself.
            var result = await client.CheckEligibilityAsync(contractId, cancellationToken);
            return result ?? new EligibilityResult(false, "contrato_nao_encontrado");
        }
        catch (Exception ex)
        {
            throw new UpstreamServiceUnavailableException("EligibilityApi", ex);
        }
    }
}
