using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Domain;
using renegotiation_service.Platform;

namespace renegotiation_service.Application.UseCases;

public class SimulateUseCase(
    IContractingApiClient client,
    ISimulationIdempotencyStore idempotencyStore,
    TenantContext tenantContext) : ISimulateUseCase
{
    public async Task<SimulationResult> ExecuteAsync(
        string contractId,
        SimulationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse(tenantContext.TenantId);
        var requestHash = ComputeRequestHash(contractId, request);
        var lease = await idempotencyStore.TryAcquireAsync(
            tenantId,
            idempotencyKey,
            requestHash,
            cancellationToken);

        if (lease.Status == IdempotencyAcquireStatus.Conflict)
        {
            throw new IdempotencyConflictException();
        }
        if (lease.Status == IdempotencyAcquireStatus.InProgress)
        {
            throw new IdempotencyInProgressException();
        }
        if (lease.Status == IdempotencyAcquireStatus.Completed)
        {
            return JsonSerializer.Deserialize<SimulationResult>(lease.ResponseJson ?? string.Empty)
                ?? throw new InvalidOperationException("Persisted simulation response is invalid.");
        }

        try
        {
            var result = await client.SimulateAsync(
                contractId,
                request,
                idempotencyKey,
                cancellationToken);
            await idempotencyStore.CompleteAsync(
                tenantId,
                idempotencyKey,
                JsonSerializer.Serialize(result),
                cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            try
            {
                await idempotencyStore.MarkFailedAsync(
                    tenantId,
                    idempotencyKey,
                    ex.GetType().Name,
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original upstream failure; readiness/metrics expose PostgreSQL failures.
            }
            throw new UpstreamServiceUnavailableException("ContractingApi", ex);
        }
    }

    private static string ComputeRequestHash(string contractId, SimulationRequest request)
    {
        var canonical = JsonSerializer.Serialize(new { contractId, request });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
