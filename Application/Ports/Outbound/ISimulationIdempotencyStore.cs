namespace renegotiation_service.Application.Ports.Outbound;

public enum IdempotencyAcquireStatus
{
    Acquired,
    InProgress,
    Completed,
    Conflict,
    Ambiguous
}

public sealed record IdempotencyLease(
    IdempotencyAcquireStatus Status,
    string? ResponseJson = null);

public interface ISimulationIdempotencyStore
{
    Task<IdempotencyLease> TryAcquireAsync(
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid tenantId,
        string idempotencyKey,
        string responseJson,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid tenantId,
        string idempotencyKey,
        string errorType,
        CancellationToken cancellationToken);
}
