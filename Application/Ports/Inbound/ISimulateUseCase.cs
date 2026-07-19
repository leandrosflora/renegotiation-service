using renegotiation_service.Domain;

namespace renegotiation_service.Application.Ports.Inbound;

public interface ISimulateUseCase
{
    Task<SimulationResult> ExecuteAsync(
        string contractId,
        SimulationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class IdempotencyInProgressException : Exception
{
    public IdempotencyInProgressException() : base("An execution with this idempotency key is in progress.") { }
}

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException() : base("The idempotency key was already used with different parameters.") { }
}
