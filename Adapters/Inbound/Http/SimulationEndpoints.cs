using renegotiation_service.Application;
using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Domain;

namespace renegotiation_service.Adapters.Inbound.Http;

public static class SimulationEndpoints
{
    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/contracts/{contractId}/simulations", HandleSimulateAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSimulateAsync(
        string contractId,
        SimulationRequest request,
        ISimulateUseCase useCase,
        ILogger<SimulationLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(contractId, request, cancellationToken);
            return Results.Ok(result);
        }
        catch (UpstreamServiceUnavailableException ex)
        {
            logger.LogWarning("{ServiceName} call failed after retries ({ExceptionType})", ex.ServiceName, ex.InnerException?.GetType().Name);
            return Results.Json(
                new ErrorResponse("Contracting API unavailable"), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class SimulationLogCategory;
